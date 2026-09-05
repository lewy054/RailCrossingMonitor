using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using RailCrossingMonitor.Application.RailwayTracks;
using RailCrossingMonitor.Application.Trains;
using RailCrossingMonitor.Model;

namespace RailCrossingMonitor.Application.Crossing;

public sealed class RailwayCrossingService(
    HttpClient httpClient,
    IOptions<RailCrossingServiceOptions> options,
    TrainStore trainStore,
    RailwayTrackStore trackStore,
    CrossingStore crossingStore,
    ILogger<RailwayCrossingService> logger)
{
    private static readonly TimeSpan MaxGpsAge =
        TimeSpan.FromSeconds(60);

    private const double MaxPredictionSeconds = 30;

    public Task<List<RailwayCrossingStatus>> GetCrossingStatusesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;

        var crossings = crossingStore
            .GetAll()
            .ToList();

        var trains = trainStore
            .GetAll()
            .Where(IsValidTrain)
            .Where(train =>
                now - train.ReceivedAtUtc <= MaxGpsAge)
            .ToList();

        /*
         * Mapujemy każdy pociąg na tor tylko RAZ.
         *
         * To jest ważne, bo później możemy sprawdzać
         * ten sam pociąg względem setek przejazdów
         * bez ponownego Match().
         */
        var trainContexts = trains
            .Select(train =>
            {
                var match = trackStore.Match(
                    train.Latitude,
                    train.Longitude,
                    train.HeadingDegrees,
                    maxDistanceMeters: 150);

                return new TrainTrackContext(
                    train,
                    match);
            })
            .Where(context => context.Match is not null)
            .ToList();

        var statuses = crossings
            .Select(crossing =>
                CalculateStatus(
                    crossing,
                    trainContexts,
                    now))
            .ToList();

        return Task.FromResult(statuses);
    }

    public async Task LoadCrossingsAsync(
        CancellationToken cancellationToken)
    {
        var url =
            $"{options.Value.Url}" +
            "?SERVICE=WFS" +
            "&VERSION=2.0.0" +
            "&REQUEST=GetFeature" +
            "&TYPENAMES=ms:PKP_PLK" +
            "&SRSNAME=EPSG:4326";

        var stopwatch = Stopwatch.StartNew();

        using var response = await httpClient.GetAsync(
            url,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(
            cancellationToken);

        stopwatch.Stop();

        Console.WriteLine(
            $"WFS: {(int)response.StatusCode} " +
            $"czas={stopwatch.ElapsedMilliseconds}ms " +
            $"bytes={xml.Length}");

        var crossings = ParseXml(xml);

        crossingStore.Set(crossings);

        Console.WriteLine(
            $"Załadowano {crossings.Count} przejazdów kolejowych.");
    }

    private RailwayCrossingStatus CalculateStatus(
        RailwayCrossing crossing,
        IReadOnlyCollection<TrainTrackContext> trains,
        DateTimeOffset now)
    {
        var candidate = FindApproachingTrain(
            crossing,
            trains,
            now);

        if (candidate is null)
        {
            return CreateSafeStatus(crossing);
        }

        var protection = CalculateProtection(
            crossing.Category,
            candidate.EtaSeconds);

        return new RailwayCrossingStatus
        {
            Id = crossing.Id,
            Name = crossing.Name,
            Category = crossing.Category,

            State = protection.State,
            Barriers = protection.Barriers,
            Lights = protection.Lights,

            TrainId = candidate.Train.Id,
            TrainNumber = candidate.Train.Number,
            Carrier = candidate.Train.Carrier,

            DistanceMeters = candidate.DistanceMeters,
            EtaSeconds = candidate.EtaSeconds,

            Latitude = crossing.Latitude,
            Longitude = crossing.Longitude
        };
    }

    private TrainTrackCandidate? FindApproachingTrain(
        RailwayCrossing crossing,
        IReadOnlyCollection<TrainTrackContext> trains,
        DateTimeOffset now)
    {
        /*
         * Jeden przejazd może leżeć na kilku torach.
         *
         * Przykład:
         *
         * tor 101 ----\
         *               X przejazd
         * tor 102 ----/
         *
         * MatchAll() zwróci każdy właściwy tor.
         */
        var crossingTracks = trackStore.MatchAll(
            crossing.Latitude,
            crossing.Longitude,
            maxDistanceMeters: 30);

        if (crossingTracks.Count == 0)
            return null;

        TrainTrackCandidate? best = null;

        foreach (var context in trains)
        {
            var train = context.Train;
            var trainMatch = context.Match;

            if (trainMatch is null)
                continue;

            var elapsed =
                now - train.ReceivedAtUtc;

            /*
             * Nie pozwalamy, żeby stare GPS powodowało
             * nieograniczoną ekstrapolację.
             */
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            if (elapsed > MaxGpsAge)
                continue;

            /*
             * KLUCZOWE:
             *
             * Nie liczymy:
             *
             * GPS + lat/lon + prosta.
             *
             * Liczymy:
             *
             * pozycja na torze + prędkość * czas.
             */
            var predictedDistanceAlongTrack =
                trackStore.PredictDistanceAlongTrack(
                    trainMatch,
                    train.SpeedKmh!.Value,
                    elapsed,
                    MaxPredictionSeconds);
            // logger.LogInformation(
            //     "TRAIN {TrainId}: GPS={Lat:F6},{Lon:F6}, " +
            //     "speed={Speed:F1} km/h, age={Age:F1}s, " +
            //     "track={TrackId}, actualAlong={Actual:F1}m, " +
            //     "predictedAlong={Predicted:F1}m",
            //     train.Id,
            //     train.Latitude,
            //     train.Longitude,
            //     train.SpeedKmh,
            //     elapsed.TotalSeconds,
            //     context.Match.Track.Id,
            //     context.Match.DistanceAlongTrackMeters,
            //     predictedDistanceAlongTrack);
            foreach (var crossingMatch in crossingTracks)
            {
                /*
                 * Pociąg i przejazd muszą być na dokładnie
                 * tym samym torze PLK.
                 */
                if (trainMatch.Track.Id !=
                    crossingMatch.Track.Id)
                {
                    continue;
                }

                /*
                 * Pozycja przejazdu na torze:
                 *
                 * np. 5432 m
                 *
                 * Przewidywana pozycja pociągu:
                 *
                 * np. 4920 m
                 *
                 * => 512 m do przejazdu
                 */
                var distanceAlongTrack =
                    crossingMatch.DistanceAlongTrackMeters -
                    predictedDistanceAlongTrack;

                /*
                 * Jeżeli pociąg jedzie przeciwnie do kierunku
                 * geometrii PLK, odwracamy znak.
                 */
                if (!trainMatch.Forward)
                {
                    distanceAlongTrack = -distanceAlongTrack;
                }

                /*
                 * Pociąg maksymalnie 20 m za przejazdem
                 * traktujemy jako będący już przy przejeździe.
                 */
                if (distanceAlongTrack < -20)
                    continue;

                /*
                 * Nie interesują nas przejazdy absurdalnie
                 * daleko od pociągu.
                 */
                if (distanceAlongTrack > 100_000)
                    continue;

                var speedKmh =
                    train.SpeedKmh.Value;

                if (speedKmh < 5)
                    continue;

                var speedMps =
                    speedKmh / 3.6;

                var etaSeconds =
                    Math.Max(0, distanceAlongTrack) /
                    speedMps;

                var candidate = new TrainTrackCandidate(
                    Train: train,
                    TrainMatch: trainMatch,
                    CrossingMatch: crossingMatch,
                    DistanceMeters: Math.Max(
                        0,
                        distanceAlongTrack),
                    EtaSeconds: etaSeconds);

                if (best is null ||
                    candidate.EtaSeconds <
                    best.EtaSeconds)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    private static RailwayCrossingStatus CreateSafeStatus(
        RailwayCrossing crossing)
    {
        var category =
            NormalizeCategory(crossing.Category);

        return new RailwayCrossingStatus
        {
            Id = crossing.Id,
            Name = crossing.Name,
            Category = crossing.Category,

            State = category == "F"
                ? CrossingState.Stop
                : CrossingState.CanGo,

            Barriers = category switch
            {
                "B" or "E" => BarrierState.Open,
                "F" => BarrierState.Closed,
                _ => BarrierState.None
            },

            Lights = category switch
            {
                "B" or "C" or "E" =>
                    LightState.Off,

                _ =>
                    LightState.None
            },

            Latitude = crossing.Latitude,
            Longitude = crossing.Longitude
        };
    }

    private static bool IsValidTrain(
        TrainPosition train)
    {
        if (train.SpeedKmh is not > 5)
            return false;

        if (train.SpeedKmh > 300)
            return false;

        return true;
    }

    private static ProtectionState CalculateProtection(
        string category,
        double etaSeconds)
    {
        return NormalizeCategory(category) switch
        {
            "A" => CalculateCategoryA(etaSeconds),
            "B" => CalculateCategoryB(etaSeconds),
            "C" => CalculateCategoryC(etaSeconds),
            "D" => CalculateCategoryD(),
            "E" => CalculateCategoryE(etaSeconds),
            "F" => CalculateCategoryF(),

            _ => new ProtectionState(
                CrossingState.Unknown,
                BarrierState.Unknown,
                LightState.Unknown)
        };
    }

    private static ProtectionState CalculateCategoryA(
        double etaSeconds)
    {
        if (etaSeconds > 120)
        {
            return new ProtectionState(
                CrossingState.CanGo,
                BarrierState.Unknown,
                LightState.Off);
        }

        return new ProtectionState(
            CrossingState.Caution,
            BarrierState.Unknown,
            LightState.Unknown);
    }

    private static ProtectionState CalculateCategoryB(
        double etaSeconds)
    {
        const double warningSeconds = 8;
        const double barrierClosingSeconds = 10;

        if (etaSeconds >
            warningSeconds + barrierClosingSeconds)
        {
            return new ProtectionState(
                CrossingState.CanGo,
                BarrierState.Open,
                LightState.Off);
        }

        if (etaSeconds > barrierClosingSeconds)
        {
            return new ProtectionState(
                CrossingState.Caution,
                BarrierState.Open,
                LightState.FlashingRed);
        }

        if (etaSeconds > 0)
        {
            return new ProtectionState(
                CrossingState.Stop,
                BarrierState.Closing,
                LightState.FlashingRed);
        }

        return new ProtectionState(
            CrossingState.Stop,
            BarrierState.Closed,
            LightState.FlashingRed);
    }

    private static ProtectionState CalculateCategoryC(
        double etaSeconds)
    {
        const double warningSeconds = 30;

        if (etaSeconds > warningSeconds)
        {
            return new ProtectionState(
                CrossingState.CanGo,
                BarrierState.None,
                LightState.Off);
        }

        return new ProtectionState(
            CrossingState.Stop,
            BarrierState.None,
            LightState.FlashingRed);
    }

    private static ProtectionState CalculateCategoryD()
    {
        return new ProtectionState(
            CrossingState.CanGo,
            BarrierState.None,
            LightState.None);
    }

    private static ProtectionState CalculateCategoryE(
        double etaSeconds)
    {
        const double warningSeconds = 8;
        const double closingSeconds = 10;

        if (etaSeconds >
            warningSeconds + closingSeconds)
        {
            return new ProtectionState(
                CrossingState.CanGo,
                BarrierState.Open,
                LightState.Off);
        }

        if (etaSeconds > closingSeconds)
        {
            return new ProtectionState(
                CrossingState.Caution,
                BarrierState.Open,
                LightState.FlashingRed);
        }

        if (etaSeconds > 0)
        {
            return new ProtectionState(
                CrossingState.Stop,
                BarrierState.Closing,
                LightState.FlashingRed);
        }

        return new ProtectionState(
            CrossingState.Stop,
            BarrierState.Closed,
            LightState.FlashingRed);
    }

    private static ProtectionState CalculateCategoryF()
    {
        return new ProtectionState(
            CrossingState.Stop,
            BarrierState.Closed,
            LightState.None);
    }

    private static string NormalizeCategory(
        string? category)
    {
        return category?
            .Trim()
            .ToUpperInvariant() ?? "";
    }

    private static List<RailwayCrossing> ParseXml(
        string xml)
    {
        var document = XDocument.Parse(xml);

        XNamespace ms =
            "http://mapserver.gis.umn.edu/mapserver";

        XNamespace gml =
            "http://www.opengis.net/gml/3.2";

        var result =
            new List<RailwayCrossing>();

        foreach (var crossing in
                 document.Descendants(ms + "PKP_PLK"))
        {
            var pos = crossing
                .Descendants(gml + "Point")
                .Descendants(gml + "pos")
                .FirstOrDefault()
                ?.Value;

            if (string.IsNullOrWhiteSpace(pos))
                continue;

            var coordinates = pos.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (coordinates.Length < 2)
                continue;

            if (!double.TryParse(
                    coordinates[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var latitude))
            {
                continue;
            }

            if (!double.TryParse(
                    coordinates[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var longitude))
            {
                continue;
            }

            result.Add(new RailwayCrossing
            {
                Id =
                    crossing
                        .Element(ms + "GML_ID")
                        ?.Value ?? "",

                Name =
                    crossing
                        .Element(ms + "NAZWA")
                        ?.Value ?? "",

                Category =
                    crossing
                        .Element(ms + "KATEGORIA")
                        ?.Value ?? "",

                StationRoute =
                    crossing
                        .Element(ms + "STAC_SZLAK")
                        ?.Value ?? "",

                Manager =
                    crossing
                        .Element(ms + "IDDE")
                        ?.Value ?? "",

                Latitude = latitude,
                Longitude = longitude
            });
        }

        return result;
    }

    private sealed record TrainTrackContext(
        TrainPosition Train,
        TrackMatch? Match);

    private sealed record TrainTrackCandidate(
        TrainPosition Train,
        TrackMatch TrainMatch,
        TrackMatch CrossingMatch,
        double DistanceMeters,
        double EtaSeconds);
}
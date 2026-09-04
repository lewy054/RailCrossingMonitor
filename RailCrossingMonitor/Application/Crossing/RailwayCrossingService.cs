using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using RailCrossingMonitor.Model;

namespace RailCrossingMonitor.Application.Crossing;

public class RailwayCrossingService(
    HttpClient httpClient,
    IOptions<RailCrossingServiceOptions> options,
    TrainStore trainStore,
    CrossingStore crossingStore)
{
    public Task<List<RailwayCrossingStatus>> GetCrossingStatusesAsync(
        CancellationToken cancellationToken)
    {
        var crossings = crossingStore.GetAll();

        var trains = trainStore
            .GetAll()
            .Where(IsValidTrain)
            .ToList();

        var statuses = crossings
            .Select(crossing => CalculateStatus(crossing, trains))
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

        var response = await httpClient.GetAsync(
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
    
    private static RailwayCrossingStatus CreateSafeStatus(
        RailwayCrossing crossing)
    {
        var category = crossing.Category.Trim().ToUpperInvariant();

        return new RailwayCrossingStatus
        {
            Id = crossing.Id,
            Name = crossing.Name,
            Category = crossing.Category,

            State = category == "F"
                ? CrossingState.Stop
                : CrossingState.CanGo,

            Barriers = category == "F"
                ? BarrierState.Closed
                : category is "B" or "E"
                    ? BarrierState.Open
                    : BarrierState.None,

            Lights = LightState.Off,

            Latitude = crossing.Latitude,
            Longitude = crossing.Longitude
        };
    }
    
    private static bool IsValidTrain(TrainPosition train)
    {
        if (train.SpeedKmh is null)
            return false;

        if (train.SpeedKmh <= 5)
            return false;

        if (train.SpeedKmh > 300)
            return false;

        return true;
    }

    private RailwayCrossingStatus CalculateStatus(
        RailwayCrossing crossing,
        IReadOnlyCollection<TrainPosition> trains)
    {
        var train = FindApproachingTrain(crossing, trains);

        if (train is null)
        {
            return CreateSafeStatus(crossing);
        }

        var protection = CalculateProtection(
            crossing.Category,
            train.EtaSeconds!.Value);

        return new RailwayCrossingStatus
        {
            Id = crossing.Id,
            Name = crossing.Name,
            Category = crossing.Category,

            State = protection.State,
            Barriers = protection.Barriers,
            Lights = protection.Lights,

            DistanceMeters = train.DistanceMeters,
            EtaSeconds = train.EtaSeconds,

            Latitude = crossing.Latitude,
            Longitude = crossing.Longitude
        };
    }
    
    private static ProtectionState CalculateProtection(
        string category,
        double etaSeconds)
    {
        return category.Trim().ToUpperInvariant() switch
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

    private static ProtectionState CalculateCategoryA(double etaSeconds)
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

    private static ProtectionState CalculateCategoryB(double etaSeconds)
    {
        const double warningSeconds = 8;
        const double barrierClosingSeconds = 10;

        if (etaSeconds > warningSeconds + barrierClosingSeconds)
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

    private static ProtectionState CalculateCategoryC(double etaSeconds)
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

    private static ProtectionState CalculateCategoryE(double etaSeconds)
    {
        const double warningSeconds = 8;
        const double closingSeconds = 10;

        if (etaSeconds > warningSeconds + closingSeconds)
        {
            return new ProtectionState(
                CrossingState.CanGo,
                BarrierState.Open,
                LightState.Off);
        }

        if (etaSeconds > closingSeconds)
        {
            // Światła ostrzegawcze działają,
            // ale rogatki jeszcze są otwarte.
            return new ProtectionState(
                CrossingState.Caution,
                BarrierState.Open,
                LightState.FlashingRed);
        }

        if (etaSeconds > 0)
        {
            // Rogatki są w trakcie opuszczania.
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

    private static bool IsApproaching(
        double trainLatitude,
        double trainLongitude,
        double crossingLatitude,
        double crossingLongitude,
        double? headingDegrees)
    {
        if (headingDegrees is null)
            return false;

        var bearingToCrossing = CalculateBearing(
            trainLatitude,
            trainLongitude,
            crossingLatitude,
            crossingLongitude);

        var difference = Math.Abs(
            NormalizeAngle(
                bearingToCrossing - headingDegrees.Value));

        return difference <= 45;
    }

    private static double CalculateBearing(
        double lat1,
        double lon1,
        double lat2,
        double lon2)
    {
        var lat1Rad = lat1 * Math.PI / 180.0;
        var lat2Rad = lat2 * Math.PI / 180.0;
        var deltaLon = (lon2 - lon1) * Math.PI / 180.0;

        var y = Math.Sin(deltaLon) * Math.Cos(lat2Rad);

        var x =
            Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
            Math.Sin(lat1Rad) *
            Math.Cos(lat2Rad) *
            Math.Cos(deltaLon);

        var bearing = Math.Atan2(y, x) * 180.0 / Math.PI;

        return (bearing + 360.0) % 360.0;
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= 360.0;

        if (angle > 180)
            angle -= 360;

        if (angle < -180)
            angle += 360;

        return angle;
    }

    private static double CalculateDistanceKm(
        double lat1,
        double lon1,
        double lat2,
        double lon2)
    {
        const double earthRadiusKm = 6371.0;

        var lat1Rad = lat1 * Math.PI / 180.0;
        var lat2Rad = lat2 * Math.PI / 180.0;
        var deltaLat = (lat2 - lat1) * Math.PI / 180.0;
        var deltaLon = (lon2 - lon1) * Math.PI / 180.0;

        var a =
            Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
            Math.Cos(lat1Rad) *
            Math.Cos(lat2Rad) *
            Math.Sin(deltaLon / 2) *
            Math.Sin(deltaLon / 2);

        var c = 2 * Math.Atan2(
            Math.Sqrt(a),
            Math.Sqrt(1 - a));

        return earthRadiusKm * c;
    }

    private static List<RailwayCrossing> ParseXml(string xml)
    {
        var document = XDocument.Parse(xml);

        XNamespace ms = "http://mapserver.gis.umn.edu/mapserver";
        XNamespace gml = "http://www.opengis.net/gml/3.2";

        var result = new List<RailwayCrossing>();

        foreach (var crossing in document.Descendants(ms + "PKP_PLK"))
        {
            var pos = crossing
                .Descendants(gml + "Point")
                .Descendants(gml + "pos")
                .FirstOrDefault()?.Value;

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
                continue;

            if (!double.TryParse(
                    coordinates[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var longitude))
                continue;

            result.Add(new RailwayCrossing
            {
                Id = crossing.Element(ms + "GML_ID")?.Value ?? "",
                Name = crossing.Element(ms + "NAZWA")?.Value ?? "",
                Category = crossing.Element(ms + "KATEGORIA")?.Value ?? "",
                StationRoute = crossing.Element(ms + "STAC_SZLAK")?.Value ?? "",
                Manager = crossing.Element(ms + "IDDE")?.Value ?? "",
                Latitude = latitude,
                Longitude = longitude
            });
        }

        return result;
    }
    
    private static TrainCandidate? FindApproachingTrain(
        RailwayCrossing crossing,
        IReadOnlyCollection<TrainPosition> trains)
    {
        var candidates = trains
            .Where(IsValidTrain)
            .Select(train =>
            {
                var distanceKm = CalculateDistanceKm(
                    train.Latitude,
                    train.Longitude,
                    crossing.Latitude,
                    crossing.Longitude);

                var distanceMeters = distanceKm * 1000.0;

                var approaching = IsApproaching(
                    train.Latitude,
                    train.Longitude,
                    crossing.Latitude,
                    crossing.Longitude,
                    train.HeadingDegrees);

                return new TrainCandidate(
                    train,
                    distanceMeters,
                    approaching);
            })
            .Where(x => x.DistanceMeters <= 3000)
            .Where(x => x.Approaching)
            .Select(x =>
            {
                var speedKmh = x.Train.SpeedKmh!.Value;

                var etaSeconds =
                    x.DistanceMeters / 1000.0
                                     / speedKmh
                    * 3600.0;

                return x with
                {
                    EtaSeconds = etaSeconds
                };
            })
            .Where(x => x.EtaSeconds is >= 0 and <= 600)
            .OrderBy(x => x.EtaSeconds)
            .FirstOrDefault();

        return candidates;
    }
    
    private sealed record TrainCandidate(
        TrainPosition Train,
        double DistanceMeters,
        bool Approaching,
        double? EtaSeconds = null);
}
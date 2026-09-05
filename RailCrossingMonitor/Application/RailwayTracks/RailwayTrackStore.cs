using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using RailCrossingMonitor.Model;

namespace RailCrossingMonitor.Application.RailwayTracks;

public sealed class RailwayTrackStore
{
    private const double EarthRadiusMeters = 6_371_000.0;
    private const double TrackIndexPaddingMeters = 200.0;

    private readonly ILogger<RailwayTrackStore> _logger;
    private readonly object _lock = new();

    private List<RailwayTrack> _tracks = [];
    private STRtree<TrackSegment> _index = new();

    public RailwayTrackStore(
        ILogger<RailwayTrackStore> logger)
    {
        _logger = logger;
    }

    public IReadOnlyCollection<RailwayTrack> GetAll()
    {
        lock (_lock)
        {
            return _tracks.ToArray();
        }
    }

    public async Task LoadAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        const string url =
            "https://mapa.plk-sa.pl/geoserver/ows" +
            "?service=WFS" +
            "&version=2.0.0" +
            "&request=GetFeature" +
            "&typeNames=wektory:TORY_UZYTKOWANE" +
            "&outputFormat=application/json" +
            "&srsName=EPSG:4326";

        _logger.LogInformation(
            "Downloading railway tracks from PLK WFS.");

        using var response = await httpClient.GetAsync(
            url,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        var data =
            await JsonSerializer.DeserializeAsync<PlkTrackFeatureCollection>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                },
                cancellationToken);

        if (data is null)
        {
            throw new InvalidOperationException(
                "PLK WFS returned empty response.");
        }

        var tracks = data.Features
            .Select(ConvertTrack)
            .Where(track => track is not null)
            .Cast<RailwayTrack>()
            .ToList();

        if (tracks.Count == 0)
        {
            throw new InvalidOperationException(
                "PLK WFS returned no valid railway tracks.");
        }

        var index = BuildIndex(tracks);

        lock (_lock)
        {
            _tracks = tracks;
            _index = index;
        }

        _logger.LogInformation(
            "Loaded {Count} railway tracks from PLK.",
            tracks.Count);
    }

    public TrackMatch? Match(
        double latitude,
        double longitude,
        double? headingDegrees = null,
        double maxDistanceMeters = 150)
    {
        var index = GetIndex();

        if (index.IsEmpty)
            return null;

        var envelope = CreateSearchEnvelope(
            longitude,
            latitude,
            maxDistanceMeters);

        var segments = index.Query(envelope);

        TrackMatch? best = null;

        foreach (var segment in segments)
        {
            var match = MatchSegment(
                segment,
                latitude,
                longitude,
                headingDegrees);

            if (match.DistanceToTrackMeters > maxDistanceMeters)
                continue;

            if (best is null ||
                IsBetterMatch(match, best, headingDegrees))
            {
                best = match;
            }
        }

        return best;
    }

    public IReadOnlyList<TrackMatch> MatchAll(
        double latitude,
        double longitude,
        double maxDistanceMeters = 30)
    {
        var index = GetIndex();

        if (index.IsEmpty)
            return [];

        var envelope = CreateSearchEnvelope(
            longitude,
            latitude,
            maxDistanceMeters);

        var segments = index.Query(envelope);

        var results = new List<TrackMatch>();

        foreach (var segment in segments)
        {
            var match = MatchSegment(
                segment,
                latitude,
                longitude,
                headingDegrees: null);

            if (match.DistanceToTrackMeters > maxDistanceMeters)
                continue;

            results.Add(match);
        }

        return results
            .GroupBy(match => match.Track.Id)
            .Select(group =>
                group
                    .OrderBy(match => match.DistanceToTrackMeters)
                    .First())
            .OrderBy(match => match.DistanceToTrackMeters)
            .ToArray();
    }

    public TrackMatch? MatchOnSpecificTrack(
        RailwayTrack track,
        double latitude,
        double longitude,
        double? headingDegrees = null,
        double maxDistanceMeters = 100)
    {
        TrackMatch? best = null;

        for (var i = 0; i < track.Points.Count - 1; i++)
        {
            var segment = new TrackSegment(
                Track: track,
                Start: track.Points[i],
                End: track.Points[i + 1],
                StartDistanceMeters: track.CumulativeMeters[i]);

            var match = MatchSegment(
                segment,
                latitude,
                longitude,
                headingDegrees);

            if (match.DistanceToTrackMeters > maxDistanceMeters)
                continue;

            if (best is null ||
                IsBetterMatch(match, best, headingDegrees))
            {
                best = match;
            }
        }

        return best;
    }

    /// <summary>
    /// Przewiduje pozycję pociągu wzdłuż toru.
    ///
    /// Nie przesuwamy pociągu po prostej GPS.
    /// Przesuwamy go o speed * time po geometrii
    /// toru pobranej z PLK.
    /// </summary>
    public double PredictDistanceAlongTrack(
        TrackMatch match,
        double speedKmh,
        TimeSpan elapsed,
        double maxPredictionSeconds = 30)
    {
        var seconds = Math.Clamp(
            elapsed.TotalSeconds,
            0,
            maxPredictionSeconds);

        var speedMps = Math.Max(0, speedKmh) / 3.6;

        var distanceTravelled =
            speedMps * seconds;

        return match.Forward
            ? match.DistanceAlongTrackMeters + distanceTravelled
            : match.DistanceAlongTrackMeters - distanceTravelled;
    }

    public double? DistanceAlongTrack(
        TrackMatch from,
        double latitude,
        double longitude,
        double maxDistanceMeters = 100)
    {
        var target = MatchOnSpecificTrack(
            from.Track,
            latitude,
            longitude,
            from.Forward
                ? from.TrackBearingDegrees
                : NormalizeBearing(
                    from.TrackBearingDegrees + 180),
            maxDistanceMeters);

        if (target is null)
            return null;

        var distance =
            target.DistanceAlongTrackMeters -
            from.DistanceAlongTrackMeters;

        if (!from.Forward)
            distance = -distance;

        return distance;
    }

    private STRtree<TrackSegment> GetIndex()
    {
        lock (_lock)
        {
            return _index;
        }
    }

    private static STRtree<TrackSegment> BuildIndex(
        IReadOnlyCollection<RailwayTrack> tracks)
    {
        var index = new STRtree<TrackSegment>();

        foreach (var track in tracks)
        {
            AddTrackToIndex(index, track);
        }

        index.Build();

        return index;
    }

    private static TrackMatch MatchSegment(
        TrackSegment segment,
        double latitude,
        double longitude,
        double? headingDegrees)
    {
        var projection = ProjectPointToSegment(
            latitude,
            longitude,
            segment.Start,
            segment.End);

        var bearing = CalculateBearing(
            segment.Start.Latitude,
            segment.Start.Longitude,
            segment.End.Latitude,
            segment.End.Longitude);

        var forward = DetermineDirection(
            bearing,
            headingDegrees);

        var distanceAlongTrack =
            segment.StartDistanceMeters +
            projection.SegmentDistanceMeters;

        return new TrackMatch(
            Track: segment.Track,
            Longitude: projection.Longitude,
            Latitude: projection.Latitude,
            DistanceAlongTrackMeters: distanceAlongTrack,
            DistanceToTrackMeters: projection.DistanceMeters,
            TrackBearingDegrees: bearing,
            Forward: forward);
    }

    private static bool IsBetterMatch(
        TrackMatch candidate,
        TrackMatch current,
        double? headingDegrees)
    {
        if (candidate.DistanceToTrackMeters <
            current.DistanceToTrackMeters - 5)
        {
            return true;
        }

        if (headingDegrees is null)
            return false;

        var candidateDirectionDifference =
            AngularDifference(
                candidate.TrackBearingDegrees,
                headingDegrees.Value);

        var currentDirectionDifference =
            AngularDifference(
                current.TrackBearingDegrees,
                headingDegrees.Value);

        return candidateDirectionDifference <
               currentDirectionDifference;
    }

    private static RailwayTrack? ConvertTrack(
        PlkTrackFeature feature)
    {
        var properties = feature.Properties;
        var geometry = feature.Geometry;

        if (properties is null ||
            geometry is null ||
            string.IsNullOrWhiteSpace(geometry.Type))
        {
            return null;
        }

        var points = geometry.Type.Trim().ToLowerInvariant() switch
        {
            "linestring" =>
                ParseLineString(geometry.Coordinates),

            "multilinestring" =>
                ParseMultiLineString(geometry.Coordinates),

            _ =>
                []
        };

        if (points.Length < 2)
            return null;

        var cumulativeMeters =
            CalculateCumulativeDistances(points);

        return new RailwayTrack
        {
            Id = properties.ID,
            LineId = properties.ID_LINII,
            Number = properties.NUMER,
            TrackNumber = properties.NR_TORU_LINII,
            Direction = properties.KIER_TORU ?? "",
            Name = properties.NAZWA ?? "",
            StartKm = properties.KM_P,
            EndKm = properties.KM_K,
            LengthKm = properties.DLUGOSC,
            Points = points,
            CumulativeMeters = cumulativeMeters
        };
    }
    
    private static TrackPoint[] ParseLineString(
        JsonElement coordinates)
    {
        if (coordinates.ValueKind != JsonValueKind.Array)
            return [];

        var points = new List<TrackPoint>();

        foreach (var coordinate in coordinates.EnumerateArray())
        {
            if (TryParsePoint(coordinate, out var point))
            {
                points.Add(point);
            }
        }

        return points.ToArray();
    }

    private static TrackPoint[] ParseMultiLineString(
        JsonElement coordinates)
    {
        if (coordinates.ValueKind != JsonValueKind.Array)
            return [];

        var points = new List<TrackPoint>();

        foreach (var line in coordinates.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var coordinate in line.EnumerateArray())
            {
                if (TryParsePoint(coordinate, out var point))
                {
                    points.Add(point);
                }
            }
        }

        return points.ToArray();
    }

    private static bool TryParsePoint(
        JsonElement coordinate,
        out TrackPoint point)
    {
        point = default!;

        if (coordinate.ValueKind != JsonValueKind.Array)
            return false;

        var values = coordinate.EnumerateArray().ToArray();

        if (values.Length < 2)
            return false;

        if (!values[0].TryGetDouble(out var longitude) ||
            !values[1].TryGetDouble(out var latitude))
        {
            return false;
        }

        if (double.IsNaN(latitude) ||
            double.IsNaN(longitude) ||
            double.IsInfinity(latitude) ||
            double.IsInfinity(longitude))
        {
            return false;
        }

        point = new TrackPoint(
            Longitude: longitude,
            Latitude: latitude);

        return true;
    }

    private static double[] CalculateCumulativeDistances(
        IReadOnlyList<TrackPoint> points)
    {
        var cumulative = new double[points.Count];

        for (var i = 1; i < points.Count; i++)
        {
            cumulative[i] =
                cumulative[i - 1] +
                HaversineMeters(
                    points[i - 1].Latitude,
                    points[i - 1].Longitude,
                    points[i].Latitude,
                    points[i].Longitude);
        }

        return cumulative;
    }

    private static void AddTrackToIndex(
        STRtree<TrackSegment> index,
        RailwayTrack track)
    {
        for (var i = 0; i < track.Points.Count - 1; i++)
        {
            var start = track.Points[i];
            var end = track.Points[i + 1];

            var envelope = CreateSegmentEnvelope(
                start,
                end,
                TrackIndexPaddingMeters);

            index.Insert(
                envelope,
                new TrackSegment(
                    Track: track,
                    Start: start,
                    End: end,
                    StartDistanceMeters: track.CumulativeMeters[i]));
        }
    }

    private static Envelope CreateSearchEnvelope(
        double longitude,
        double latitude,
        double distanceMeters)
    {
        var latDelta =
            distanceMeters / 111_320.0;

        var cosLatitude =
            Math.Cos(latitude * Math.PI / 180.0);

        var lonDelta =
            distanceMeters /
            (111_320.0 * Math.Max(cosLatitude, 0.01));

        return new Envelope(
            longitude - lonDelta,
            longitude + lonDelta,
            latitude - latDelta,
            latitude + latDelta);
    }

    private static Envelope CreateSegmentEnvelope(
        TrackPoint start,
        TrackPoint end,
        double paddingMeters)
    {
        var latitude =
            (start.Latitude + end.Latitude) / 2.0;

        var latPadding =
            paddingMeters / 111_320.0;

        var cosLatitude =
            Math.Cos(latitude * Math.PI / 180.0);

        var lonPadding =
            paddingMeters /
            (111_320.0 * Math.Max(cosLatitude, 0.01));

        return new Envelope(
            Math.Min(start.Longitude, end.Longitude) - lonPadding,
            Math.Max(start.Longitude, end.Longitude) + lonPadding,
            Math.Min(start.Latitude, end.Latitude) - latPadding,
            Math.Max(start.Latitude, end.Latitude) + latPadding);
    }

    private static ProjectionResult ProjectPointToSegment(
        double latitude,
        double longitude,
        TrackPoint start,
        TrackPoint end)
    {
        var latitudeRadians =
            latitude * Math.PI / 180.0;

        const double metersPerDegreeLatitude = 111_320.0;

        var metersPerDegreeLongitude =
            111_320.0 *
            Math.Cos(latitudeRadians);

        var px =
            (longitude - start.Longitude) *
            metersPerDegreeLongitude;

        var py =
            (latitude - start.Latitude) *
            metersPerDegreeLatitude;

        var ex =
            (end.Longitude - start.Longitude) *
            metersPerDegreeLongitude;

        var ey =
            (end.Latitude - start.Latitude) *
            metersPerDegreeLatitude;

        var lengthSquared =
            ex * ex + ey * ey;

        var t = lengthSquared <= 0.000001
            ? 0
            : (px * ex + py * ey) / lengthSquared;

        t = Math.Clamp(t, 0, 1);

        var projectedX = t * ex;
        var projectedY = t * ey;

        var distanceX = px - projectedX;
        var distanceY = py - projectedY;

        var distanceMeters =
            Math.Sqrt(
                distanceX * distanceX +
                distanceY * distanceY);

        var projectedLongitude =
            start.Longitude +
            (end.Longitude - start.Longitude) * t;

        var projectedLatitude =
            start.Latitude +
            (end.Latitude - start.Latitude) * t;

        var segmentLengthMeters =
            Math.Sqrt(lengthSquared);

        return new ProjectionResult(
            Longitude: projectedLongitude,
            Latitude: projectedLatitude,
            DistanceMeters: distanceMeters,
            SegmentDistanceMeters:
                segmentLengthMeters * t);
    }

    private static bool DetermineDirection(
        double trackBearing,
        double? trainHeading)
    {
        if (trainHeading is null)
            return true;

        var difference =
            AngularDifference(
                trackBearing,
                trainHeading.Value);

        return difference <= 90;
    }

    private static double AngularDifference(
        double a,
        double b)
    {
        var difference =
            Math.Abs(a - b) % 360;

        return difference > 180
            ? 360 - difference
            : difference;
    }

    private static double NormalizeBearing(
        double bearing)
    {
        bearing %= 360;

        if (bearing < 0)
            bearing += 360;

        return bearing;
    }

    private static double CalculateBearing(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        var lat1 =
            latitude1 * Math.PI / 180.0;

        var lat2 =
            latitude2 * Math.PI / 180.0;

        var deltaLongitude =
            (longitude2 - longitude1) *
            Math.PI / 180.0;

        var y =
            Math.Sin(deltaLongitude) *
            Math.Cos(lat2);

        var x =
            Math.Cos(lat1) *
            Math.Sin(lat2) -
            Math.Sin(lat1) *
            Math.Cos(lat2) *
            Math.Cos(deltaLongitude);

        var bearing =
            Math.Atan2(y, x) *
            180.0 / Math.PI;

        return NormalizeBearing(bearing);
    }

    private static double HaversineMeters(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        var lat1 =
            latitude1 * Math.PI / 180.0;

        var lat2 =
            latitude2 * Math.PI / 180.0;

        var deltaLatitude =
            (latitude2 - latitude1) *
            Math.PI / 180.0;

        var deltaLongitude =
            (longitude2 - longitude1) *
            Math.PI / 180.0;

        var a =
            Math.Sin(deltaLatitude / 2) *
            Math.Sin(deltaLatitude / 2) +
            Math.Cos(lat1) *
            Math.Cos(lat2) *
            Math.Sin(deltaLongitude / 2) *
            Math.Sin(deltaLongitude / 2);

        var c =
            2 * Math.Atan2(
                Math.Sqrt(a),
                Math.Sqrt(1 - a));

        return EarthRadiusMeters * c;
    }

    private sealed record TrackSegment(
        RailwayTrack Track,
        TrackPoint Start,
        TrackPoint End,
        double StartDistanceMeters);

    private readonly record struct ProjectionResult(
        double Longitude,
        double Latitude,
        double DistanceMeters,
        double SegmentDistanceMeters);
    
    public TrackPoint? GetPointAtDistanceAlongTrack(
        RailwayTrack track,
        double distanceAlongTrackMeters)
    {
        if (track.Points.Count < 2 ||
            track.CumulativeMeters.Count != track.Points.Count)
        {
            return null;
        }

        var totalLength =
            track.CumulativeMeters[^1];

        distanceAlongTrackMeters = Math.Clamp(
            distanceAlongTrackMeters,
            0,
            totalLength);

        for (var i = 1; i < track.CumulativeMeters.Count; i++)
        {
            var segmentStartDistance =
                track.CumulativeMeters[i - 1];

            var segmentEndDistance =
                track.CumulativeMeters[i];

            if (distanceAlongTrackMeters > segmentEndDistance)
                continue;

            var segmentLength =
                segmentEndDistance -
                segmentStartDistance;

            if (segmentLength <= 0)
                return track.Points[i];

            var t =
                (distanceAlongTrackMeters - segmentStartDistance) /
                segmentLength;

            var start = track.Points[i - 1];
            var end = track.Points[i];

            return new TrackPoint(
                Longitude:
                start.Longitude +
                (end.Longitude - start.Longitude) * t,

                Latitude:
                start.Latitude +
                (end.Latitude - start.Latitude) * t);
        }

        return track.Points[^1];
    }
}
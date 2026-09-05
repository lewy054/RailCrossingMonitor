namespace RailCrossingMonitor.Model;

public sealed class RailwayTrack
{
    public int Id { get; init; }

    public int LineId { get; init; }

    public int Number { get; init; }

    public int TrackNumber { get; init; }

    public string Direction { get; init; } = "";

    public string Name { get; init; } = "";

    public double StartKm { get; init; }

    public double EndKm { get; init; }

    public double LengthKm { get; init; }

    /// <summary>
    /// Kolejne punkty geometrii toru [longitude, latitude].
    /// </summary>
    public IReadOnlyList<TrackPoint> Points { get; init; }
        = Array.Empty<TrackPoint>();

    /// <summary>
    /// Narastająca odległość od początku geometrii w metrach.
    /// Index odpowiada Points.
    /// </summary>
    public IReadOnlyList<double> CumulativeMeters { get; init; }
        = Array.Empty<double>();
}

public readonly record struct TrackPoint(
    double Longitude,
    double Latitude);

public sealed record TrackMatch(
    RailwayTrack Track,

    /// <summary>
    /// Punkt GPS rzutowany na tor.
    /// </summary>
    double Longitude,
    double Latitude,

    /// <summary>
    /// Odległość od początku geometrii toru.
    /// </summary>
    double DistanceAlongTrackMeters,

    /// <summary>
    /// Odległość GPS → tor.
    /// </summary>
    double DistanceToTrackMeters,

    /// <summary>
    /// Kierunek toru w punkcie dopasowania.
    /// </summary>
    double TrackBearingDegrees,

    /// <summary>
    /// Czy pociąg jedzie w kierunku rosnącego kilometrażu/geometrii.
    /// </summary>
    bool Forward);
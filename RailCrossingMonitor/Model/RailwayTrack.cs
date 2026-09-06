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
    public IReadOnlyList<TrackPoint> Points { get; init; } = [];
    public IReadOnlyList<double> CumulativeMeters { get; init; } = [];
}
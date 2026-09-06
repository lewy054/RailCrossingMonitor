namespace RailCrossingMonitor.Model;

public class TrainPosition
{
    public long Id { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Status { get; set; }
    public int Info { get; set; }
    public string Carrier { get; set; }
    public string Number { get; set; }
    public object? Code { get; set; }
    public double Angle { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; } = DateTimeOffset.UtcNow;
    public double? SpeedKmh { get; set; }
    public double? HeadingDegrees { get; set; }
}
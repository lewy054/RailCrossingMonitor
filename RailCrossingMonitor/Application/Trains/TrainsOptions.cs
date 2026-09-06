namespace RailCrossingMonitor.Application.Trains;

public class TrainsOptions
{
    public const string SectionName = "TrainsOptions";
    public required string DefaultUrl { get; set; }
    public required string TokenUrl { get; set; }
    public required string HubUrl { get; set; }
}
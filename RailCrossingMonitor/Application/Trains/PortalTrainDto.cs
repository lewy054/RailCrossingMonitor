namespace RailCrossingMonitor.Application.Trains;

using System.Text.Json.Serialization;

public sealed class PortalTrainDto
{
    [JsonPropertyName("t")]
    public long Id { get; set; }

    [JsonPropertyName("s")]
    public double Latitude { get; set; }

    [JsonPropertyName("d")]
    public double Longitude { get; set; }

    [JsonPropertyName("o")]
    public int Status { get; set; }

    [JsonPropertyName("i")]
    public int Info { get; set; }

    [JsonPropertyName("p")]
    public string? Carrier { get; set; }

    [JsonPropertyName("n")]
    public string? Number { get; set; }

    [JsonPropertyName("c")]
    public object? Code { get; set; }

    [JsonPropertyName("a")]
    public double Angle { get; set; }
}

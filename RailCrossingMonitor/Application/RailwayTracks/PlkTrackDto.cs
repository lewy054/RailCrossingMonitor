using System.Text.Json;
using System.Text.Json.Serialization;

namespace RailCrossingMonitor.Application.RailwayTracks;

internal sealed class PlkTrackFeatureCollection
{
    [JsonPropertyName("features")]
    public List<PlkTrackFeature> Features { get; init; } = [];
}

internal sealed class PlkTrackFeature
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("geometry")]
    public PlkGeometry? Geometry { get; init; }

    [JsonPropertyName("properties")]
    public PlkTrackProperties? Properties { get; init; }
}

internal sealed class PlkGeometry
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("coordinates")]
    public JsonElement Coordinates { get; init; }
}

internal sealed class PlkTrackProperties
{
    [JsonPropertyName("ID")]
    public int Id { get; init; }

    [JsonPropertyName("ID_LINII")]
    public int LineId { get; init; }

    [JsonPropertyName("NUMER")]
    public int Number { get; init; }

    [JsonPropertyName("NR_TORU_LINII")]
    public int LineTrackNumber { get; init; }

    [JsonPropertyName("KIER_TORU")]
    public string? TrackDirection { get; init; }

    [JsonPropertyName("NAZWA")]
    public string? Name { get; init; }

    [JsonPropertyName("KM_P")]
    public double KM_P { get; init; }

    [JsonPropertyName("KM_K")]
    public double KM_K { get; init; }

    [JsonPropertyName("DLUGOSC")]
    public double Lenght { get; init; }
}
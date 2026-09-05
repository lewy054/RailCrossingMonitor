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
    public int ID { get; init; }

    [JsonPropertyName("ID_LINII")]
    public int ID_LINII { get; init; }

    [JsonPropertyName("NUMER")]
    public int NUMER { get; init; }

    [JsonPropertyName("NR_TORU_LINII")]
    public int NR_TORU_LINII { get; init; }

    [JsonPropertyName("KIER_TORU")]
    public string? KIER_TORU { get; init; }

    [JsonPropertyName("NAZWA")]
    public string? NAZWA { get; init; }

    [JsonPropertyName("KM_P")]
    public double KM_P { get; init; }

    [JsonPropertyName("KM_K")]
    public double KM_K { get; init; }

    [JsonPropertyName("DLUGOSC")]
    public double DLUGOSC { get; init; }
}
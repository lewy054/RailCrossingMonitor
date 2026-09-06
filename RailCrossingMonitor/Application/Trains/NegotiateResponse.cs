using System.Text.Json.Serialization;

namespace RailCrossingMonitor.Application.Trains;

public record NegotiateResponse
{
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("accessToken")] public string? AccessToken { get; init; }
}
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace RailCrossingMonitor.Application.Trains;

public sealed class TrainsWorker(
    IHttpClientFactory httpClientFactory,
    TrainStore store,
    ILogger<TrainsWorker> logger,
    IOptions<TrainsOptions> options)
    : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    private const string Language = "PL";
    private const double Zoom = 7.022935189742923;

    private const double MinLatitude = 48.29702653973435;
    private const double MinLongitude = 13.44729945766156;

    private const double MaxLatitude = 55.52329783923743;
    private const double MaxLongitude = 26.55270054233844;

    private const int SelectedTrainNumber = 0;
    private const bool ForcedRefresh = true;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Trains worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Trains worker failed. Retrying in {RetryDelay}.", RetryDelay);

                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        logger.LogInformation("Trains worker stopped.");
    }

    private async Task RunConnectionAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting Portal Pasażera connection cycle.");

        var connectionParameters = await GetConnectionParametersAsync(cancellationToken);
        var negotiate = await NegotiateAsync(cancellationToken);

        await using var connection = CreateConnection(negotiate);
        RegisterHandlers(connection, connectionParameters);
        logger.LogInformation("Connecting to Portal Pasażera SignalR...");
        await connection.StartAsync(cancellationToken);
        logger.LogInformation("Portal Pasażera SignalR connected. ConnectionId={ConnectionId}",
            connection.ConnectionId);

        await RegisterParametersAsync(connection, connectionParameters, cancellationToken);
        logger.LogInformation("Portal Pasażera registration completed.");
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        finally
        {
            logger.LogInformation("Stopping Portal Pasażera SignalR connection.");

            try
            {
                await connection.StopAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error while stopping Portal Pasażera SignalR connection.");
            }
        }
    }

    private async Task<NegotiateResponse> NegotiateAsync(
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Negotiating Portal Pasażera SignalR connection...");

        var httpClient = httpClientFactory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.HubUrl);
        request.Content = JsonContent.Create(new { });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"SignalR negotiate failed. " +
                $"Status={(int)response.StatusCode} " +
                $"({response.ReasonPhrase}). " +
                $"Response={responseBody}");
        }

        var negotiate = JsonSerializer.Deserialize<NegotiateResponse>(responseBody, JsonOptions);

        if (negotiate is null)
        {
            throw new InvalidOperationException(
                "SignalR negotiate returned an empty response.");
        }

        if (string.IsNullOrWhiteSpace(negotiate.Url))
        {
            throw new InvalidOperationException(
                "SignalR negotiate response does not contain 'url'.");
        }

        if (string.IsNullOrWhiteSpace(negotiate.AccessToken))
        {
            throw new InvalidOperationException(
                "SignalR negotiate response does not contain 'accessToken'.");
        }

        logger.LogDebug("SignalR negotiate successful. Url={Url}", negotiate.Url);
        return negotiate;
    }

    private static HubConnection CreateConnection(NegotiateResponse negotiate)
    {
        return new HubConnectionBuilder().WithUrl(negotiate.Url,
                httpOptions =>
                {
                    httpOptions.AccessTokenProvider = () => Task.FromResult(negotiate.AccessToken);
                })
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30)
            ])
            .Build();
    }

    private void RegisterHandlers(
        HubConnection connection,
        ConnectionParameters parameters)
    {
        // connection.On<string, List<PortalTrainDto>>(
        //     "TrainStatus",
        //     ProcessTrainStatus);
        connection.On<JsonElement>("TrainStatus", payload =>
        {
            logger.LogInformation(
                "!!! RAW TrainStatus RECEIVED !!! Kind={Kind}, Payload={Payload}",
                payload.ValueKind,
                payload.ToString());

            try
            {
                if (payload.ValueKind != JsonValueKind.Array)
                {
                    logger.LogWarning("TrainStatus payload is not an array. Kind={Kind}", payload.ValueKind);
                    return;
                }

                logger.LogInformation("TrainStatus array length: {Length}", payload.GetArrayLength());

                if (payload.GetArrayLength() < 2)
                {
                    logger.LogWarning("TrainStatus payload has less than 2 elements.");
                    return;
                }

                var source = payload[0].GetString() ?? string.Empty;

                var trains = payload[1].Deserialize<List<PortalTrainDto>>(
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                logger.LogInformation("TrainStatus parsed. Source={Source}, Count={Count}", source, trains?.Count ?? 0);
                if (trains is null)
                {
                    logger.LogWarning("Unable to deserialize trains.");
                    return;
                }

                ProcessTrainStatus(source, trains);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing raw TrainStatus payload.");
            }
        });

        connection.Reconnecting += error =>
        {
            logger.LogWarning(error, "Portal Pasażera SignalR reconnecting...");
            return Task.CompletedTask;
        };

        connection.Reconnected += async connectionId =>
        {
            logger.LogInformation("Portal Pasażera SignalR reconnected. ConnectionId={ConnectionId}", connectionId);

            try
            {
                await RegisterParametersAsync(connection, parameters, CancellationToken.None);

                logger.LogInformation("Portal Pasażera parameters registered again after reconnect.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to register Portal Pasażera parameters after reconnect.");
            }
        };

        connection.Closed += error =>
        {
            if (error is null)
            {
                logger.LogWarning("Portal Pasażera SignalR connection closed.");
            }
            else
            {
                logger.LogWarning(error, "Portal Pasażera SignalR connection closed because of an error.");
            }

            return Task.CompletedTask;
        };
    }

    private async Task RegisterParametersAsync(HubConnection connection, ConnectionParameters parameters,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Registering Portal Pasażera parameters. TID={Tid}, PID={Pid}", parameters.Tid, parameters.Pid);
        await connection.InvokeAsync("RegisterParams", Language, Zoom, MinLatitude, MinLongitude, MaxLatitude,
            MaxLongitude, SelectedTrainNumber, ForcedRefresh, parameters.Tid, parameters.Pid, cancellationToken);
        logger.LogDebug("Portal Pasażera parameters registered.");
    }

    private void ProcessTrainStatus(string source, List<PortalTrainDto> trains)
    {
        if (trains.Count == 0)
        {
            logger.LogDebug("Received empty TrainStatus from {Source}.", source);
            return;
        }

        try
        {
            store.Upsert(trains);
            logger.LogDebug("TrainStatus processed. Source={Source}, Count={Count}", source, trains.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store TrainStatus. Source={Source}, Count={Count}", source, trains.Count);
        }
    }

    private async Task<ConnectionParameters> GetConnectionParametersAsync(
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Fetching Portal Pasażera connection parameters from {Url}.", options.Value.TokenUrl);

        var httpClient = httpClientFactory.CreateClient();

        using var response = await httpClient.GetAsync(options.Value.TokenUrl, cancellationToken);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Failed to fetch Portal Pasażera parameters. " +
                $"Status={(int)response.StatusCode} " +
                $"({response.ReasonPhrase}).");
        }

        var tid = ExtractJavaScriptVariable(html, "TID");
        var pid = ExtractJavaScriptVariable(html, "PID");

        logger.LogDebug("Portal Pasażera parameters retrieved successfully.");

        return new ConnectionParameters(tid, pid);
    }

    private static string ExtractJavaScriptVariable(string html, string variableName)
    {
        var pattern = $@"\b(?:var|let|const)\s+{Regex.Escape(variableName)}\s*=\s*[""']([^""']+)[""']";

        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Portal Pasażera page does not contain JavaScript variable '{variableName}'.");
        }

        return match.Groups[1].Value;
    }

    private sealed record ConnectionParameters(string Tid, string Pid);

    private sealed record NegotiateResponse(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("accessToken")]
        string? AccessToken);
}
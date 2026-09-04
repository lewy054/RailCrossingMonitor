using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using RailCrossingMonitor.Models;

namespace RailCrossingMonitor.Application;

public sealed class TrainsWorker(
    IHttpClientFactory httpClientFactory,
    TrainStore store,
    ILogger<TrainsWorker> logger,
    IOptions<TrainsOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        logger.LogInformation("Trains worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectToPortal(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error connecting to Portal Pasażera.");
                logger.LogInformation("Retrying in 10 seconds...");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        logger.LogInformation(
            "TrainsWorker stopped.");
    }

    private async Task ConnectToPortal(CancellationToken cancellationToken)
    {
        logger.LogInformation("Negotiating Portal Pasażera SignalR connection...");
        var http = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.HubUrl);
        request.Content = JsonContent.Create(new { });
        using var response = await http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Negotiate failed: {(int)response.StatusCode} " +
                $"{response.ReasonPhrase}. " +
                $"Response: {responseBody}");
        }

        var negotiate = JsonSerializer.Deserialize<NegotiateResponse>(responseBody,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (negotiate is null)
        {
            throw new InvalidOperationException(
                "Empty negotiate response.");
        }

        if (string.IsNullOrWhiteSpace(negotiate.url))
        {
            throw new InvalidOperationException(
                "Negotiate response does not contain 'url'.");
        }

        if (string.IsNullOrWhiteSpace(negotiate.accessToken))
        {
            throw new InvalidOperationException(
                "Negotiate response does not contain 'accessToken'.");
        }

        logger.LogInformation("Negotiate successful.");
        logger.LogDebug("SignalR URL: {Url}", negotiate.url);
        var connection = new HubConnectionBuilder().WithUrl(negotiate.url,
                httpOptions =>
                {
                    httpOptions.AccessTokenProvider = () => Task.FromResult<string?>(negotiate.accessToken);
                })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddConsole();
            })
            .WithAutomaticReconnect()
            .Build();


        connection.On<string, List<PortalTrainDto>>("TrainStatus", ProcessTrainStatus);
        connection.On<JsonElement>("TrainStatus", payload =>
        {
            try
            {
                if (payload.ValueKind !=
                    JsonValueKind.Array)
                {
                    return;
                }

                if (payload.GetArrayLength() < 2)
                {
                    return;
                }

                var source = payload[0].GetString() ?? string.Empty;
                var trains = payload[1].Deserialize<List<PortalTrainDto>>();
                if (trains is null)
                {
                    return;
                }

                ProcessTrainStatus(source, trains);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Unable to parse TrainStatus payload.");
            }
        });

        connection.Reconnecting += error =>
        {
            logger.LogWarning(error, "Portal SignalR reconnecting...");
            return Task.CompletedTask;
        };

        connection.Reconnected += connectionId =>
        {
            logger.LogInformation("Portal SignalR reconnected. " + "ConnectionId={ConnectionId}", connectionId);
            return Task.CompletedTask;
        };

        connection.Closed += error =>
        {
            logger.LogWarning(error, "Portal SignalR connection closed.");
            return Task.CompletedTask;
        };

        logger.LogInformation("Connecting to Portal SignalR...");
        await connection.StartAsync(cancellationToken);
        const string lang = "PL";
        const double zoom = 7.022935189742923;
        const double minLat = 48.29702653973435;
        const double minLon = 13.44729945766156;
        const double maxLat = 55.52329783923743;
        const double maxLon = 26.55270054233844;
        const int selectedTrainNumber = 0;
        const bool forcedRefresh = true;
        var (tid, pid) = await GetConnectionParameters(cancellationToken);
        await connection.InvokeAsync("RegisterParams", lang, zoom, minLat, minLon, maxLat, maxLon,
            selectedTrainNumber, forcedRefresh, tid, pid, cancellationToken);

        logger.LogInformation("CONNECTED to Portal Pasażera.");

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        finally
        {
            try
            {
                await connection.StopAsync(cancellationToken);
            }
            catch
            {
                // Ignorujemy błąd podczas zamykania.
            }
        }
    }

    private void ProcessTrainStatus(string source, List<PortalTrainDto> trains)
    {
        if (trains.Count == 0)
        {
            return;
        }

        store.Upsert(trains);
        logger.LogDebug("TrainStatus: source={Source}, count={Count}", source, trains.Count);
    }

    private async Task<(string Tid, string Pid)> GetConnectionParameters(
        CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient();
        using var response = await httpClient.GetAsync(options.Value.TokenUrl, cancellationToken);

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var tidMatch = Regex.Match(html, """var\s+TID\s*=\s*['"]([^'"]*)['"]""");
        var pidMatch = Regex.Match(html, """var\s+PID\s*=\s*['"]([^'"]*)['"]""");

        if (!tidMatch.Success)
        {
            throw new Exception("Nie znaleziono TID");
        }

        if (!pidMatch.Success)
        {
            throw new Exception("Nie znaleziono PID");
        }

        return (tidMatch.Groups[1].Value, pidMatch.Groups[1].Value);
    }
}
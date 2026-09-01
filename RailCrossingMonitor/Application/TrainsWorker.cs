using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using RailCrossingMonitor.Models;

namespace RailCrossingMonitor.Application;

public sealed class TrainsWorker : BackgroundService
{
    private const string DefaultNegotiateUrl =
        "https://mapa.portalpasazera.pl/alltrainshub/negotiate?negotiateVersion=1";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TrainStore _store;
    private readonly IHubContext<TrainHub> _hub;
    private readonly ILogger<TrainsWorker> _logger;

    public TrainsWorker(
        IHttpClientFactory httpClientFactory,
        TrainStore store,
        IHubContext<TrainHub> hub,
        ILogger<TrainsWorker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _store = store;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Portal Pasażera worker started.");

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
                _logger.LogError(
                    ex,
                    "Error connecting to Portal Pasażera.");

                _logger.LogInformation(
                    "Retrying in 10 seconds...");

                await Task.Delay(
                    TimeSpan.FromSeconds(10),
                    stoppingToken);
            }
        }

        _logger.LogInformation(
            "Portal Pasażera worker stopped.");
    }

    private async Task ConnectToPortal(
        CancellationToken cancellationToken)
    {
        var negotiateUrl =
            Environment.GetEnvironmentVariable("PP_NEGO_URL")
            ?? DefaultNegotiateUrl;

        _logger.LogInformation(
            "Negotiating Portal Pasażera SignalR connection...");

        var http = _httpClientFactory.CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            negotiateUrl);

        /*
         * Portal oczekuje połączenia pochodzącego
         * z aplikacji mapy.
         */
        request.Headers.Referrer =
            new Uri("https://mapa.portalpasazera.pl/");

        request.Headers.TryAddWithoutValidation(
            "Origin",
            "https://mapa.portalpasazera.pl");

        request.Content =
            JsonContent.Create(new { });

        using var response =
            await http.SendAsync(
                request,
                cancellationToken);

        var responseBody =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Negotiate failed: {(int)response.StatusCode} " +
                $"{response.ReasonPhrase}. " +
                $"Response: {responseBody}");
        }

        var negotiate =
            JsonSerializer.Deserialize<NegotiateResponse>(
                responseBody,
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

        _logger.LogInformation(
            "Negotiate successful.");

        _logger.LogDebug(
            "SignalR URL: {Url}",
            negotiate.url);

        /*
         * Nie logujemy accessToken.
         */

        var connection = new HubConnectionBuilder().WithUrl(
                negotiate.url,
                options =>
                {
                    options.AccessTokenProvider =
                        () => Task.FromResult<string?>(
                            negotiate.accessToken);
                })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddConsole();
            })
            .WithAutomaticReconnect()
            .Build();

        /*
         * Najbardziej interesujący event.
         *
         * Portal:
         *
         * TrainStatus(
         *     "ATM",
         *     [
         *        { t, s, d, o, i, p, n, c, a }
         *     ]
         * )
         */


        
        connection.On<string, List<PortalTrainDto>>("TrainStatus", ProcessTrainStatus);

        /*
         * Fallback.
         *
         * Przy zmianie serializacji możemy dostać
         * cały payload jako JsonElement.
         */

        connection.On<JsonElement>(
            "TrainStatus",
            payload =>
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

                    var source =
                        payload[0].GetString()
                        ?? string.Empty;

                    var trains =
                        payload[1].Deserialize<
                            List<PortalTrainDto>>();

                    if (trains is null)
                    {
                        return;
                    }

                    ProcessTrainStatus(
                        source,
                        trains);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Unable to parse TrainStatus payload.");
                }
            });

        connection.Reconnecting += error =>
        {
            _logger.LogWarning(
                error,
                "Portal SignalR reconnecting...");

            return Task.CompletedTask;
        };

        connection.Reconnected += connectionId =>
        {
            _logger.LogInformation(
                "Portal SignalR reconnected. " +
                "ConnectionId={ConnectionId}",
                connectionId);

            return Task.CompletedTask;
        };

        connection.Closed += error =>
        {
            _logger.LogWarning(
                error,
                "Portal SignalR connection closed.");

            return Task.CompletedTask;
        };

        _logger.LogInformation(
            "Connecting to Portal SignalR...");

        await connection.StartAsync(
            cancellationToken);
        
        const string lang = "PL";
        const double zoom = 7.022935189742923;

        const double minLat = 48.29702653973435;
        const double minLon = 13.44729945766156;
        const double maxLat = 55.52329783923743;
        const double maxLon = 26.55270054233844;

        const int selectedTrainNumber = 0;
        const bool forcedRefresh = true;
        // const string tid = "ATM";
        // const string pid = "R0FQU0tJDUgZafuDscc2B4PeuHscc2ByyI8Ze9Qgt7ClEIwG8ZHaFKXFee2scc2B1BDiDv5SMvcfIAbYHdLO";
        var (tid, pid) = await GetConnectionParameters(cancellationToken);
        await connection.InvokeAsync(
            "RegisterParams",
            lang,
            zoom,
            minLat,
            minLon,
            maxLat,
            maxLon,
            selectedTrainNumber,
            forcedRefresh,
            tid,
            pid,
            cancellationToken);

        _logger.LogInformation(
            "CONNECTED to Portal Pasażera.");

        /*
         * Trzymamy połączenie przy życiu.
         *
         * Jeśli zostanie zamknięte, metoda wróci,
         * a ExecuteAsync wykona ponowną negotiate.
         */

        try
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
        }
        finally
        {
            try
            {
                await connection.StopAsync();
            }
            catch
            {
                // Ignorujemy błąd podczas zamykania.
            }
        }
    }

    private void ProcessTrainStatus(
        string source,
        List<PortalTrainDto> trains)
    {
        if (trains.Count == 0)
        {
            return;
        }

        _store.Upsert(trains);

        _logger.LogDebug(
            "TrainStatus: source={Source}, count={Count}",
            source,
            trains.Count);

        /*
         * Wysyłamy do wszystkich klientów naszego huba
         * aktualny snapshot.
         */

        // _ = _hub.Clients.All.SendAsync("TrainStatus", _store.GetAll());
    }
    
    private async Task<(string Tid, string Pid)> GetConnectionParameters(
        CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient();
        using var response = await httpClient.GetAsync(
            "https://mapa.portalpasazera.pl/pl/Mapa",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(
            cancellationToken);

        var tidMatch = Regex.Match(
            html,
            @"var\s+TID\s*=\s*['""]([^'""]*)['""]");

        var pidMatch = Regex.Match(
            html,
            @"var\s+PID\s*=\s*['""]([^'""]*)['""]");

        if (!tidMatch.Success)
            throw new Exception("Nie znaleziono TID");

        if (!pidMatch.Success)
            throw new Exception("Nie znaleziono PID");

        return (
            tidMatch.Groups[1].Value,
            pidMatch.Groups[1].Value
        );
    }
}
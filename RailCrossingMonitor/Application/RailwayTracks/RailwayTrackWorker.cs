namespace RailCrossingMonitor.Application.RailwayTracks;

public sealed class RailwayTrackWorker(
    IHttpClientFactory httpClientFactory,
    RailwayTrackStore store,
    ILogger<RailwayTrackWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Railway track worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var httpClient = httpClientFactory.CreateClient("RailwayTracks");

                await store.LoadAsync(httpClient, stoppingToken);

                await Task.Delay(RefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load railway tracks. Retrying in {RetryMinutes} minutes.",
                    RetryInterval.TotalMinutes);

                try
                {
                    await Task.Delay(RetryInterval, stoppingToken);
                }
                catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        logger.LogInformation("Railway track worker stopped.");
    }
}
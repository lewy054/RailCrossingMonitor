using Microsoft.Extensions.Hosting;

namespace RailCrossingMonitor.Application.Crossing;

public class CrossingWorker(
    RailwayCrossingService crossingService)
    : IHostedService
{
    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        await crossingService.LoadCrossingsAsync(
            cancellationToken);
    }

    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
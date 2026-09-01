using Microsoft.AspNetCore.SignalR;

namespace RailCrossingMonitor.Application;

public sealed class TrainHub : Hub
{
    private readonly TrainStore _store;

    public TrainHub(TrainStore store)
    {
        _store = store;
    }

    public override async Task OnConnectedAsync()
    {
        // Po podłączeniu frontend dostaje aktualny snapshot.
        await Clients.Caller.SendAsync(
            "InitialTrains",
            _store.GetAll()
        );

        await base.OnConnectedAsync();
    }
}
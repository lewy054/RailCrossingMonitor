using System.Collections.Concurrent;
using RailCrossingMonitor.Models;

namespace RailCrossingMonitor.Application;

public sealed class TrainStore
{
    private readonly ConcurrentDictionary<long, TrainPosition> _trains = new();

    public IReadOnlyCollection<TrainPosition> GetAll()
    {
        return _trains.Values
            .OrderBy(x => x.Carrier)
            .ThenBy(x => x.Number)
            .ToArray();
    }

    public void Upsert(IEnumerable<PortalTrainDto> trains)
    {
        var receivedAt = DateTimeOffset.UtcNow;

        foreach (var train in trains)
        {
            // Podstawowa walidacja GPS.
            if (train.s is < -90 or > 90)
                continue;

            if (train.d is < -180 or > 180)
                continue;

            var position = new TrainPosition(
                Id: train.t,
                Latitude: train.s,
                Longitude: train.d,
                Status: train.o,
                Info: train.i,
                Carrier: train.p ?? string.Empty,
                Number: train.n ?? string.Empty,
                Code: train.c,
                Angle: train.a,
                ReceivedAtUtc: receivedAt
            );

            _trains[train.t] = position;
        }
    }
}
using System.Collections.Concurrent;
using RailCrossingMonitor.Model;
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
        if (train.s is < -90 or > 90)
            continue;

        if (train.d is < -180 or > 180)
            continue;

        _trains.TryGetValue(train.t, out var previous);

        double? speedKmh = null;
        double? headingDegrees = null;

        if (previous is not null)
        {
            var seconds = (receivedAt - previous.ReceivedAtUtc).TotalSeconds;

            if (seconds > 0)
            {
                var distanceKm = CalculateDistanceKm(
                    previous.Latitude,
                    previous.Longitude,
                    train.s,
                    train.d);

                var speed = distanceKm / (seconds / 3600.0);

                if (speed is >= 0 and <= 300)
                    speedKmh = speed;

                if (distanceKm > 0.005)
                {
                    headingDegrees = CalculateBearing(
                        previous.Latitude,
                        previous.Longitude,
                        train.s,
                        train.d);
                }
            }
        }

        _trains[train.t] = new TrainPosition(
            Id: train.t,
            Latitude: train.s,
            Longitude: train.d,
            Status: train.o,
            Info: train.i,
            Carrier: train.p ?? string.Empty,
            Number: train.n ?? string.Empty,
            Code: train.c,
            Angle: train.a,
            ReceivedAtUtc: receivedAt,
            SpeedKmh: speedKmh,
            HeadingDegrees: headingDegrees
        );
    }
}

private static double CalculateDistanceKm(
    double lat1,
    double lon1,
    double lat2,
    double lon2)
{
    const double earthRadiusKm = 6371.0;

    var lat1Rad = lat1 * Math.PI / 180.0;
    var lat2Rad = lat2 * Math.PI / 180.0;
    var deltaLat = (lat2 - lat1) * Math.PI / 180.0;
    var deltaLon = (lon2 - lon1) * Math.PI / 180.0;

    var a =
        Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
        Math.Cos(lat1Rad) *
        Math.Cos(lat2Rad) *
        Math.Sin(deltaLon / 2) *
        Math.Sin(deltaLon / 2);

    var c = 2 * Math.Atan2(
        Math.Sqrt(a),
        Math.Sqrt(1 - a));

    return earthRadiusKm * c;
}

private static double CalculateBearing(
    double lat1,
    double lon1,
    double lat2,
    double lon2)
{
    var lat1Rad = lat1 * Math.PI / 180.0;
    var lat2Rad = lat2 * Math.PI / 180.0;
    var deltaLon = (lon2 - lon1) * Math.PI / 180.0;

    var y = Math.Sin(deltaLon) * Math.Cos(lat2Rad);

    var x =
        Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
        Math.Sin(lat1Rad) *
        Math.Cos(lat2Rad) *
        Math.Cos(deltaLon);

    var bearing = Math.Atan2(y, x) * 180.0 / Math.PI;

    return (bearing + 360.0) % 360.0;
}
}
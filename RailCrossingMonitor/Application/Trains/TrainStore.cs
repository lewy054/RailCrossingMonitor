using System.Collections.Concurrent;
using RailCrossingMonitor.Model;

namespace RailCrossingMonitor.Application.Trains;

public sealed class TrainStore
{
    private readonly ConcurrentDictionary<long, TrainPosition> trains = new();

    public IReadOnlyCollection<TrainPosition> GetAll()
    {
        return trains.Values
            .OrderBy(x => x.Carrier)
            .ThenBy(x => x.Number)
            .ToArray();
    }

    public void Upsert(IEnumerable<PortalTrainDto> trainsDto)
    {
        foreach (var train in trainsDto)
        {
            if (train.Latitude is < -90 or > 90)
            {
                continue;
            }

            if (train.Longitude is < -180 or > 180)
            {
                continue;
            }

            trains.TryGetValue(train.Id, out var previous);

            if (previous is null)
            {
                trains[train.Id] = new TrainPosition()
                {
                    Id = train.Id,
                    Latitude = train.Latitude,
                    Longitude = train.Longitude,
                    Status = train.Status,
                    Info = train.Info,
                    Carrier = train.Carrier ?? string.Empty,
                    Number = train.Number ?? string.Empty,
                    Code = train.Code,
                    Angle = train.Angle,
                };
                continue;
            }

            var distanceKm = CalculateDistanceKm(previous.Latitude, previous.Longitude, train.Latitude, train.Longitude);
            var positionChanged = distanceKm > 0.005;
            var speedKmh = previous.SpeedKmh;
            var headingDegrees = previous.HeadingDegrees;

            if (positionChanged)
            {
                var seconds = (DateTimeOffset.UtcNow - previous.ReceivedAtUtc).TotalSeconds;

                if (seconds > 0)
                {
                    var speed = distanceKm / (seconds / 3600.0);

                    if (speed is >= 0 and <= 300)
                    {
                        speedKmh = speed;
                    }

                    headingDegrees = CalculateBearing(previous.Latitude, previous.Longitude, train.Latitude, train.Longitude);
                }
            }

            trains[train.Id] = new TrainPosition()
            {
                Id = train.Id,
                Latitude = train.Latitude,
                Longitude = train.Longitude,
                Status = train.Status,
                Info = train.Info,
                Carrier = train.Carrier ?? string.Empty,
                Number = train.Number ?? string.Empty,
                Code = train.Code,
                Angle = train.Angle,
                SpeedKmh = speedKmh,
                HeadingDegrees = headingDegrees,
            };
        }
    }

    private static double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371.0;
        var lat1Rad = lat1 * Math.PI / 180.0;
        var lat2Rad = lat2 * Math.PI / 180.0;
        var deltaLat = (lat2 - lat1) * Math.PI / 180.0;
        var deltaLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                Math.Cos(lat1Rad) * Math.Cos(lat2Rad) * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return earthRadiusKm * c;
    }

    private static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        var lat1Rad = lat1 * Math.PI / 180.0;
        var lat2Rad = lat2 * Math.PI / 180.0;
        var deltaLon = (lon2 - lon1) * Math.PI / 180.0;
        var y = Math.Sin(deltaLon) * Math.Cos(lat2Rad);
        var x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) - Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(deltaLon);
        var bearing = Math.Atan2(y, x) * 180.0 / Math.PI;
        return (bearing + 360.0) % 360.0;
    }
}
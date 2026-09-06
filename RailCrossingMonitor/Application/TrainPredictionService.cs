using RailCrossingMonitor.Application.RailwayTracks;
using RailCrossingMonitor.Application.Trains;
using RailCrossingMonitor.Model;

namespace RailCrossingMonitor.Application;

public sealed class TrainPredictionService(TrainStore trainStore, RailwayTrackStore trackStore)
{
    private static readonly TimeSpan MaxGpsAge = TimeSpan.FromSeconds(60);
    private const double MaxPredictionSeconds = 30;
    private const double MinSpeedKmh = 5;
    private const double MaxSpeedKmh = 300;

    public IReadOnlyCollection<PredictedTrainPosition> GetAll()
    {
        var now = DateTimeOffset.UtcNow;

        var result = new List<PredictedTrainPosition>();

        foreach (var train in trainStore.GetAll())
        {
            var predicted = Predict(train, now);

            if (predicted is not null)
            {
                result.Add(predicted);
            }
        }

        return result;
    }

    private PredictedTrainPosition? Predict(TrainPosition train, DateTimeOffset now)
    {
        if (train.SpeedKmh is not >= MinSpeedKmh)
        {
            return null;
        }

        if (train.SpeedKmh > MaxSpeedKmh)
        {
            return null;
        }

        var gpsAge = now - train.ReceivedAtUtc;

        if (gpsAge < TimeSpan.Zero)
        {
            gpsAge = TimeSpan.Zero;
        }

        if (gpsAge > MaxGpsAge)
        {
            return null;
        }

        var match = trackStore.Match(train.Latitude, train.Longitude, train.HeadingDegrees);

        if (match is null)
        {
            return null;
        }

        var predictionSeconds = Math.Clamp(gpsAge.TotalSeconds, 0, MaxPredictionSeconds);
        var predictionDuration = TimeSpan.FromSeconds(predictionSeconds);
        var predictedDistance =
            RailwayTrackStore.PredictDistanceAlongTrack(match, train.SpeedKmh.Value, predictionDuration);

        var predictedPoint = trackStore.GetPointAtDistanceAlongTrack(match.Track, predictedDistance);

        if (predictedPoint is null)
        {
            return null;
        }

        return new PredictedTrainPosition(train.Id, predictedPoint.Value.Latitude, predictedPoint.Value.Longitude,
            predictedDistance, match.Track.Id, train.SpeedKmh.Value, predictionSeconds, train.ReceivedAtUtc);
    }
}
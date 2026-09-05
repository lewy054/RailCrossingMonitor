namespace RailCrossingMonitor.Model;

public sealed record PredictedTrainPosition(
    long Id,
    double Latitude,
    double Longitude,
    double DistanceAlongTrackMeters,
    int TrackId,
    double SpeedKmh,
    double PredictionSeconds,
    DateTimeOffset BasedOnGpsAtUtc);
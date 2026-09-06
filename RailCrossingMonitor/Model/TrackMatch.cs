namespace RailCrossingMonitor.Model;

public sealed record TrackMatch(
    RailwayTrack Track,
    double Longitude,
    double Latitude,
    double DistanceAlongTrackMeters,
    double DistanceToTrackMeters,
    double TrackBearingDegrees,
    bool Forward);
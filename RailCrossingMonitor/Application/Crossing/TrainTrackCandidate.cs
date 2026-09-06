using RailCrossingMonitor.Model;

namespace RailCrossingMonitor.Application.Crossing;

public record TrainTrackCandidate(
    TrainPosition Train,
    TrackMatch TrainMatch,
    TrackMatch CrossingMatch,
    double DistanceMeters,
    double EtaSeconds);
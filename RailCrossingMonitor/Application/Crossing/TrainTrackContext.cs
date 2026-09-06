using RailCrossingMonitor.Model;

namespace RailCrossingMonitor.Application.Crossing;

public record TrainTrackContext(TrainPosition Train, TrackMatch? Match);
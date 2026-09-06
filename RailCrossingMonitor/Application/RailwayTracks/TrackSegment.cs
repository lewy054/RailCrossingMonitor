using RailCrossingMonitor.Model;

namespace RailCrossingMonitor.Application.RailwayTracks;

public record TrackSegment(RailwayTrack Track, TrackPoint Start, TrackPoint End, double StartDistanceMeters);
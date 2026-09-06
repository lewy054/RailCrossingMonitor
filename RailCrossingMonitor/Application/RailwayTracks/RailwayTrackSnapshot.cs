using NetTopologySuite.Index.Strtree;
using RailCrossingMonitor.Model;

namespace RailCrossingMonitor.Application.RailwayTracks;

public class RailwayTrackSnapshot(List<RailwayTrack> track, STRtree<TrackSegment> index)
{
    public List<RailwayTrack> Track { get; set; } = track;
    public STRtree<TrackSegment> Index { get; set; } = index;
}
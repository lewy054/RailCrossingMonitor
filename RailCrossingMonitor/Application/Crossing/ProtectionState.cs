namespace RailCrossingMonitor.Application.Crossing;

public sealed record ProtectionState(
    CrossingState State,
    BarrierState Barriers,
    LightState Lights);
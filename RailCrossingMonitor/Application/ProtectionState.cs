namespace RailCrossingMonitor.Application;

public sealed record ProtectionState(
    CrossingState State,
    BarrierState Barriers,
    LightState Lights);
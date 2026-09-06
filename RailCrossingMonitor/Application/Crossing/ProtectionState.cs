namespace RailCrossingMonitor.Application.Crossing;

public record ProtectionState(CrossingState State, BarrierState Barriers, LightState Lights);
public enum CrossingState
{
    CanGo,
    Caution,
    Stop,
    Unknown
}

public enum BarrierState
{
    None,
    Open,
    Closing,
    Closed,
    Unknown
}

public enum LightState
{
    None,
    Off,
    FlashingRed,
    Unknown
}

public sealed class RailwayCrossingStatus
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";

    public CrossingState State { get; init; }

    public BarrierState Barriers { get; init; }
    public LightState Lights { get; init; }

    public long? TrainId { get; init; }
    public string? TrainNumber { get; init; }
    public string? Carrier { get; init; }
    public double? DistanceMeters { get; init; }
    public double? EtaSeconds { get; init; }

    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
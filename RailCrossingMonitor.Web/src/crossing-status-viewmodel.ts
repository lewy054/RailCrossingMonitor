export interface CrossingStatusViewModel {
    id: string
    name: string
    category: string
    state: CrossingState
    barriers: BarrierState
    lights: LightState
    trainId?: number
    trainNumber?: string
    carrier?: string
    distanceMeters: number
    etaSeconds: number
    latitude: number
    longitude: number
}

export enum CrossingState
{
    CanGo,
    Caution,
    Stop
}

export enum LightState
{
    None,
    Off,
    FlashingRed,
    Unknown
}

export enum BarrierState
{
    None,
    Open,
    Closing,
    Closed,
    Unknown
}


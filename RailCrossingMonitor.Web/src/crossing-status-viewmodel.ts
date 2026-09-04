export interface CrossingStatusViewModel {
    id: string
    name: string
    category: string
    state: CrossingState
    lightsActive: boolean
    barriersClosing: boolean
    barriersDown: boolean
    trainId: number
    trainNumber: string
    carrier: string
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

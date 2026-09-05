export interface PredictedTrainViewModel {
    id: number
    latitude: number
    longitude: number
    distanceAlongTrackMeters: number
    trackId: number
    speedKmh: number
    predictionSeconds: number
    basedOnGpsAtUtc: string
}
import { onBeforeUnmount, onMounted, watch, type Ref } from 'vue'

import OLMap from 'ol/Map'
import View from 'ol/View'
import Feature from 'ol/Feature'
import Point from 'ol/geom/Point'
import TileLayer from 'ol/layer/Tile'
import VectorLayer from 'ol/layer/Vector'
import OSM from 'ol/source/OSM'
import VectorSource from 'ol/source/Vector'
import Overlay from 'ol/Overlay'
import { fromLonLat } from 'ol/proj'
import { Icon, Style } from 'ol/style'

import { useTrainStore } from '@/stores/trains-store'
import { useCrossingStore } from '@/stores/crossing-store'

import type { TrainViewModel } from '@/train-viewmodel'
import type { PredictedTrainViewModel } from '@/predicted-train-viewmodel'

import {
    BarrierState,
    CrossingState,
    LightState,
    type CrossingStatusViewModel
} from '@/crossing-status-viewmodel'

const trainIcon = '/train.svg'

function createSvgImage(src: string): HTMLImageElement {
    const image = new Image()
    image.src = src
    return image
}

const trainImage = createSvgImage(trainIcon)

function getStateColor(state: CrossingState): string {
    switch (state) {
        case CrossingState.CanGo:
            return '#22c55e'
        case CrossingState.Caution:
            return '#f59e0b'
        case CrossingState.Stop:
            return '#ef4444'
        default:
            return '#6b7280'
    }
}

function getStateLabel(state: CrossingState): string {
    switch (state) {
        case CrossingState.CanGo:
            return 'OTWARTY'
        case CrossingState.Caution:
            return 'UWAGA'
        case CrossingState.Stop:
            return 'STOP'
        default:
            return 'NIEZNANY'
    }
}

function getBarrierColor(crossing: CrossingStatusViewModel): string {
    switch (crossing.barriers) {
        case BarrierState.Closed:
            return '#ef4444'
        case BarrierState.Closing:
            return '#f59e0b'
        case BarrierState.Open:
            return '#22c55e'
        case BarrierState.None:
        case BarrierState.Unknown:
        default:
            return '#6b7280'
    }
}

function getBarrierLabel(crossing: CrossingStatusViewModel): string {
    switch (crossing.barriers) {
        case BarrierState.None:
            return 'BRAK'
        case BarrierState.Open:
            return 'PODNIESIONE'
        case BarrierState.Closing:
            return 'ZAMYKAJĄ SIĘ'
        case BarrierState.Closed:
            return 'ZAMKNIĘTE'
        default:
            return 'NIEZNANE'
    }
}

function getLightColor(crossing: CrossingStatusViewModel): string {
    switch (crossing.lights) {
        case LightState.FlashingRed:
            return '#ef4444'
        case LightState.Off:
            return '#9ca3af'
        case LightState.None:
        case LightState.Unknown:
        default:
            return '#6b7280'
    }
}

function getLightLabel(crossing: CrossingStatusViewModel): string {
    switch (crossing.lights) {
        case LightState.None:
            return 'BRAK'
        case LightState.Off:
            return 'WYŁĄCZONE'
        case LightState.FlashingRed:
            return 'AKTYWNE'
        default:
            return 'NIEZNANE'
    }
}

function getBarrierRotation(crossing: CrossingStatusViewModel): number | null {
    switch (crossing.barriers) {
        case BarrierState.Closed:
            return 0
        case BarrierState.Closing:
            return -25
        case BarrierState.Open:
            return -55
        default:
            return null
    }
}

function createCrossingSvg(crossing: CrossingStatusViewModel): string {
    const stateColor = getStateColor(crossing.state)
    const lightColor = getLightColor(crossing)
    const barrierColor = getBarrierColor(crossing)
    const barrierRotation = getBarrierRotation(crossing)

    const barrier = barrierRotation === null
        ? ''
        : `
<rect
x="20"
y="18"
width="7"
height="3"
rx="1.5"
fill="${barrierColor}"
transform="rotate(${barrierRotation} 20 18)"
    />`

    const svg = `
    <svg
xmlns="http://www.w3.org/2000/svg"
width="32"
height="32"
viewBox="0 0 32 32"
>
<circle
    cx="16"
cy="16"
r="14"
fill="white"
stroke="${stateColor}"
stroke-width="3"
/>

<circle
    cx="16"
cy="16"
r="9"
fill="${stateColor}"
/>

<circle
    cx="12"
cy="12"
r="3"
fill="${lightColor}"
    />

    ${barrier}
</svg>`

return `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svg)}`
}

export function useMap(mapElement: Ref<HTMLElement | null>) {
    const trainStore = useTrainStore()
    const crossingStore = useCrossingStore()

    let map: OLMap | null = null
    let trainSource: VectorSource | null = null
    let predictedTrainSource: VectorSource | null = null
    let crossingSource: VectorSource | null = null

    let resizeObserver: ResizeObserver | null = null
    let tooltipElement: HTMLElement | null = null
    let tooltipOverlay: Overlay | null = null

    const trainFeatures = new Map<number, Feature<Point>>()
    const predictedTrainFeatures = new Map<number, Feature<Point>>()
    const crossingFeatures = new Map<string, Feature<Point>>()

    function getCrossingScale(): number {
        if (!map) {
            return 0.5
        }

        const zoom = map.getView().getZoom() ?? 6

        if (zoom <= 6) {
            return 0.375
        }

        if (zoom <= 8) {
            return 0.5
        }

        if (zoom <= 10) {
            return 0.6875
        }

        if (zoom <= 12) {
            return 0.875
        }

        return 1
    }

    function createTrainStyle(train: TrainViewModel): Style {
        return new Style({
            renderer: (pixelCoordinates, state) => {
                const context = state.context as CanvasRenderingContext2D
                const pixel = pixelCoordinates as number[]

                // NIE ZMIENIAMY rozmiaru działającej ikony.
                const size = 32

                const x = pixel[0] - size / 2
                const y = pixel[1] - size / 2

                if (trainImage.complete) {
                    context.drawImage(
                        trainImage,
                        x,
                        y,
                        size,
                        size
                    )
                }

                const text = `${train.carrier} ${train.number}`

                context.font = '600 11px Arial'
                context.textAlign = 'center'
                context.textBaseline = 'top'

                const textWidth = context.measureText(text).width

                context.fillStyle = 'rgba(255, 255, 255, 0.85)'
                context.fillRect(
                    pixel[0] - textWidth / 2 - 5,
                    pixel[1] + 18,
                    textWidth + 10,
                    17
                )

                context.fillStyle = '#222'

                context.fillText(
                    text,
                    pixel[0],
                    pixel[1] + 21
                )
            }
        })
    }

    function createPredictedTrainStyle(): Style {
        return new Style({
            renderer: (pixelCoordinates, state) => {
                const context = state.context as CanvasRenderingContext2D
                const pixel = pixelCoordinates as number[]

                // Taki sam rozmiar jak prawdziwy pociąg.
                const size = 32

                const x = pixel[0] - size / 2
                const y = pixel[1] - size / 2

                context.save()

                // Szary/półprzezroczysty pociąg.
                context.globalAlpha = 0.55

                if (trainImage.complete) {
                    context.filter = 'grayscale(1)'

                    context.drawImage(
                        trainImage,
                        x,
                        y,
                        size,
                        size
                    )
                }

                context.restore()

                // Delikatna przerywana obwódka,
                // żeby predykcja była jednoznacznie widoczna.
                context.save()

                context.beginPath()
                context.arc(
                    pixel[0],
                    pixel[1],
                    18,
                    0,
                    Math.PI * 2
                )

                context.strokeStyle = '#6b7280'
                context.lineWidth = 2
                context.setLineDash([4, 3])
                context.stroke()

                context.restore()
            }
        })
    }

    function createCrossingStyle(
        crossing: CrossingStatusViewModel
    ): Style {
        return new Style({
            image: new Icon({
                src: createCrossingSvg(crossing),
                anchor: [0.5, 0.5],
                anchorXUnits: 'fraction',
                anchorYUnits: 'fraction',
                scale: getCrossingScale()
            })
        })
    }

    function updateTrain(train: TrainViewModel) {
        if (!trainSource) {
            return
        }

        if (
            !Number.isFinite(train.latitude) ||
            !Number.isFinite(train.longitude)
        ) {
            return
        }

        const coordinates = fromLonLat([
            train.longitude,
            train.latitude
        ])

        let feature = trainFeatures.get(train.id)

        if (!feature) {
            feature = new Feature<Point>({
                geometry: new Point(coordinates)
            })

            feature.set('type', 'train')
            feature.set('train', train)
            feature.setStyle(createTrainStyle(train))

            trainFeatures.set(
                train.id,
                feature
            )

            trainSource.addFeature(feature)

            return
        }

        feature.getGeometry()?.setCoordinates(coordinates)
        feature.set('train', train)
        feature.setStyle(createTrainStyle(train))
    }

    function syncTrains() {
        if (!trainSource) {
            return
        }

        const trains = trainStore.filteredTrains

        const currentIds = new Set(
            trains.map(train => train.id)
        )

        for (const [id, feature] of trainFeatures) {
            if (!currentIds.has(id)) {
                trainSource.removeFeature(feature)
                trainFeatures.delete(id)
            }
        }

        for (const train of trains) {
            updateTrain(train)
        }

        trainSource.changed()
    }

    function updatePredictedTrain(
        train: PredictedTrainViewModel
    ) {
        if (!predictedTrainSource) {
            return
        }

        if (
            !Number.isFinite(train.latitude) ||
            !Number.isFinite(train.longitude)
        ) {
            return
        }

        const coordinates = fromLonLat([
            train.longitude,
            train.latitude
        ])

        let feature = predictedTrainFeatures.get(train.id)

        if (!feature) {
            feature = new Feature<Point>({
                geometry: new Point(coordinates)
            })

            feature.set(
                'type',
                'predicted-train'
            )

            feature.set(
                'predictedTrain',
                train
            )

            feature.setStyle(
                createPredictedTrainStyle()
            )

            predictedTrainFeatures.set(
                train.id,
                feature
            )

            predictedTrainSource.addFeature(feature)

            return
        }

        // To jest najważniejsza część:
        // za każdym pobraniem ustawiamy NOWĄ pozycję.
        const geometry = feature.getGeometry()

        if (geometry) {
            geometry.setCoordinates(coordinates)
        }

        feature.set(
            'predictedTrain',
            train
        )

        feature.changed()
    }

    function syncPredictedTrains() {
        if (!predictedTrainSource) {
            return
        }

        const predictedTrains =
            trainStore.predictedTrains

        const currentIds = new Set(
            predictedTrains.map(train => train.id)
        )

        for (const [id, feature] of predictedTrainFeatures) {
            if (!currentIds.has(id)) {
                predictedTrainSource.removeFeature(
                    feature
                )

                predictedTrainFeatures.delete(id)
            }
        }

        for (const train of predictedTrains) {
            updatePredictedTrain(train)
        }

        predictedTrainSource.changed()
    }

    function updateCrossing(
        crossing: CrossingStatusViewModel
    ) {
        if (!crossingSource) {
            return
        }

        if (
            !Number.isFinite(crossing.latitude) ||
            !Number.isFinite(crossing.longitude)
        ) {
            return
        }

        const coordinates = fromLonLat([
            crossing.longitude,
            crossing.latitude
        ])

        let feature = crossingFeatures.get(
            crossing.id
        )

        if (!feature) {
            feature = new Feature<Point>({
                geometry: new Point(coordinates)
            })

            feature.set(
                'type',
                'crossing'
            )

            feature.set(
                'crossing',
                crossing
            )

            feature.set(
                'status',
                crossing
            )

            feature.setStyle(
                createCrossingStyle(crossing)
            )

            crossingFeatures.set(
                crossing.id,
                feature
            )

            crossingSource.addFeature(feature)

            return
        }

        feature.getGeometry()?.setCoordinates(
            coordinates
        )

        feature.set(
            'crossing',
            crossing
        )

        feature.set(
            'status',
            crossing
        )

        feature.setStyle(
            createCrossingStyle(crossing)
        )
    }

    function syncCrossings() {
        if (!crossingSource) {
            return
        }

        const crossings =
            crossingStore.crossingsStatus

        const currentIds = new Set(
            crossings.map(crossing => crossing.id)
        )

        for (const [id, feature] of crossingFeatures) {
            if (!currentIds.has(id)) {
                crossingSource.removeFeature(
                    feature
                )

                crossingFeatures.delete(id)
            }
        }

        for (const crossing of crossings) {
            updateCrossing(crossing)
        }

        crossingSource.changed()
    }

    async function loadCrossings() {
        try {
            await crossingStore.fetchCrossingsStatus()
            syncCrossings()
        } catch (error) {
            console.error(
                'Nie udało się pobrać przejazdów:',
                error
            )
        }
    }

    function updateCrossingIconScales() {
        const scale = getCrossingScale()

        for (const feature of crossingFeatures.values()) {
            const style = feature.getStyle()

            if (!(style instanceof Style)) {
                continue
            }

            const image = style.getImage()

            if (!(image instanceof Icon)) {
                continue
            }

            image.setScale(scale)
        }

        crossingSource?.changed()
    }

    function hideTooltip() {
        if (tooltipElement) {
            tooltipElement.style.display = 'none'
        }

        if (map) {
            map.getTargetElement().style.cursor = ''
        }
    }

    function showCrossingTooltip(
        crossing: CrossingStatusViewModel,
        coordinate: number[]
    ) {
        if (!tooltipElement || !tooltipOverlay) {
            return
        }

        const stateLabel =
            getStateLabel(crossing.state)

        const barrierLabel =
            getBarrierLabel(crossing)

        const lightLabel =
            getLightLabel(crossing)

        const stateColor =
            getStateColor(crossing.state)

        const eta = Number.isFinite(
            crossing.etaSeconds
        )
            ? `${Math.max(
                0,
                Math.round(
                    crossing.etaSeconds!
                )
            )} s`
            : '-'

        const distance = Number.isFinite(
            crossing.distanceMeters
        )
            ? `${Math.round(
                crossing.distanceMeters!
            )} m`
            : '-'

        const train = crossing.trainNumber
            ? `${crossing.carrier ?? ''} ${crossing.trainNumber}`.trim()
            : 'Brak'

        tooltipElement.innerHTML = `
<div class="map-tooltip-title">
    ${crossing.name}
</div>

<div class="map-tooltip-state">
    <span
        class="map-tooltip-state-dot"
        style="
            background: ${stateColor};
            box-shadow: 0 0 0 4px ${stateColor}33;
        "
    ></span>

    <strong style="color: ${stateColor}">
        ${stateLabel}
    </strong>
</div>

<div class="map-tooltip-row">
    <span>🚦 Światła</span>
    <strong>${lightLabel}</strong>
</div>

<div class="map-tooltip-row">
    <span>🚧 Zapory</span>
    <strong>${barrierLabel}</strong>
</div>

<div class="map-tooltip-row">
    <span>🚆 Pociąg</span>
    <strong>${train}</strong>
</div>

<div class="map-tooltip-row">
    <span>📍 Odległość</span>
    <strong>${distance}</strong>
</div>

<div class="map-tooltip-row">
    <span>⏱ ETA</span>
    <strong>${eta}</strong>
</div>
`

        tooltipOverlay.setPosition(coordinate)
        tooltipElement.style.display = 'block'
    }

    function setupMapInteractions() {
        if (!map) {
            return
        }

        map.on('click', event => {
            const feature = map?.forEachFeatureAtPixel(
                event.pixel,
                feature => feature
            )

            if (!feature) {
                trainStore.clearSelection()
                return
            }

            const train = feature.get(
                'train'
            ) as TrainViewModel | undefined

            if (train) {
                trainStore.selectTrain(train)
                return
            }

            const crossing = feature.get(
                'crossing'
            ) as CrossingStatusViewModel | undefined

            if (crossing) {
                showCrossingTooltip(
                    crossing,
                    event.coordinate
                )
            }
        })

        map.on('pointermove', event => {
            if (!map) {
                return
            }

            const target =
                map.getTargetElement()

            if (!target) {
                return
            }

            const feature =
                map.forEachFeatureAtPixel(
                    event.pixel,
                    feature => feature
                )

            if (!feature) {
                hideTooltip()
                return
            }

            const crossing = feature.get(
                'crossing'
            ) as CrossingStatusViewModel | undefined

            if (!crossing) {
                hideTooltip()
                return
            }

            target.style.cursor = 'pointer'

            showCrossingTooltip(
                crossing,
                event.coordinate
            )
        })

        map.getTargetElement().addEventListener(
            'mouseleave',
            hideTooltip
        )
    }

    function createMap() {
        if (!mapElement.value) {
            return
        }

        trainSource = new VectorSource()
        predictedTrainSource = new VectorSource()
        crossingSource = new VectorSource()

        const crossingLayer = new VectorLayer({
            source: crossingSource
        })

        const predictedTrainLayer =
            new VectorLayer({
                source: predictedTrainSource
            })

        const trainLayer = new VectorLayer({
            source: trainSource
        })

        map = new OLMap({
            target: mapElement.value,
            layers: [
                new TileLayer({
                    source: new OSM()
                }),

                // Przejazdy zostają na swoim miejscu.
                crossingLayer,

                // Predykcja pod rzeczywistym pociągiem.
                predictedTrainLayer,

                // Rzeczywisty pociąg jest na wierzchu.
                trainLayer
            ],
            view: new View({
                center: fromLonLat([
                    19.4,
                    52.1
                ]),
                zoom: 6,
                minZoom: 5,
                maxZoom: 18
            })
        })

        tooltipElement =
            document.getElementById('tooltip')

        if (tooltipElement) {
            tooltipElement.style.display = 'none'

            tooltipOverlay = new Overlay({
                element: tooltipElement,
                positioning: 'bottom-center',
                stopEvent: false,
                offset: [0, -15]
            })

            map.addOverlay(tooltipOverlay)
        }

        resizeObserver =
            new ResizeObserver(() => {
                map?.updateSize()
            })

        resizeObserver.observe(
            mapElement.value
        )

        setupMapInteractions()

        syncTrains()
        syncPredictedTrains()

        void loadCrossings()

        map.getView().on(
            'change:resolution',
            updateCrossingIconScales
        )

        requestAnimationFrame(() => {
            map?.updateSize()
        })
    }

    function centerOnSelectedTrain() {
        if (
            !map ||
            trainStore.selectedTrainId === null
        ) {
            return
        }

        const feature =
            trainFeatures.get(
                trainStore.selectedTrainId
            )

        const coordinates =
            feature
                ?.getGeometry()
                ?.getCoordinates()

        if (!coordinates) {
            return
        }

        map.getView().animate({
            center: coordinates,
            duration: 400
        })
    }

    function destroyMap() {
        resizeObserver?.disconnect()
        resizeObserver = null

        hideTooltip()

        if (!map) {
            return
        }

        const target =
            map.getTargetElement()

        if (target) {
            target.style.cursor = ''

            target.removeEventListener(
                'mouseleave',
                hideTooltip
            )
        }

        map.setTarget(undefined)

        trainSource?.clear()
        predictedTrainSource?.clear()
        crossingSource?.clear()

        trainFeatures.clear()
        predictedTrainFeatures.clear()
        crossingFeatures.clear()

        trainSource = null
        predictedTrainSource = null
        crossingSource = null

        tooltipOverlay = null
        tooltipElement = null
        map = null
    }

    watch(
        () => trainStore.filteredTrains,
        syncTrains,
        {
            deep: true
        }
    )

    watch(
        () => trainStore.predictedTrains,
        syncPredictedTrains,
        {
            deep: true
        }
    )

    watch(
        () => trainStore.selectedTrainId,
        centerOnSelectedTrain
    )

    watch(
        () => crossingStore.crossingsStatus,
        syncCrossings,
        {
            deep: true
        }
    )

    onMounted(createMap)
    onBeforeUnmount(destroyMap)
}
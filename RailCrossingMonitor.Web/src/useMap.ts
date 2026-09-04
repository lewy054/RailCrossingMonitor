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
import type { CrossingStatusViewModel } from '@/crossing-status-viewmodel'
import { CrossingState } from '@/crossing-status-viewmodel'

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
    if (crossing.barriersDown) {
        return '#ef4444'
    }

    if (crossing.barriersClosing) {
        return '#f59e0b'
    }

    return '#22c55e'
}

function getBarrierLabel(crossing: CrossingStatusViewModel): string {
    if (crossing.barriersDown) {
        return 'ZAMKNIĘTE'
    }

    if (crossing.barriersClosing) {
        return 'ZAMYKAJĄ SIĘ'
    }

    return 'OTWARTE'
}

function getLightColor(crossing: CrossingStatusViewModel): string {
    return crossing.lightsActive ? '#ef4444' : '#9ca3af'
}

function getLightLabel(crossing: CrossingStatusViewModel): string {
    return crossing.lightsActive ? 'AKTYWNE' : 'WYŁĄCZONE'
}

/**
 * Tworzy małą ikonę przejazdu.
 *
 * Na mapie pokazujemy tylko:
 * - kolor obwódki / środka -> stan przejazdu,
 * - małą kropkę -> światła,
 * - małą belkę -> stan rogatek.
 *
 * Całe informacje są pokazane dopiero w tooltipie.
 */
function createCrossingSvg(crossing: CrossingStatusViewModel): string {
    const stateColor = getStateColor(crossing.state)
    const barrierColor = getBarrierColor(crossing)
    const lightColor = getLightColor(crossing)

    const barrierRotation = crossing.barriersDown
        ? 0
        : crossing.barriersClosing
            ? -25
            : -55

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

<rect
    x="20"
y="18"
width="7"
height="3"
rx="1.5"
fill="${barrierColor}"
transform="rotate(${barrierRotation} 20 18)"
    />
    </svg>
        `

    return `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svg)}`
}

export function useMap(mapElement: Ref<HTMLElement | null>) {
    const trainStore = useTrainStore()
    const crossingStore = useCrossingStore()

    let map: OLMap | null = null
    let trainSource: VectorSource | null = null
    let crossingSource: VectorSource | null = null
    let resizeObserver: ResizeObserver | null = null

    let tooltipElement: HTMLElement | null = null
    let tooltipOverlay: Overlay | null = null

    const trainFeatures = new Map<number, Feature<Point>>()
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

    function createTrainStyle(train: TrainViewModel) {
        return new Style({
            renderer: (pixelCoordinates, state) => {
                const context = state.context as CanvasRenderingContext2D
                const pixel = pixelCoordinates as number[]

                const size = 32
                const x = pixel[0] - size / 2
                const y = pixel[1] - size / 2

                if (trainImage.complete) {
                    context.drawImage(trainImage, x, y, size, size)
                }

                context.font = '600 11px Arial'
                context.textAlign = 'center'
                context.textBaseline = 'top'

                const text = `${train.carrier} ${train.number}`
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

    function createCrossingStyle(crossing: CrossingStatusViewModel) {
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

            trainFeatures.set(train.id, feature)
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
    }

    function updateCrossing(crossing: CrossingStatusViewModel) {
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

        let feature = crossingFeatures.get(crossing.id)

        if (!feature) {
            feature = new Feature<Point>({
                geometry: new Point(coordinates)
            })

            feature.set('type', 'crossing')
            feature.set('crossing', crossing)
            feature.set('status', crossing)
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

        feature.set('crossing', crossing)
        feature.set('status', crossing)
        feature.setStyle(
            createCrossingStyle(crossing)
        )
    }

    function syncCrossings() {
        if (!crossingSource) {
            return
        }

        const crossings = crossingStore.crossingsStatus
        const currentIds = new Set(
            crossings.map(crossing => crossing.id)
        )

        for (const [id, feature] of crossingFeatures) {
            if (!currentIds.has(id)) {
                crossingSource.removeFeature(feature)
                crossingFeatures.delete(id)
            }
        }

        for (const crossing of crossings) {
            updateCrossing(crossing)
        }
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
        if (
            !tooltipElement ||
            !tooltipOverlay
        ) {
            return
        }

        const stateLabel = getStateLabel(
            crossing.state
        )

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
    Math.round(crossing.etaSeconds)
)} s`
            : '-'

        const distance = Number.isFinite(
            crossing.distanceMeters
        )
            ? `${Math.round(
    crossing.distanceMeters
)} m`
            : '-'

        const train = crossing.trainNumber
            ? `${crossing.carrier} ${crossing.trainNumber}`
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
            const feature =
                map?.forEachFeatureAtPixel(
                    event.pixel,
                    feature => feature
                )

            if (!feature) {
                trainStore.clearSelection()
                return
            }

            const train =
                feature.get(
                    'train'
                ) as TrainViewModel | undefined

            if (train) {
                trainStore.selectTrain(train)
                return
            }

            const crossing =
                feature.get(
                    'crossing'
                ) as CrossingStatusViewModel | undefined

            if (crossing) {
                console.log(
                    'Kliknięto przejazd:',
                    crossing
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

            const crossing =
                feature.get(
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

        map.getTargetElement()
            .addEventListener(
                'mouseleave',
                hideTooltip
            )
    }

    function createMap() {
        if (!mapElement.value) {
            return
        }

        trainSource = new VectorSource()
        crossingSource = new VectorSource()

        const crossingLayer =
            new VectorLayer({
                source: crossingSource
            })

        const trainLayer =
            new VectorLayer({
                source: trainSource
            })

        map = new OLMap({
            target: mapElement.value,
            layers: [
                new TileLayer({
                    source: new OSM()
                }),
                crossingLayer,
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
            document.getElementById(
                'tooltip'
            )

        if (tooltipElement) {
            tooltipElement.style.display =
                'none'

            tooltipOverlay =
                new Overlay({
                    element:
                        tooltipElement,
                    positioning:
                        'bottom-center',
                    stopEvent: false,
                    offset: [0, -15]
                })

            map.addOverlay(
                tooltipOverlay
            )
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
        }

        map.setTarget(undefined)

        trainSource?.clear()
        crossingSource?.clear()

        trainFeatures.clear()
        crossingFeatures.clear()

        trainSource = null
        crossingSource = null
        tooltipOverlay = null
        tooltipElement = null
        map = null
    }

    watch(
        () => trainStore.filteredTrains,
        () => {
            syncTrains()
        },
        {
            deep: true
        }
    )

    watch(
        () => trainStore.selectedTrainId,
        () => {
            centerOnSelectedTrain()
        }
    )

    watch(
        () => crossingStore.crossingsStatus,
        () => {
            syncCrossings()
        },
        {
            deep: true
        }
    )

    onMounted(createMap)
    onBeforeUnmount(destroyMap)
}

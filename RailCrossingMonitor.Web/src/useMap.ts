import {
    onBeforeUnmount,
    onMounted,
    watch,
    type Ref
} from 'vue'

import OLMap from 'ol/Map'
import View from 'ol/View'
import Feature from 'ol/Feature'
import Point from 'ol/geom/Point'
import TileLayer from 'ol/layer/Tile'
import VectorLayer from 'ol/layer/Vector'
import OSM from 'ol/source/OSM'
import VectorSource from 'ol/source/Vector'
import { fromLonLat } from 'ol/proj'

import {Circle, Fill, Stroke, Style, Text} from 'ol/style'

import { useTrainStore } from '@/stores/trains-store'
import { useCrossingStore } from '@/stores/crossing-store'

import type { TrainViewModel } from '@/train-viewmodel'
import type { CrossingViewModel} from '@/crossing-viewmodel'
import { Overlay } from "ol"

const trainIcon = '/train.svg'
const crossingIcon = '/crossing.svg'

function createSvgImage(src: string): HTMLImageElement {
    const image = new Image()
    image.src = src
    return image
}

const trainImage = createSvgImage(trainIcon)
const crossingImage = createSvgImage(crossingIcon)
const tooltipElement = document.getElementById('tooltip');
const tooltipOverlay = new Overlay({
    element: tooltipElement,
    positioning: 'bottom-center',
    stopEvent: false,
});

export function useMap(mapElement: Ref<HTMLElement | null>) {
    const trainStore = useTrainStore()
    const crossingStore = useCrossingStore()

    let map: OLMap | null = null
    let trainSource: VectorSource | null = null
    let crossingSource: VectorSource | null = null
    let resizeObserver: ResizeObserver | null = null

    const trainFeatures = new Map<number, Feature<Point>>()
    const crossingFeatures = new Map<string, Feature<Point>>()

    function createTrainStyle(train: TrainViewModel) {
        return new Style({
            renderer: (pixelCoordinates, state) => {
                const context = state.context as CanvasRenderingContext2D
                const pixel = pixelCoordinates as number[]

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

                context.font = '600 11px Arial'
                context.textAlign = 'center'
                context.textBaseline = 'top'

                const text = `${train.carrier} ${train.number}`
                const textWidth = context.measureText(text).width

                context.fillStyle = 'rgba(255, 255, 255, 0.4)'
                context.fillRect(pixel[0] - textWidth / 2 - 5, pixel[1] + 18, textWidth + 10, 17)

                context.fillStyle = '#222'
                context.fillText(text, pixel[0], pixel[1] + 21)
            }
        })
    }

    function createCrossingStyle() {
        return new Style({
            image: new Circle({
                radius: 5,
                fill: new Fill({
                    color: '#ff0000'
                }),
                stroke: new Stroke({
                    color: '#ffffff',
                    width: 2
                })
            })
        })
        // return new Style({
        //     renderer: (pixelCoordinates, state) => {
        //         const context = state.context as CanvasRenderingContext2D
        //         const pixel = pixelCoordinates as number[]
        //
        //         const size = 20
        //         const x = pixel[0] - size / 2
        //         const y = pixel[1] - size / 2
        //
        //         if (crossingImage.complete) {
        //             context.drawImage(crossingImage, x, y, size, size
        //             )
        //         }
        //
        //         context.font = '600 8px Arial'
        //         context.textAlign = 'center'
        //         context.textBaseline = 'top'
        //
        //         const text = `${crossing.category} ${crossing.manager}`
        //         const textWidth = context.measureText(text).width
        //
        //         context.fillStyle = 'rgba(255, 255, 255, 0.3)'
        //         context.fillRect(pixel[0] - textWidth / 2 - 5, pixel[1] + 18, textWidth + 10, 17)
        //
        //         context.fillStyle = '#222'
        //         context.fillText(text, pixel[0], pixel[1] + 21)
        //     }
        // })
    }

    function updateTrain(train: TrainViewModel) {
        if (!trainSource) {
            return
        }

        if (!Number.isFinite(train.latitude) || !Number.isFinite(train.longitude)) {
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
        const currentIds = new Set(trains.map(train => train.id))

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

    function updateCrossing(crossing: CrossingViewModel) {
        if (!crossingSource) {
            return
        }

        if (!Number.isFinite(crossing.latitude) || !Number.isFinite(crossing.longitude)) {
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
            feature.setStyle(createCrossingStyle())

            crossingFeatures.set(crossing.id, feature)
            crossingSource.addFeature(feature)

            return
        }

        feature.getGeometry()?.setCoordinates(coordinates)
        feature.set('crossing', crossing)
    }

    function syncCrossings() {
        if (!crossingSource) {
            return
        }

        const crossings = crossingStore.crossings
        const currentIds = new Set(crossings.map(crossing => crossing.id))

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
            await crossingStore.fetchCrossings()
            syncCrossings()
        } catch (error) {
            console.error('Nie udało się pobrać przejazdów:', error)
        }
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

            const train = feature.get('train') as TrainViewModel | undefined

            if (train) {
                trainStore.selectTrain(train)
                return
            }

            const crossing = feature.get('crossing') as CrossingViewModel | undefined

            if (crossing) {
                console.log('Kliknięto przejazd:', crossing)
            }
        })

        map.on('pointermove', event => {
            if (!map) {
                return
            }

            const target = map.getTargetElement()

            if (!target) {
                return
            }
            
            const feature = map.forEachFeatureAtPixel(event.pixel, function (feature) {
                return feature;
            });
            if (!feature) {
                return
            }

            const train = feature.get('train') as TrainViewModel | undefined

            if (train) {
                console.log('najechano na pociag:', train)
                return
            }

            const crossing = feature.get('crossing') as CrossingViewModel | undefined

            if (crossing) {
                tooltipElement.innerHTML = `${crossing.category} ${crossing.manager}`;
                tooltipOverlay.setPosition(evt.coordinate);
                tooltipElement.style.display = 'block';

                // Zmiana kursora na wskaźnik nad obiektem
                map.getTargetElement().style.cursor = 'pointer';
            }

            tooltipElement.style.display = 'none';
            map.getTargetElement().style.cursor = '';
        })
    }

    function createMap() {
        if (!mapElement.value) {
            return
        }

        trainSource = new VectorSource()
        crossingSource = new VectorSource()

        const crossingLayer = new VectorLayer({
            source: crossingSource
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

        map.addOverlay(tooltipOverlay);

        resizeObserver = new ResizeObserver(() => {
            map?.updateSize()
        })

        resizeObserver.observe(mapElement.value)

        setupMapInteractions()
        syncTrains()
        void loadCrossings()

        requestAnimationFrame(() => {
            map?.updateSize()
        })
    }

    function centerOnSelectedTrain() {
        if (!map || trainStore.selectedTrainId === null) {
            return
        }

        const feature = trainFeatures.get(trainStore.selectedTrainId)
        const coordinates = feature?.getGeometry()?.getCoordinates()

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

        if (!map) {
            return
        }

        const target = map.getTargetElement()

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
        () => crossingStore.crossings,
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

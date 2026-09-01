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

import {
    Style,
    Circle,
    Fill,
    Stroke,
    Text,
    RegularShape
} from 'ol/style'

import { useTrainStore } from '@/stores/trains-store'
import { useCrossingStore } from '@/stores/crossing-store'

import type { TrainViewModel } from '@/train-viewmodel'
import type { Crossing } from '@/crossing-viewmodel'

export function useMap(mapElement: Ref<HTMLElement | null>) {
    const trainStore = useTrainStore()
    const crossingStore = useCrossingStore()

    let map: OLMap | null = null

    let trainSource: VectorSource | null = null
    let crossingSource: VectorSource | null = null

    const trainFeatures = new Map<number, Feature<Point>>()
    const crossingFeatures = new Map<string, Feature<Point>>()

    /*
     * --------------------------------------------------------------------------
     * Styles
     * --------------------------------------------------------------------------
     */

    function createTrainStyle(train: TrainViewModel) {
        return new Style({
            image: new RegularShape({
                points: 3,
                radius: 10,
                rotation: train.heading,
                fill: new Fill({
                    color: '#1976d2'
                }),
                stroke: new Stroke({
                    color: '#ffffff',
                    width: 2
                })
            }),

            text: new Text({
                text: `${train.carrier} ${train.number}`,
                offsetY: 22,
                font: '600 11px Arial',
                fill: new Fill({
                    color: '#222'
                }),
                backgroundFill: new Fill({
                    color: 'rgba(255, 255, 255, 0.9)'
                }),
                padding: [3, 5, 3, 5]
            })
        })
    }

    const crossingStyle = new Style({
        image: new Circle({
            radius: 5,
            fill: new Fill({
                color: '#ff9800'
            }),
            stroke: new Stroke({
                color: '#ffffff',
                width: 2
            })
        })
    })

    /*
     * --------------------------------------------------------------------------
     * Trains
     * --------------------------------------------------------------------------
     */

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

        feature
            .getGeometry()
            ?.setCoordinates(coordinates)

        feature.set('train', train)
        feature.setStyle(createTrainStyle(train))
    }

    function syncTrains() {
        if (!trainSource) {
            return
        }

        const currentIds = new Set(
            trainStore.filteredTrains.map(train => train.id)
        )

        /*
         * Remove trains which are no longer present
         * in filteredTrains.
         */
        for (const [id, feature] of trainFeatures) {
            if (!currentIds.has(id)) {
                trainSource.removeFeature(feature)
                trainFeatures.delete(id)
            }
        }

        /*
         * Add / update current trains.
         */
        for (const train of trainStore.filteredTrains) {
            updateTrain(train)
        }
    }

    /*
     * --------------------------------------------------------------------------
     * Crossings
     * --------------------------------------------------------------------------
     */

    function updateCrossing(crossing: Crossing) {
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
            feature.setStyle(crossingStyle)

            crossingFeatures.set(crossing.id, feature)
            crossingSource.addFeature(feature)

            return
        }

        feature
            .getGeometry()
            ?.setCoordinates(coordinates)

        feature.set('crossing', crossing)
    }

    function syncCrossings() {
        if (!crossingSource) {
            return
        }

        const currentIds = new Set(
            crossingStore.crossings.map(crossing => crossing.id)
        )

        /*
         * Remove crossings which no longer exist
         * in the store.
         */
        for (const [id, feature] of crossingFeatures) {
            if (!currentIds.has(id)) {
                crossingSource.removeFeature(feature)
                crossingFeatures.delete(id)
            }
        }

        /*
         * Add / update crossings.
         */
        for (const crossing of crossingStore.crossings) {
            updateCrossing(crossing)
        }
    }

    async function loadCrossings() {
        try {
            await crossingStore.fetchCrossings()

            syncCrossings()
        } catch (error) {
            console.error(
                'Nie udało się pobrać przejazdów:',
                error
            )
        }
    }

    /*
     * --------------------------------------------------------------------------
     * Map interactions
     * --------------------------------------------------------------------------
     */

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

            /*
             * Train clicked.
             */
            const train = feature.get(
                'train'
            ) as TrainViewModel | undefined

            if (train) {
                trainStore.selectTrain(train)
                return
            }

            /*
             * Crossing clicked.
             *
             * Na razie tylko logujemy dane.
             * Później możemy tutaj podpiąć popup / drawer.
             */
            const crossing = feature.get(
                'crossing'
            ) as Crossing | undefined

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

            const target = map.getTargetElement()

            if (!target) {
                return
            }

            target.style.cursor = map.hasFeatureAtPixel(
                event.pixel
            )
                ? 'pointer'
                : ''
        })
    }

    /*
     * --------------------------------------------------------------------------
     * Map creation
     * --------------------------------------------------------------------------
     */

    function createMap() {
        if (!mapElement.value) {
            return
        }

        trainSource = new VectorSource()
        crossingSource = new VectorSource()

        const trainLayer = new VectorLayer({
            source: trainSource
        })

        const crossingLayer = new VectorLayer({
            source: crossingSource
        })

        map = new OLMap({
            target: mapElement.value,

            layers: [
                new TileLayer({
                    source: new OSM()
                }),

                /*
                 * Crossings are below trains,
                 * so trains remain visually more important.
                 */
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

        setupMapInteractions()

        /*
         * Initial synchronization.
         *
         * Important:
         * the train store may already contain trains
         * before the map is created.
         */
        syncTrains()

        /*
         * Load crossings from backend.
         */
        void loadCrossings()
    }

    /*
     * --------------------------------------------------------------------------
     * Map navigation
     * --------------------------------------------------------------------------
     */

    function centerOnSelectedTrain() {
        if (
            !map ||
            trainStore.selectedTrainId === null
        ) {
            return
        }

        const feature = trainFeatures.get(
            trainStore.selectedTrainId
        )

        const coordinates = feature
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

    /*
     * --------------------------------------------------------------------------
     * Cleanup
     * --------------------------------------------------------------------------
     */

    function destroyMap() {
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

    /*
     * --------------------------------------------------------------------------
     * Watchers
     * --------------------------------------------------------------------------
     */

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

    /*
     * --------------------------------------------------------------------------
     * Lifecycle
     * --------------------------------------------------------------------------
     */

    onMounted(createMap)
    onBeforeUnmount(destroyMap)
}
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

import Style from 'ol/style/Style'
import Text from 'ol/style/Text'
import Fill from 'ol/style/Fill'
import Stroke from 'ol/style/Stroke'
import RegularShape from 'ol/style/RegularShape'

import { useTrainStore } from '@/stores/trains-store'
import type { TrainViewModel } from '@/train-viewmodel'

export function useTrainMap(mapElement: Ref<HTMLElement | null>) {
    const trainStore = useTrainStore()

    let map: OLMap | null = null
    let trainSource: VectorSource | null = null

    const features = new Map<number, Feature<Point>>()

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

        let feature = features.get(train.id)

        if (!feature) {
            feature = new Feature<Point>({
                geometry: new Point(coordinates)
            })

            feature.set('train', train)
            feature.setStyle(createTrainStyle(train))

            features.set(train.id, feature)
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

        const currentIds = new Set(
            trainStore.filteredTrains.map(train => train.id)
        )

        for (const [id, feature] of features) {
            if (!currentIds.has(id)) {
                trainSource.removeFeature(feature)
                features.delete(id)
            }
        }

        for (const train of trainStore.filteredTrains) {
            updateTrain(train)
        }
    }

    function createMap() {
        if (!mapElement.value) {
            return
        }

        trainSource = new VectorSource()

        const trainLayer = new VectorLayer({
            source: trainSource
        })

        map = new OLMap({
            target: mapElement.value,
            layers: [
                new TileLayer({
                    source: new OSM()
                }),
                trainLayer
            ],
            view: new View({
                center: fromLonLat([19.4, 52.1]),
                zoom: 6,
                minZoom: 5,
                maxZoom: 18
            })
        })

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

            target.style.cursor = map.hasFeatureAtPixel(event.pixel)
                ? 'pointer'
                : ''
        })

        syncTrains()
    }

    function centerOnSelectedTrain() {
        if (!map || trainStore.selectedTrainId === null) {
            return
        }

        const feature = features.get(
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
        features.clear()

        trainSource = null
        map = null
    }

    watch(
        () => trainStore.filteredTrains,
        syncTrains
    )

    watch(
        () => trainStore.selectedTrainId,
        centerOnSelectedTrain
    )

    onMounted(createMap)
    onBeforeUnmount(destroyMap)
}
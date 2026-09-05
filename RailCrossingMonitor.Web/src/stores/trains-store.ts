import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

import type { TrainViewModel } from '@/train-viewmodel'
import type { PredictedTrainViewModel } from '@/predicted-train-viewmodel'

export const useTrainStore = defineStore('trains', () => {
    const trains = ref<TrainViewModel[]>([])
    const predictedTrains = ref<PredictedTrainViewModel[]>([])
    const filteredTrains = ref<TrainViewModel[]>([])

    const selectedTrainId = ref<number | null>(null)
    const searchQuery = ref('')

    const isLoading = ref(false)
    const isPredictionLoading = ref(false)

    const error = ref<string | null>(null)
    const predictionError = ref<string | null>(null)

    const trainCount = computed(() => trains.value.length)

    const selectedTrain = computed(() => {
        if (selectedTrainId.value === null) {
            return null
        }

        return trains.value.find(
            train => train.id === selectedTrainId.value
        ) ?? null
    })

    const selectedPredictedTrain = computed(() => {
        if (selectedTrainId.value === null) {
            return null
        }

        return predictedTrains.value.find(
            train => train.id === selectedTrainId.value
        ) ?? null
    })

    const connected = computed(() => error.value === null)

    function filterTrains() {
        const query = searchQuery.value.trim()

        if (!query) {
            filteredTrains.value = trains.value
            return
        }

        filteredTrains.value = trains.value.filter(train =>
            `${train.carrier} ${train.number}`
                .toLowerCase()
                .includes(query.toLowerCase())
        )
    }

    async function fetchTrains() {
        isLoading.value = true

        try {
            const response = await fetch(
                'http://localhost:5100/api/trains'
            )

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`)
            }

            trains.value = await response.json()
            filterTrains()
            error.value = null
        } catch (err) {
            console.error('Failed to fetch trains:', err)

            error.value = err instanceof Error
                ? err.message
                : 'Nie udało się pobrać pociągów'
        } finally {
            isLoading.value = false
        }
    }

    async function fetchPredictedTrains() {
        isPredictionLoading.value = true

        try {
            const response = await fetch(
                'http://localhost:5100/api/trains/predicted'
            )

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`)
            }

            predictedTrains.value = await response.json()
            predictionError.value = null
        } catch (err) {
            console.error('Failed to fetch predicted trains:', err)

            predictionError.value = err instanceof Error
                ? err.message
                : 'Nie udało się pobrać predykcji'

            predictedTrains.value = []
        } finally {
            isPredictionLoading.value = false
        }
    }

    function selectTrain(train: TrainViewModel) {
        selectedTrainId.value = train.id
    }

    function selectTrainById(id: number | null) {
        selectedTrainId.value = id
    }

    function clearSelection() {
        selectedTrainId.value = null
    }

    return {
        trains,
        predictedTrains,
        filteredTrains,
        selectedTrainId,
        selectedTrain,
        selectedPredictedTrain,
        trainCount,
        isLoading,
        isPredictionLoading,
        error,
        predictionError,
        connected,
        searchQuery,
        fetchTrains,
        fetchPredictedTrains,
        filterTrains,
        selectTrain,
        selectTrainById,
        clearSelection
    }
})
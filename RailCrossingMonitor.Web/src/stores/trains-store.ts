import {computed, ref} from 'vue'
import {defineStore} from 'pinia'

import type {TrainViewModel} from '@/train-viewmodel'

export const useTrainStore = defineStore('trains', () => {
    const trains = ref<TrainViewModel[]>([])
    const filteredTrains = ref<TrainViewModel[]>([])
    const selectedTrainId = ref<number | null>(null)
    const searchQuery = ref('')
    const isLoading = ref(false)
    const error = ref<string | null>(null)

    const trainCount = computed(() => trains.value.length)

    const selectedTrain = computed(() => {
        if (selectedTrainId.value === null) {
            return null
        }
        return trains.value.find(train => train.id === selectedTrainId.value) ?? null
    })

    function filterTrains() {
        let query = searchQuery.value.trim().toLowerCase()

        if (!query) {
            filteredTrains.value = trains.value
        }

        filteredTrains.value = trains.value.filter(train =>
            `${train.carrier} ${train.number}`
                .toLowerCase()
                .includes(query)
        )
    }

    const connected = computed(() => error.value === null)

    async function fetchTrains() {
        isLoading.value = true

        try {
            const response = await fetch(
                'http://localhost:5100/api/trains'
            )

            if (!response.ok) {

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
        filteredTrains,
        selectedTrainId,
        selectedTrain,
        trainCount,
        isLoading,
        error,
        connected,
        searchQuery,
        fetchTrains,
        filterTrains,
        selectTrain,
        clearSelection
    }
})
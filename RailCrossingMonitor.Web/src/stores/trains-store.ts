import {computed, ref} from 'vue'
import {defineStore} from 'pinia'

import type {TrainViewModel} from '@/train-viewmodel'

export const useTrainStore = defineStore('trains', () => {
    const trains = ref<TrainViewModel[]>([])
    const filteredTrains = ref<TrainViewModel[]>([])
    const selectedTrainId = ref<number | null>(null)

    const search = ref('')

    const isLoading = ref(false)
    const error = ref<string | null>(null)

    const trainCount = computed(() => trains.value.length)

    const selectedTrain = computed(() => {
        if (selectedTrainId.value === null) {
            return null
        }
        return trains.value.find(train => train.id === selectedTrainId.value) ?? null
    })

    function filterTrains(query: string) {
        query = query.trim().toLowerCase()

        if (!query) {
            return trains.value
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
            filteredTrains.value = trains.value;
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

    function setSearch(value: string) {
        search.value = value
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
        search,
        selectedTrainId,
        selectedTrain,
        trainCount,
        isLoading,
        error,
        connected,
        fetchTrains,
        filterTrains,
        selectTrain,
        clearSelection
    }
})
import { defineStore } from 'pinia'
import { ref } from 'vue'
import type {CrossingViewModel} from "@/crossing-viewmodel.ts";
import type {CrossingStatusViewModel} from "@/crossing-status-viewmodel.ts";

export const useCrossingStore = defineStore('crossing', () => {
    const crossings = ref<CrossingViewModel[]>([])
    const crossingsStatus = ref<CrossingStatusViewModel[]>([])
    const loading = ref(false)
    const error = ref<string | null>(null)

    const fetchCrossings = async () => {
        loading.value = true
        error.value = null

        try {
            const response = await fetch(
                'http://localhost:5100/api/crossings'
            )

            if (!response.ok) {
                
            }

            crossings.value = await response.json()
        } catch (err) {
            error.value = err instanceof Error ? err.message : 'Nie udało się pobrać przejazdów'
        } finally {
            loading.value = false
        }
    }

    const fetchCrossingsStatus = async () => {
        loading.value = true
        error.value = null

        try {
            const response = await fetch(
                'http://localhost:5100/api/crossings/status'
            )

            if (!response.ok) {

            }

            crossingsStatus.value = await response.json()
        } catch (err) {
            error.value = err instanceof Error ? err.message : 'Nie udało się pobrać przejazdów'
        } finally {
            loading.value = false
        }
    }

    return {
        // crossings,
        crossingsStatus,
        loading,
        error,
        // fetchCrossings,
        fetchCrossingsStatus
    }
})
import { defineStore } from 'pinia'
import { ref } from 'vue'
import type {Crossing} from "@/crossing-viewmodel.ts";

export const useCrossingStore = defineStore('crossing', () => {
    const crossings = ref<Crossing[]>([])
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

    return {
        crossings,
        loading,
        error,
        fetchCrossings
    }
})
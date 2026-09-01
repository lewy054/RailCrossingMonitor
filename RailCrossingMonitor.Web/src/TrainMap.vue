<template>
  <div ref="mapElement" class="train-map">
    <div class="map-overlay">
      <v-chip
          size="small"
          prepend-icon="mdi-train"
          color="primary"
          variant="elevated"
      >
        {{ trainStore.trainCount }} pociągów
      </v-chip>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'

import { useTrainStore } from '@/stores/trains-store'
import { useTrainMap } from '@/useTrainMap'

const trainStore = useTrainStore()
const mapElement = ref<HTMLElement | null>(null)

useTrainMap(mapElement)
</script>

<style scoped>
.train-map {
  width: 100%;
  height: 100%;
  position: relative;
}

.map-overlay {
  position: absolute;
  top: 16px;
  right: 16px;
  z-index: 10;
}
</style>
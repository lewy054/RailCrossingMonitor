<template>
  <div id="tooltip"
      class="map-tooltip"></div>
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
import { useMap } from '@/useMap.ts'

const trainStore = useTrainStore()
const mapElement = ref<HTMLElement | null>(null)

useMap(mapElement)
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
#tooltip {
  display: none;
  pointer-events: none;
  min-width: 220px;
  padding: 12px 14px;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.96);
  border: 1px solid rgba(0, 0, 0, 0.08);
  box-shadow: 0 8px 30px rgba(0, 0, 0, 0.18);
  color: #1f2937;
  font-family: Arial, sans-serif;
  font-size: 12px;
  line-height: 1.6;
}

.map-tooltip-title {
  margin-bottom: 6px;
  font-size: 14px;
  font-weight: 700;
}

.map-tooltip-state {
  display: flex;
  align-items: center;
  gap: 7px;
  margin-bottom: 7px;
}

.map-tooltip-state-dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
}

.map-tooltip-row {
  display: flex;
  justify-content: space-between;
  gap: 20px;
  margin-top: 3px;
}
</style>
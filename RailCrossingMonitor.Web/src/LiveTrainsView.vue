<template>
  <div class="live-trains">
    <main class="map-container">
      <Map />
    </main>

    <aside class="train-sidebar">
      <TrainList />
    </aside>
  </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'

import TrainList from '@/TrainList.vue'
import Map from '@/Map.vue'
import { useTrainStore } from '@/stores/trains-store'
import {useCrossingStore} from "@/stores/crossing-store.ts";

const trainStore = useTrainStore()
const crossingStore = useCrossingStore()

let refreshTimer: number | undefined

onMounted(async () => {
  await trainStore.fetchTrains()
  await crossingStore.fetchCrossings()

  refreshTimer = window.setInterval(() => {
    trainStore.fetchTrains()
  }, 5000)
})

onBeforeUnmount(() => {
  if (refreshTimer !== undefined) {
    window.clearInterval(refreshTimer)
  }
})
</script>

<style scoped>
.live-trains {
  display: flex;
  width: 100%;
  height: 100%;
  min-height: 0;
  overflow: hidden;
}

.map-container {
  flex: 1 1 auto;
  width: 0;
  min-width: 0;
  height: 100%;
  min-height: 0;
  position: relative;
  overflow: hidden;
}

.train-sidebar {
  width: 340px;
  height: 100%;
  flex: 0 0 340px;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  border-right: 1px solid rgba(0, 0, 0, 0.12);
}
</style>
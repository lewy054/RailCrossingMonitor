<template>
  <div class="live-trains">
    <main class="train-map">
      <TrainMap />
    </main>
    <aside class="train-sidebar">
      <TrainList />
    </aside>
  </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'

import TrainList from '@/TrainList.vue'
import TrainMap from '@/TrainMap.vue'
import { useTrainStore } from '@/stores/trains-store'

const trainStore = useTrainStore()

let refreshTimer: number | undefined

onMounted(() => {
  trainStore.fetchTrains()

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

.train-sidebar {
  width: 340px;
  height: 100%;
  flex: 0 0 340px;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  border-right: 1px solid rgba(0, 0, 0, 0.12);
}

.train-map {
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  height: 100%;
}
</style>
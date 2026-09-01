<template>
  <v-app>
    <AppNavigation />

    <v-main class="app-main">
      <LiveTrainsView />
    </v-main>
  </v-app>
</template>

<script setup lang="ts">
import AppNavigation from '@/AppNavigation.vue'
import LiveTrainsView from '@/LiveTrainsView.vue'
</script>

<style>
html,
body,
#app {
  width: 100%;
  height: 100%;
  margin: 0;
  overflow: hidden;
}

.app-main {
  height: 100vh;
  overflow: hidden;
}
</style>
<template>
  <div class="train-list">
    <div class="pa-4">
      <v-text-field
          v-model="searchQuery"
          label="Szukaj pociągu"
          prepend-inner-icon="mdi-magnify"
          variant="outlined"
          density="compact"
          clearable
          hide-details
      />
    </div>

    <v-divider />

    <v-list>
      <v-list-subheader>
        Pociągi
      </v-list-subheader>

      <v-list-item
          v-for="train in trainStore.filteredTrains"
          :key="train.id"
          :active="train.id === trainStore.selectedTrainId"
          @click="trainStore.selectTrain(train)"
      >
        <template #prepend>
          <v-avatar
              color="primary"
              size="36"
          >
            <v-icon>mdi-train</v-icon>
          </v-avatar>
        </template>

        <v-list-item-title>
          {{ train.carrier }} {{ train.number }}
        </v-list-item-title>

        <v-list-item-subtitle>
          {{ train.latitude.toFixed(4) }},
          {{ train.longitude.toFixed(4) }}
        </v-list-item-subtitle>
      </v-list-item>

      <v-list-item
          v-if="!trainStore.filteredTrains.length && !trainStore.isLoading"
      >
        <v-list-item-title>
          Nie znaleziono pociągów
        </v-list-item-title>
      </v-list-item>
    </v-list>
  </div>
</template>

<script setup lang="ts">
import { useTrainStore } from '@/stores/trains-store'
import {ref, watch} from "vue";

const trainStore = useTrainStore()
const searchQuery = ref('');

watch(searchQuery, () => {
  trainStore.filterTrains(searchQuery.value)
})
</script>
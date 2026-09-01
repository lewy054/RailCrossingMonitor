/**
 * router/index.ts
 *
 * Manual routes for ./src/pages/*.vue
 */

// Composables
import { createRouter, createWebHistory } from 'vue-router'
import Index from '@/App.vue'
import LiveTrainsView from "@/LiveTrainsView.vue";

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      component: LiveTrainsView,
    },
    {
      path: '/trains',
      component: LiveTrainsView,
    },
  ],
})

export default router

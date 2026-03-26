import { createRouter, createWebHistory } from 'vue-router'

import ModelsPage from './views/ModelsPage.vue'
import AgentsPage from './views/AgentsPage.vue'
import ChatPage from './views/ChatPage.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/models' },
    { path: '/models', name: 'models', component: ModelsPage },
    { path: '/agents', name: 'agents', component: AgentsPage },
    { path: '/chat', name: 'chat', component: ChatPage },
  ],
})

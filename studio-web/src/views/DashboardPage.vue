<template>
  <section class="page">
    <div class="page-header">
      <p class="eyebrow">Studio overview</p>
      <n-button type="primary" @click="$router.push('/agents')">Manage Agents</n-button>
    </div>

    <n-grid cols="1 s:3" responsive="screen" :x-gap="18" :y-gap="18">
      <n-grid-item v-for="stat in stats" :key="stat.label">
        <n-card class="glass-card metric-card" embedded>
          <p class="eyebrow">{{ stat.hint }}</p>
          <n-statistic :label="stat.label" :value="stat.value" />
        </n-card>
      </n-grid-item>
    </n-grid>

    <n-card class="glass-card">
      <template #header>
        <div class="section-heading">
          <div>
            <p class="eyebrow">Recent activity</p>
            <h3>Latest conversations</h3>
          </div>
        </div>
      </template>

      <div v-if="overview?.recentConversations?.length" class="list-stack">
        <article v-for="item in overview.recentConversations" :key="item.id" class="list-row">
          <div>
            <strong>{{ item.title }}</strong>
            <p>{{ item.agentName }} · {{ item.messageCount }} messages</p>
          </div>
          <span>{{ new Date(item.updatedAtUtc).toLocaleString() }}</span>
        </article>
      </div>
      <div v-else class="empty-state">No conversations yet. Create an agent and start chatting.</div>
    </n-card>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NCard, NGrid, NGridItem, NStatistic, NButton } from 'naive-ui'

import { useStudioStore } from '../stores/studio'

const store = useStudioStore()
const overview = computed(() => store.overview)

const stats = computed(() => [
  { label: 'Models', value: overview.value?.modelCount ?? 0, hint: 'Active model catalog' },
  { label: 'Agents', value: overview.value?.agentCount ?? 0, hint: 'Reusable AI personas' },
  { label: 'Chats', value: overview.value?.conversationCount ?? 0, hint: 'Persisted conversations' },
])
</script>

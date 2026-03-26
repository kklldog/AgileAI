<script setup lang="ts">
import { RouterLink, useRoute } from 'vue-router'
import { NButton, NIcon, NLayout, NLayoutSider } from 'naive-ui'
import { MoonOutline, SunnyOutline } from '@vicons/ionicons5'

defineProps<{
  themeMode: 'light' | 'dark'
}>()

defineEmits<{
  toggleTheme: []
}>()

const route = useRoute()

const navItems = [
  { label: 'Models', path: '/models' },
  { label: 'Agents', path: '/agents' },
]
</script>

<template>
  <n-layout has-sider class="shell">
    <n-layout-sider bordered collapse-mode="width" :collapsed-width="0" :width="280" class="shell-sider">
      <div class="brand-panel">
        <div>
          <p class="eyebrow">AgileAI Product Lab</p>
          <h1>AgileAI.Studio</h1>
        </div>
      </div>

      <nav class="nav-list">
        <RouterLink
          v-for="item in navItems"
          :key="item.path"
          :to="item.path"
          class="nav-link"
          :class="{ active: route.path === item.path }"
        >
          <span>{{ item.label }}</span>
        </RouterLink>
      </nav>

      <div class="cta-card glass-card">
        <p class="eyebrow">Studio Flow</p>
        <p class="cta-copy">Add a model, shape an agent, then iterate in chat with a polished control center.</p>
        <RouterLink to="/models">
          <n-button type="primary" block secondary>Add your first model</n-button>
        </RouterLink>
      </div>
    </n-layout-sider>

    <section class="shell-main">
      <div class="ambient ambient-one"></div>
      <div class="ambient ambient-two"></div>
      <div class="shell-main-bar">
        <div></div>
        <n-button circle secondary class="theme-toggle" @click="$emit('toggleTheme')">
          <template #icon>
            <n-icon>
              <component :is="themeMode === 'dark' ? SunnyOutline : MoonOutline" />
            </n-icon>
          </template>
        </n-button>
      </div>
      <div class="shell-content">
        <slot />
      </div>
    </section>
  </n-layout>
</template>

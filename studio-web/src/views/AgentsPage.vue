<template>
  <section class="page">
    <div class="page-header">
      <n-button type="primary" data-testid="create-agent" @click="openModal()">Create agent</n-button>
    </div>

    <div>
      <n-card class="glass-card" embedded>
        <div v-if="store.agents.length === 0" class="empty-state">
          No agents yet. Create your first agent to get started.
        </div>
        <div v-else class="agent-grid">
          <n-card
            v-for="agent in store.agents"
            :key="agent.id"
            hoverable
            class="agent-card"
            @click="navigateToChat(agent)"
          >
            <template #header>
              <n-space align="center" :size="8">
                <n-icon size="20" :depth="2"><boat-outline /></n-icon>
                <n-text strong>{{ agent.name }}</n-text>
                <n-tag v-if="agent.isPinned" type="success" size="tiny" round>Pinned</n-tag>
              </n-space>
            </template>
            <n-ellipsis :line-clamp="3" :tooltip="false">
              {{ agent.systemPrompt || 'No system prompt' }}
            </n-ellipsis>
            <template #footer>
              <n-space justify="space-between" align="center">
                <n-text depth="3" style="font-size: 12px;">{{ agent.modelDisplayName || 'No model' }}</n-text>
                <n-space :size="8" @click.stop>
                  <n-button circle quaternary size="small" @click="openModal(agent)">
                    <template #icon><n-icon><create-outline /></n-icon></template>
                  </n-button>
                  <n-popconfirm @positive-click="removeAgent(agent.id)">
                    <template #trigger>
                      <n-button circle quaternary size="small" type="error">
                        <template #icon><n-icon><trash-outline /></n-icon></template>
                      </n-button>
                    </template>
                    Are you sure you want to delete this agent?
                  </n-popconfirm>
                </n-space>
              </n-space>
            </template>
          </n-card>
        </div>
      </n-card>
    </div>

    <n-modal v-model:show="showModal" preset="card" :title="editing ? 'Edit Agent' : 'Create Agent'" class="modal-shell modal-wide">
      <n-form label-placement="top">
        <n-form-item label="Name"><n-input v-model:value="form.name" data-testid="agent-name-input" /></n-form-item>
        <n-form-item label="Description"><n-input v-model:value="form.description" type="textarea" :autosize="{ minRows: 2, maxRows: 4 }" data-testid="agent-description-input" /></n-form-item>
        <n-form-item label="Model">
          <n-select v-model:value="form.studioModelId" :options="modelOptions" />
        </n-form-item>
        <n-form-item label="System prompt"><n-input v-model:value="form.systemPrompt" type="textarea" :autosize="{ minRows: 6, maxRows: 12 }" data-testid="agent-prompt-input" /></n-form-item>
        <div class="form-grid">
          <n-form-item label="Temperature"><n-input-number v-model:value="form.temperature" :min="0" :max="2" :step="0.1" /></n-form-item>
          <n-form-item label="Max tokens"><n-input-number v-model:value="form.maxTokens" :min="256" :max="8192" :step="128" /></n-form-item>
        </div>
        <div class="form-grid">
          <n-form-item label="Enable skills"><n-switch v-model:value="form.enableSkills" /></n-form-item>
          <n-form-item label="Pin on top"><n-switch v-model:value="form.isPinned" /></n-form-item>
        </div>
        <div class="modal-actions"><n-button @click="showModal = false">Cancel</n-button><n-button type="primary" :disabled="!isFormValid" data-testid="save-agent" @click="submit">Save</n-button></div>
      </n-form>
    </n-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useMessage, NButton, NCard, NForm, NFormItem, NInput, NInputNumber, NModal, NPopconfirm, NSelect, NSwitch, NTag, NIcon, NText, NEllipsis, NSpace } from 'naive-ui'
import { CreateOutline, TrashOutline, BoatOutline } from '@vicons/ionicons5'
import { useStudioStore } from '../stores/studio'
import type { AgentPayload } from '../api/studio'
import type { AgentItem } from '../types'

const store = useStudioStore()
const message = useMessage()
const router = useRouter()

const showModal = ref(false)
const editing = ref<AgentItem | null>(null)

const form = reactive<AgentPayload>({
  studioModelId: '',
  name: '',
  description: '',
  systemPrompt: '',
  temperature: 0.6,
  maxTokens: 2048,
  enableSkills: false,
  isPinned: false,
})

const modelOptions = computed(() =>
  store.models.map((item) => ({ label: `${item.displayName} · ${item.providerConnectionName}`, value: item.id })),
)

const isFormValid = computed(() => {
  return Boolean(
    form.studioModelId &&
      form.name.trim() &&
      form.description.trim() &&
      form.systemPrompt.trim() &&
      form.maxTokens > 0,
  )
})

function resetForm() {
  Object.assign(form, {
    studioModelId: store.models[0]?.id ?? '',
    name: '',
    description: '',
    systemPrompt: '',
    temperature: 0.6,
    maxTokens: 2048,
    enableSkills: false,
    isPinned: false,
  })
}

function openModal(item?: AgentItem) {
  editing.value = item ?? null
  if (item) {
    Object.assign(form, {
      studioModelId: item.studioModelId,
      name: item.name,
      description: item.description,
      systemPrompt: item.systemPrompt,
      temperature: item.temperature,
      maxTokens: item.maxTokens,
      enableSkills: item.enableSkills,
      isPinned: item.isPinned,
    })
  } else {
    resetForm()
  }
  showModal.value = true
}

async function submit() {
  if (!isFormValid.value) {
    message.warning('Complete the agent fields before saving.')
    return
  }

  try {
    if (editing.value) {
      await store.updateAgent(editing.value.id, form)
      message.success('Agent updated')
    } else {
      await store.createAgent(form)
      message.success('Agent created')
    }
    showModal.value = false
  } catch (error) {
    message.error((error as Error).message)
  }
}

async function removeAgent(id: string) {
  await store.deleteAgent(id)
  message.success('Agent deleted')
}

function navigateToChat(agent: AgentItem) {
  router.push({
    name: 'chat',
    query: { agentId: agent.id }
  })
}
</script>

<style scoped>
.empty-state {
  padding: 24px;
  text-align: center;
  color: var(--text-color-3);
}

.agent-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  gap: 16px;
}

.agent-card {
  cursor: pointer;
  transition: all 0.2s ease;
}

.agent-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}
</style>

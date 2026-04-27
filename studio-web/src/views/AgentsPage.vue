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
            v-for="agent in visibleAgents"
            :key="agent.id"
            hoverable
            class="agent-card"
            @click="navigateToChat(agent)"
          >
            <template #header>
              <n-space align="center" :size="8">
                <n-icon size="20" :depth="2"><logo-ionitron /></n-icon>
                <n-text strong>{{ agent.name }}</n-text>
                <n-tag v-if="agent.isPinned" type="success" size="tiny" round>Pinned</n-tag>
              </n-space>
            </template>
            <n-ellipsis :line-clamp="3" :tooltip="false">
              {{ agent.description || 'No description' }}
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
        <div v-if="store.agents.length > 0" class="list-progress">
          <n-text depth="3">Showing {{ visibleAgents.length }} / {{ store.agents.length }} agents</n-text>
        </div>
        <div v-if="hasMoreAgents" ref="agentListSentinel" class="list-sentinel" aria-hidden="true"></div>
        <div v-if="hasMoreAgents" class="list-actions">
          <n-button secondary @click="loadMoreAgents">Load more agents</n-button>
        </div>
      </n-card>
    </div>

    <n-modal v-model:show="showModal" preset="card" :title="editing ? 'Edit Agent' : 'Create Agent'" class="modal-shell agent-modal-shell">
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
        <n-form-item v-if="thinkingIntensityOptions.length" label="Thinking intensity">
          <n-select v-model:value="form.thinkingIntensity" clearable :options="thinkingIntensityOptions" />
        </n-form-item>
        <div class="form-grid">
          <n-form-item label="Enable skills"><n-switch v-model:value="form.enableSkills" /></n-form-item>
          <n-form-item label="Pin on top"><n-switch v-model:value="form.isPinned" /></n-form-item>
        </div>
        <n-collapse v-if="store.skills.length" class="agent-tools-collapse">
          <n-collapse-item :title="`Loaded skills (${store.skills.length})`" name="skills">
            <div class="agent-skill-list">
              <div v-for="skill in store.skills" :key="skill.name" class="agent-skill-item">
                <div class="agent-skill-header">
                  <strong>{{ skill.name }}</strong>
                  <n-tag size="tiny" type="info" bordered>{{ skill.entryMode || 'prompt' }}</n-tag>
                </div>
                <p v-if="skill.description" class="agent-skill-description">{{ skill.description }}</p>
                <div v-if="skill.triggers.length" class="agent-skill-triggers">
                  <n-tag v-for="trigger in skill.triggers" :key="`${skill.name}-${trigger}`" size="small" round>
                    {{ trigger }}
                  </n-tag>
                </div>
                <n-checkbox
                  :checked="form.allowedSkillNames.includes(skill.name)"
                  @update:checked="(checked) => toggleAllowedSkill(skill.name, checked)"
                >
                  Allow this skill for the agent
                </n-checkbox>
              </div>
            </div>
          </n-collapse-item>
        </n-collapse>
        <n-collapse class="agent-tools-collapse">
          <n-collapse-item title="Tools" name="tools">
            <n-checkbox-group v-model:value="form.selectedToolNames" class="agent-tool-group">
              <div class="agent-tool-grid">
                <n-checkbox
                  v-for="tool in toolOptions"
                  :key="tool.value"
                  :value="tool.value"
                  :label="tool.label"
                  class="agent-tool-option"
                />
              </div>
            </n-checkbox-group>
          </n-collapse-item>
        </n-collapse>
        <div class="modal-actions"><n-button @click="showModal = false">Cancel</n-button><n-button type="primary" :disabled="!isFormValid" data-testid="save-agent" @click="submit">Save</n-button></div>
      </n-form>
    </n-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useMessage, NButton, NCard, NCheckbox, NCheckboxGroup, NCollapse, NCollapseItem, NForm, NFormItem, NInput, NInputNumber, NModal, NPopconfirm, NSelect, NSwitch, NTag, NIcon, NText, NEllipsis, NSpace } from 'naive-ui'
import { CreateOutline, TrashOutline, LogoIonitron } from '@vicons/ionicons5'
import { useStudioStore } from '../stores/studio'
import type { AgentPayload } from '../api/studio'
import type { AgentItem } from '../types'

const store = useStudioStore()
const message = useMessage()
const router = useRouter()

const showModal = ref(false)
const editing = ref<AgentItem | null>(null)
const AGENT_BATCH_SIZE = 12
const visibleAgentCount = ref(AGENT_BATCH_SIZE)
const agentListSentinel = ref<HTMLElement | null>(null)
let agentListObserver: IntersectionObserver | null = null
let shouldPopulateDefaultTools = false

const form = reactive<AgentPayload>({
  studioModelId: '',
  name: '',
  description: '',
  systemPrompt: '',
  temperature: 0.6,
  maxTokens: 2048,
  thinkingIntensity: null,
  enableSkills: false,
  isPinned: false,
  selectedToolNames: [],
  allowedSkillNames: [],
})

const modelOptions = computed(() =>
  store.models.map((item) => ({ label: `${item.displayName} · ${item.providerConnectionName}`, value: item.id })),
)

const toolOptions = computed(() =>
  store.agentTools.map((tool) => ({ label: tool.name, value: tool.name })),
)

const selectedModel = computed(() =>
  store.models.find((item) => item.id === form.studioModelId) ?? null,
)

const thinkingIntensityOptions = computed(() =>
  (selectedModel.value?.thinkingIntensities ?? []).map((value) => ({ label: value, value })),
)

const visibleAgents = computed(() => store.agents.slice(0, visibleAgentCount.value))

const hasMoreAgents = computed(() => visibleAgentCount.value < store.agents.length)

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
    thinkingIntensity: null,
    enableSkills: false,
    isPinned: false,
    selectedToolNames: store.agentTools.map((tool) => tool.name),
    allowedSkillNames: store.skills.map((skill) => skill.name),
  })
  shouldPopulateDefaultTools = form.selectedToolNames.length === 0
}

function openModal(item?: AgentItem) {
  editing.value = item ?? null
  if (item) {
    shouldPopulateDefaultTools = false
    Object.assign(form, {
      studioModelId: item.studioModelId,
      name: item.name,
      description: item.description,
      systemPrompt: item.systemPrompt,
      temperature: item.temperature,
      maxTokens: item.maxTokens,
      thinkingIntensity: item.thinkingIntensity ?? null,
      enableSkills: item.enableSkills,
      isPinned: item.isPinned,
      selectedToolNames: [...item.selectedToolNames],
      allowedSkillNames: item.allowedSkillNames.length ? [...item.allowedSkillNames] : store.skills.map((skill) => skill.name),
    })
  } else {
    resetForm()
  }
  showModal.value = true
}

watch(
  () => store.agentTools,
  (tools) => {
    if (!editing.value && showModal.value && shouldPopulateDefaultTools && form.selectedToolNames.length === 0 && tools.length > 0) {
      form.selectedToolNames = tools.map((tool) => tool.name)
      shouldPopulateDefaultTools = false
    }
  },
  { deep: true },
)

watch(
  () => form.studioModelId,
  () => {
    if (!form.thinkingIntensity) {
      return
    }

    const allowed = selectedModel.value?.thinkingIntensities ?? []
    if (!allowed.some((value) => value.toLowerCase() === form.thinkingIntensity?.toLowerCase())) {
      form.thinkingIntensity = null
    }
  },
)

function toggleAllowedSkill(skillName: string, checked: boolean) {
  if (checked) {
    form.allowedSkillNames = [...new Set([...form.allowedSkillNames, skillName])]
    return
  }

  form.allowedSkillNames = form.allowedSkillNames.filter((item) => item !== skillName)
}

function loadMoreAgents() {
  visibleAgentCount.value = Math.min(visibleAgentCount.value + AGENT_BATCH_SIZE, store.agents.length)
}

function syncVisibleAgentCount(nextLength: number, previousLength?: number) {
  if (nextLength === 0) {
    visibleAgentCount.value = AGENT_BATCH_SIZE
    return
  }

  if ((previousLength ?? 0) <= visibleAgentCount.value || nextLength <= visibleAgentCount.value) {
    visibleAgentCount.value = Math.min(Math.max(visibleAgentCount.value, AGENT_BATCH_SIZE), nextLength)
    return
  }

  visibleAgentCount.value = Math.min(visibleAgentCount.value, nextLength)
}

function setupAgentObserver() {
  if (typeof window === 'undefined' || typeof IntersectionObserver === 'undefined') {
    return
  }

  agentListObserver?.disconnect()
  agentListObserver = new IntersectionObserver((entries) => {
    if (entries.some((entry) => entry.isIntersecting)) {
      loadMoreAgents()
    }
  }, {
    rootMargin: '0px 0px 240px 0px',
  })

  if (agentListSentinel.value) {
    agentListObserver.observe(agentListSentinel.value)
  }
}

watch(agentListSentinel, () => {
  setupAgentObserver()
})

watch(
  () => store.agents.length,
  (nextLength, previousLength) => {
    syncVisibleAgentCount(nextLength, previousLength)
  },
  { immediate: true },
)

onMounted(() => {
  setupAgentObserver()
})

onBeforeUnmount(() => {
  agentListObserver?.disconnect()
})

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

.agent-tool-group {
  width: 100%;
}

.list-progress {
  display: flex;
  justify-content: center;
  margin-top: 18px;
}

.list-sentinel {
  width: 100%;
  height: 1px;
}

.list-actions {
  display: flex;
  justify-content: center;
  margin-top: 14px;
}

.agent-tools-collapse {
  margin-top: 8px;
}

.agent-tool-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px 12px;
}

.agent-tool-option {
  width: 100%;
  margin: 0;
  padding: 10px 12px;
  border: 1px solid rgba(148, 163, 184, 0.24);
  border-radius: 12px;
}

.agent-skill-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.agent-skill-item {
  padding: 12px;
  border: 1px solid rgba(148, 163, 184, 0.2);
  border-radius: 12px;
}

.agent-skill-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.agent-skill-description {
  margin: 8px 0;
  color: var(--text-color-2);
}

.agent-skill-triggers {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

@media (max-width: 900px) {
  .agent-tool-grid {
    grid-template-columns: 1fr;
  }
}
</style>

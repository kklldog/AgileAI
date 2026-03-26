<template>
  <section class="page">
    <div class="page-header">
      <n-space>
        <n-button secondary type="primary" @click="openProviderModal()">Add Provider</n-button>
      </n-space>
    </div>

    <div class="grid-two">
      <n-card class="glass-card providers-panel" embedded>
        <div v-if="store.providerConnections.length === 0" class="empty-state">
          No providers configured yet.
        </div>
        <div v-else class="provider-grid provider-grid-single">
          <n-card
            v-for="item in store.providerConnections"
            :key="item.id"
            class="provider-card"
            :class="{ selected: item.id === selectedProviderId }"
            embedded
            @click="selectProvider(item.id)"
          >
            <template #header>
              <n-space align="center" justify="space-between">
                <n-space align="center" :size="8">
                  <n-icon size="24" :depth="2"><server-outline /></n-icon>
                  <n-text strong>{{ item.name }}</n-text>
                  <n-tag :type="getProviderTypeColor(item.providerType)" size="small" round>{{ item.providerType }}</n-tag>
                </n-space>
                <n-space :size="4">
                  <n-button circle quaternary size="small" @click="openProviderModal(item)">
                    <template #icon><n-icon><create-outline /></n-icon></template>
                  </n-button>
                  <n-popconfirm @positive-click="handleDeleteProvider(item.id)">
                    <template #trigger>
                      <n-button circle quaternary size="small" type="error">
                        <template #icon><n-icon><trash-outline /></n-icon></template>
                      </n-button>
                    </template>
                    Delete this provider?
                  </n-popconfirm>
                </n-space>
              </n-space>
            </template>
            <n-descriptions :column="1" size="small" label-placement="left">
              <n-descriptions-item label="Base URL">
                <n-ellipsis style="max-width: 200px;">{{ item.baseUrl || 'N/A' }}</n-ellipsis>
              </n-descriptions-item>
            </n-descriptions>
            <template #footer>
              <n-divider style="margin: 8px 0;" />
              <n-space vertical :size="8">
                <n-text strong style="font-size: 13px;">Models ({{ getModelsForProvider(item.id).length }})</n-text>
                <n-space :size="4" wrap>
                  <n-tag
                    v-for="model in getModelsForProvider(item.id)"
                    :key="model.id"
                    size="small"
                    :bordered="false"
                    :type="model.isEnabled ? 'success' : 'default'"
                  >
                    {{ model.displayName }}
                  </n-tag>
                </n-space>
              </n-space>
            </template>
          </n-card>
        </div>
      </n-card>

      <n-card class="glass-card models-panel" embedded>
        <template #header>
          <div class="models-panel-header">
            <div>
              <p class="eyebrow">Models</p>
              <h3 class="models-panel-title">{{ selectedProvider?.name ?? 'Select a provider' }}</h3>
            </div>
            <n-button type="primary" :disabled="!selectedProviderId" @click="openModelModal()">New model</n-button>
          </div>
        </template>

        <div v-if="!selectedProviderId" class="empty-state">
          Select a provider on the left to manage its models.
        </div>
        <div v-else-if="selectedProviderModels.length === 0" class="empty-state">
          No models configured for this provider yet.
        </div>
        <div v-else class="model-grid">
          <n-card
            v-for="item in selectedProviderModels"
            :key="item.id"
            class="model-card"
            embedded
          >
            <template #header>
              <n-space align="center" justify="space-between">
                <n-space align="center" :size="8">
                  <n-icon size="20" :depth="2"><cube-outline /></n-icon>
                  <n-text strong>{{ item.displayName }}</n-text>
                  <n-tag :type="item.isEnabled ? 'success' : 'default'" size="tiny" round>
                    {{ item.isEnabled ? 'Enabled' : 'Disabled' }}
                  </n-tag>
                </n-space>
                <n-space :size="4">
                  <n-button circle quaternary size="small" :loading="store.validatingModelIds.includes(item.id)" @click="handleTestModel(item.id)">
                    <template #icon><n-icon><flash-outline /></n-icon></template>
                  </n-button>
                  <n-button circle quaternary size="small" @click="openModelModal(item)">
                    <template #icon><n-icon><create-outline /></n-icon></template>
                  </n-button>
                  <n-popconfirm @positive-click="handleDeleteModel(item.id)">
                    <template #trigger>
                      <n-button circle quaternary size="small" type="error">
                        <template #icon><n-icon><trash-outline /></n-icon></template>
                      </n-button>
                    </template>
                    Delete this model?
                  </n-popconfirm>
                </n-space>
              </n-space>
            </template>
            <n-descriptions :column="1" size="small" label-placement="left">
              <n-descriptions-item label="Provider">{{ item.providerConnectionName }}</n-descriptions-item>
              <n-descriptions-item label="Model Key">{{ item.modelKey }}</n-descriptions-item>
              <n-descriptions-item label="Capabilities">
                <n-space :size="4">
                  <n-tag v-if="item.supportsStreaming" size="tiny" type="info">Streaming</n-tag>
                  <n-tag v-if="item.supportsTools" size="tiny" type="info">Tools</n-tag>
                  <n-tag v-if="item.supportsVision" size="tiny" type="info">Vision</n-tag>
                </n-space>
              </n-descriptions-item>
            </n-descriptions>
          </n-card>
        </div>
      </n-card>
    </div>

    <!-- Provider Modal -->
    <n-modal v-model:show="showProviderModal" preset="card" :title="editingProvider ? 'Edit Provider' : 'Add Provider'" class="modal-shell">
      <n-form label-placement="top">
        <n-form-item label="Name"><n-input v-model:value="providerForm.name" data-testid="provider-name-input" /></n-form-item>
        <n-form-item label="Provider type"><n-select v-model:value="providerForm.providerType" :options="providerOptions" @update:value="applyProviderPreset" /></n-form-item>
        <n-form-item label="API key"><n-input v-model:value="providerForm.apiKey" type="password" show-password-on="click" data-testid="provider-key-input" /></n-form-item>
        <n-form-item v-if="providerForm.providerType !== 'AzureOpenAI'" label="Base URL"><n-input v-model:value="providerForm.baseUrl" /></n-form-item>
        <n-form-item v-if="providerForm.providerType === 'AzureOpenAI'" label="Endpoint"><n-input v-model:value="providerForm.endpoint" /></n-form-item>
        <n-form-item v-if="providerForm.providerType === 'OpenAICompatible'" label="Runtime provider name"><n-input v-model:value="providerForm.providerName" placeholder="openapi" /></n-form-item>
        <n-form-item v-if="providerForm.providerType === 'OpenAICompatible'" label="Relative path"><n-input v-model:value="providerForm.relativePath" /></n-form-item>
        <n-form-item label="Enabled"><n-switch v-model:value="providerForm.isEnabled" /></n-form-item>
        <n-space justify="end"><n-button @click="showProviderModal = false">Cancel</n-button><n-button type="primary" :disabled="!isProviderValid" data-testid="save-provider" @click="submitProvider">Save</n-button></n-space>
      </n-form>
    </n-modal>

    <!-- Model Modal -->
    <n-modal v-model:show="showModelModal" preset="card" :title="editingModel ? 'Edit Model' : 'Create Model'" class="modal-shell">
      <n-form label-placement="top">
        <n-form-item label="Provider"><n-select v-model:value="modelForm.providerConnectionId" :options="providerConnectionOptions" /></n-form-item>
        <n-form-item label="Display name"><n-input v-model:value="modelForm.displayName" data-testid="model-display-name-input" /></n-form-item>
        <n-form-item label="Model key / deployment"><n-input v-model:value="modelForm.modelKey" placeholder="gpt-4o-mini" data-testid="model-key-input" /></n-form-item>
        <n-form-item label="Streaming"><n-switch v-model:value="modelForm.supportsStreaming" /></n-form-item>
        <n-form-item label="Tools"><n-switch v-model:value="modelForm.supportsTools" /></n-form-item>
        <n-form-item label="Vision"><n-switch v-model:value="modelForm.supportsVision" /></n-form-item>
        <n-form-item label="Enabled"><n-switch v-model:value="modelForm.isEnabled" /></n-form-item>
        <n-space justify="end"><n-button @click="showModelModal = false">Cancel</n-button><n-button type="primary" :disabled="!isModelValid" data-testid="save-model" @click="submitModel">Save</n-button></n-space>
      </n-form>
    </n-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useMessage, NButton, NCard, NForm, NFormItem, NInput, NModal, NPopconfirm, NSelect, NSpace, NSwitch, NTag, NIcon, NText, NEllipsis, NDescriptions, NDescriptionsItem, NDivider } from 'naive-ui'
import { CreateOutline, TrashOutline, ServerOutline, CubeOutline, FlashOutline } from '@vicons/ionicons5'
import { useStudioStore } from '../stores/studio'
import type { ProviderConnectionPayload, ModelPayload } from '../api/studio'
import type { ModelItem, ProviderConnection } from '../types'

const store = useStudioStore()
const message = useMessage()

const showProviderModal = ref(false)
const showModelModal = ref(false)
const editingProvider = ref<ProviderConnection | null>(null)
const editingModel = ref<ModelItem | null>(null)
const selectedProviderId = ref('')

const providerForm = reactive<ProviderConnectionPayload>({
  name: '',
  providerType: 'OpenAI',
  apiKey: '',
  baseUrl: 'https://api.openai.com/v1/',
  endpoint: '',
  providerName: '',
  relativePath: 'chat/completions',
  apiKeyHeaderName: '',
  authMode: 'Bearer',
  apiVersion: '2024-02-01',
  isEnabled: true,
})

const modelForm = reactive<ModelPayload>({
  providerConnectionId: '',
  displayName: '',
  modelKey: '',
  supportsStreaming: true,
  supportsTools: true,
  supportsVision: false,
  isEnabled: true,
})

const providerOptions = [
  { label: 'OpenAI', value: 'OpenAI' },
  { label: 'OpenAI Compatible', value: 'OpenAICompatible' },
  { label: 'Azure OpenAI', value: 'AzureOpenAI' },
]

function applyProviderPreset(type: 'OpenAI' | 'OpenAICompatible' | 'AzureOpenAI') {
  if (type === 'OpenAI') {
    providerForm.baseUrl = 'https://api.openai.com/v1/'
    providerForm.providerName = ''
    providerForm.relativePath = 'chat/completions'
    providerForm.authMode = 'Bearer'
    providerForm.apiKeyHeaderName = ''
    providerForm.endpoint = ''
    return
  }

  if (type === 'OpenAICompatible') {
    providerForm.baseUrl = 'https://api.openai.com/v1/'
    providerForm.providerName = 'openapi'
    providerForm.relativePath = 'chat/completions'
    providerForm.authMode = 'Bearer'
    providerForm.apiKeyHeaderName = ''
    providerForm.endpoint = ''
    return
  }

  providerForm.endpoint = 'https://your-resource.openai.azure.com/'
  providerForm.baseUrl = ''
  providerForm.providerName = ''
  providerForm.relativePath = 'chat/completions'
  providerForm.authMode = 'Bearer'
}

const providerConnectionOptions = computed(() =>
  store.providerConnections.map((item) => ({ label: `${item.name} · ${item.providerType}`, value: item.id })),
)

const selectedProvider = computed(() =>
  store.providerConnections.find((item) => item.id === selectedProviderId.value) ?? null,
)

const selectedProviderModels = computed(() =>
  store.models.filter((item) => item.providerConnectionId === selectedProviderId.value),
)

const isProviderValid = computed(() => {
  if (!providerForm.name.trim() || !providerForm.apiKey.trim()) {
    return false
  }

  if (providerForm.providerType === 'AzureOpenAI') {
    return Boolean(providerForm.endpoint?.trim())
  }

  if (!providerForm.baseUrl?.trim()) {
    return false
  }

  if (providerForm.providerType === 'OpenAICompatible') {
    return Boolean(providerForm.providerName?.trim() && providerForm.relativePath?.trim())
  }

  return true
})

const isModelValid = computed(() =>
  Boolean(modelForm.providerConnectionId && modelForm.displayName.trim() && modelForm.modelKey.trim()),
)

function getProviderTypeColor(type: string): 'default' | 'success' | 'warning' | 'info' {
  switch (type) {
    case 'OpenAI': return 'success'
    case 'AzureOpenAI': return 'info'
    case 'OpenAICompatible': return 'warning'
    default: return 'default'
  }
}

function getModelsForProvider(providerId: string): ModelItem[] {
  return store.models.filter(m => m.providerConnectionId === providerId)
}

function selectProvider(providerId: string) {
  selectedProviderId.value = providerId
}

watch(
  () => store.providerConnections,
  (providers) => {
    if (providers.length === 0) {
      selectedProviderId.value = ''
      return
    }

    if (!providers.some((item) => item.id === selectedProviderId.value)) {
      selectedProviderId.value = providers[0].id
    }
  },
  { immediate: true, deep: true },
)

function resetProviderForm() {
  Object.assign(providerForm, {
    name: '',
    providerType: 'OpenAI',
    apiKey: '',
    baseUrl: 'https://api.openai.com/v1/',
    endpoint: '',
    providerName: '',
    relativePath: 'chat/completions',
    apiKeyHeaderName: '',
    authMode: 'Bearer',
    apiVersion: '2024-02-01',
    isEnabled: true,
  })
  applyProviderPreset('OpenAI')
}

function resetModelForm() {
  Object.assign(modelForm, {
    providerConnectionId: selectedProviderId.value || (store.providerConnections[0]?.id ?? ''),
    displayName: '',
    modelKey: '',
    supportsStreaming: true,
    supportsTools: true,
    supportsVision: false,
    isEnabled: true,
  })
}

function openProviderModal(item?: ProviderConnection) {
  editingProvider.value = item ?? null
  if (item) {
    Object.assign(providerForm, {
      name: item.name,
      providerType: item.providerType,
      apiKey: '',
      baseUrl: item.baseUrl ?? '',
      endpoint: item.endpoint ?? '',
      providerName: item.providerName ?? '',
      relativePath: item.relativePath ?? 'chat/completions',
      apiKeyHeaderName: item.apiKeyHeaderName ?? '',
      authMode: item.authMode ?? 'Bearer',
      apiVersion: item.apiVersion ?? '2024-02-01',
      isEnabled: item.isEnabled,
    })
  } else {
    resetProviderForm()
  }
  showProviderModal.value = true
}

function openModelModal(item?: ModelItem) {
  editingModel.value = item ?? null
  if (item) {
    Object.assign(modelForm, {
      providerConnectionId: item.providerConnectionId,
      displayName: item.displayName,
      modelKey: item.modelKey,
      supportsStreaming: item.supportsStreaming,
      supportsTools: item.supportsTools,
      supportsVision: item.supportsVision,
      isEnabled: item.isEnabled,
    })
  } else {
    resetModelForm()
  }
  showModelModal.value = true
}

async function submitProvider() {
  if (!isProviderValid.value) {
    message.warning('Complete the provider fields before saving.')
    return
  }

  try {
    if (editingProvider.value) {
      await store.updateProviderConnection(editingProvider.value.id, providerForm)
      message.success('Provider updated')
    } else {
      await store.createProviderConnection(providerForm)
      message.success('Provider created')
    }
    showProviderModal.value = false
    resetProviderForm()
  } catch (error) {
    message.error((error as Error).message)
  }
}

async function submitModel() {
  if (!isModelValid.value) {
    message.warning('Choose a provider and complete the model fields.')
    return
  }

  try {
    if (editingModel.value) {
      await store.updateModel(editingModel.value.id, modelForm)
      message.success('Model updated')
    } else {
      await store.createModel(modelForm)
      message.success('Model created')
    }
    showModelModal.value = false
    resetModelForm()
  } catch (error) {
    message.error((error as Error).message)
  }
}

async function handleDeleteModel(id: string) {
  await store.deleteModel(id)
  message.success('Model deleted')
}

async function handleDeleteProvider(id: string) {
  await store.deleteProviderConnection(id)
  message.success('Provider deleted')
}

async function handleTestModel(id: string) {
  const result = await store.testModel(id)
  message.info(result.message)
}
</script>

<style scoped>
.empty-state {
  padding: 24px;
  text-align: center;
  color: var(--text-color-3);
}

.provider-grid,
.model-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
  gap: 16px;
}

.provider-grid-single {
  grid-template-columns: 1fr;
}

.provider-card,
.model-card {
  transition: all 0.2s ease;
}

.provider-card:hover,
.model-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.provider-card {
  cursor: pointer;
}

.provider-card.selected {
  border-color: var(--primary-color);
  box-shadow: 0 0 0 1px var(--primary-color), 0 4px 12px rgba(0, 0, 0, 0.15);
}

.providers-panel,
.models-panel {
  min-height: 640px;
}

.models-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.models-panel-title {
  margin: 0;
}
</style>

<template>
  <section class="page page-chat">
    <div class="chat-layout">
      <!-- Main Chat Area -->
      <n-card class="glass-card chat-stage" embedded>
        <template #header>
          <div class="chat-heading-meta">
            <n-tag v-if="activeConversation" type="success">{{ activeConversation.agentName }}</n-tag>
            <n-tag v-if="activeAgent" type="info" bordered>{{ activeAgent.name }} · {{ activeAgent.modelDisplayName }}</n-tag>
          </div>
        </template>

        <div v-if="activeMessages.length" ref="transcriptRef" class="message-stack" data-testid="chat-transcript">
          <article v-for="item in activeMessages" :key="item.id" class="message-bubble" :class="item.role.toLowerCase()" :data-testid="`message-${item.role.toLowerCase()}`">
            <span class="message-role">{{ item.role }}</span>
            <p>{{ item.content }}</p>
            <div v-if="item.role === 'Assistant'" class="message-meta">
              <span v-if="item.inputTokens || item.outputTokens">{{ item.inputTokens ?? 0 }} in · {{ item.outputTokens ?? 0 }} out</span>
              <n-spin v-if="item.isStreaming" size="small" />
            </div>
          </article>
        </div>
        <n-empty v-else description="No messages yet. Start a conversation to talk with your agent." class="chat-empty" />

        <div class="composer glass-card">
          <n-input
            v-model:value="prompt"
            type="textarea"
            placeholder="Ask your agent to plan, write, summarize, or analyze..."
            :autosize="{ minRows: 3, maxRows: 6 }"
            data-testid="chat-input"
          />
          <div class="composer-actions">
            <p v-if="store.isStreaming || store.streamError">
              <span v-if="store.isStreaming">Streaming response in progress...</span>
              <span v-if="store.streamError">{{ store.streamError }}</span>
            </p>
            <n-button type="primary" :loading="isSending" data-testid="send-message" @click="submitPrompt">Send</n-button>
          </div>
        </div>
      </n-card>

      <!-- Right Sidebar: Conversation History -->
      <n-card class="glass-card chat-side" embedded>
        <div class="conversation-list">
          <n-empty v-if="sortedConversations.length === 0" description="No conversations" size="small" />
          <div v-else class="conversation-card-list" data-testid="conversation-list">
            <n-card
              v-for="conv in sortedConversations"
              :key="conv.id"
              embedded
              class="conversation-entry"
              :class="{ active: conv.id === store.activeConversationId }"
              :data-testid="`conversation-${conv.id}`"
              @click="selectConversation(conv.id)"
            >
              <strong>{{ conv.title }}</strong>
              <span>{{ conv.agentName }} · {{ conv.messageCount }} messages</span>
            </n-card>
          </div>
        </div>
      </n-card>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useMessage, NButton, NCard, NEmpty, NInput, NSpin, NTag } from 'naive-ui'
import { useStudioStore } from '../stores/studio'

const route = useRoute()
const store = useStudioStore()
const message = useMessage()

const prompt = ref('')
const selectedAgentId = ref('')
const isSending = ref(false)
const transcriptRef = ref<HTMLElement | null>(null)

const activeConversation = computed(() => store.activeConversation)
const activeMessages = computed(() => store.activeMessages)
const activeAgent = computed(() => {
  if (activeConversation.value?.agentId) {
    return store.agents.find((item) => item.id === activeConversation.value?.agentId) ?? null
  }

  if (selectedAgentId.value) {
    return store.agents.find((item) => item.id === selectedAgentId.value) ?? null
  }

  return null
})

// Filter conversations by selected agent
const sortedConversations = computed(() => {
  if (!selectedAgentId.value) return []
  return [...store.conversations]
    .filter(conv => conv.agentId === selectedAgentId.value)
    .sort((a, b) => new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime())
})

function getLatestConversationForAgent(agentId: string) {
  return [...store.conversations]
    .filter((conv) => conv.agentId === agentId)
    .sort((a, b) => new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime())[0] ?? null
}

async function syncAgentConversation(agentId: string) {
  selectedAgentId.value = agentId
  prompt.value = ''
  store.streamError = ''
  const latestConversation = getLatestConversationForAgent(agentId)

  if (latestConversation) {
    await store.fetchMessages(latestConversation.id)
    return
  }

  store.activeConversationId = ''
}

watch(
  activeMessages,
  async () => {
    await nextTick()
    if (transcriptRef.value) {
      transcriptRef.value.scrollTop = transcriptRef.value.scrollHeight
    }
  },
  { deep: true },
)

// Watch for route query changes (when navigating from Agents page)
watch(
  () => route.query.agentId,
  async (agentId) => {
    if (agentId && typeof agentId === 'string') {
      selectedAgentId.value = agentId
      await syncAgentConversation(agentId)
    }
  },
  { immediate: true },
)

watch(
  () => store.conversations,
  async (conversations) => {
    if (!selectedAgentId.value || conversations.length === 0) {
      return
    }

    if (!store.activeConversation || store.activeConversation.agentId !== selectedAgentId.value) {
      await syncAgentConversation(selectedAgentId.value)
    }
  },
  { deep: true },
)

// Watch for agents loading
watch(
  () => store.agents,
  async (agents) => {
    if (!selectedAgentId.value && agents.length > 0) {
      await syncAgentConversation(agents[0].id)
    }
  },
  { immediate: true },
)

async function createConversationForAgent() {
  if (!selectedAgentId.value) {
    message.warning('Create a model and agent first')
    return
  }

  const agent = store.agents.find((item) => item.id === selectedAgentId.value)
  const conversation = await store.createConversation(selectedAgentId.value, agent ? `${agent.name} session` : 'New chat')
  await store.fetchMessages(conversation.id)
}

async function selectConversation(id: string) {
  const conversation = store.conversations.find((item) => item.id === id)
  if (conversation) {
    selectedAgentId.value = conversation.agentId
  }
  await store.fetchMessages(id)
}

async function submitPrompt() {
  if (!prompt.value.trim()) {
    return
  }

  if (!selectedAgentId.value) {
    message.warning('Select an agent first')
    return
  }

  if (activeConversation.value && activeConversation.value.agentId !== selectedAgentId.value) {
    await syncAgentConversation(selectedAgentId.value)
  }

  if (!store.activeConversation || store.activeConversation.agentId !== selectedAgentId.value) {
    await createConversationForAgent()
  }

  if (!store.activeConversationId) {
    return
  }

  isSending.value = true
  try {
    await store.streamMessage(store.activeConversationId, prompt.value)
    await store.fetchMessages(store.activeConversationId)
    prompt.value = ''
  } catch (error) {
    message.error((error as Error).message)
  } finally {
    isSending.value = false
  }
}
</script>

<style scoped>
.conversation-card-list {
  display: grid;
  gap: 8px;
}

.conversation-entry {
  cursor: pointer;
  transition: all 0.2s ease;
}

.conversation-entry:hover {
  transform: translateY(-1px);
}

.conversation-entry.active {
  background: var(--primary-color-suppl);
  border-color: var(--primary-color);
}

.conversation-entry strong {
  display: block;
  margin-bottom: 4px;
}

.conversation-entry span {
  font-size: 12px;
  opacity: 0.7;
}

.conversation-list {
  max-height: 400px;
  overflow-y: auto;
}
</style>

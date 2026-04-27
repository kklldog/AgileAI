<template>
  <section class="page page-chat">
    <div class="chat-layout">
      <!-- Main Chat Area -->
      <n-card class="glass-card chat-stage" embedded>
        <template #header>
          <div class="chat-heading-meta">
            <div class="chat-heading-copy">
              <h3 class="chat-heading-title">{{ activeAgent?.name ?? activeConversation?.agentName ?? 'Chat' }}</h3>
            </div>
            <div class="chat-heading-side">
              <div class="chat-skill-state-row">
                <n-tag v-if="activeConversation?.activeSkill" size="small" type="success" bordered data-testid="active-skill-tag">
                  Active skill: {{ activeConversation.activeSkill.name }}
                </n-tag>
                <n-tag v-else-if="activeAgent?.enableSkills" size="small" type="warning" bordered>
                  Skills enabled
                </n-tag>
              </div>
            </div>
          </div>
        </template>

        <div v-if="activeMessages.length" ref="transcriptRef" class="message-stack" data-testid="chat-transcript">
          <article v-for="item in activeMessages" :key="item.id" class="message-bubble" :class="item.role.toLowerCase()" :data-testid="`message-${item.role.toLowerCase()}`">
            <span class="message-role">{{ item.role === 'User' ? 'You' : item.role === 'Assistant' ? activeConversation?.agentName ?? 'Assistant' : item.role }}</span>
            <div v-if="item.role === 'Assistant'">
              <div class="message-markdown" v-html="renderAssistantMessage(item.content)"></div>
              <div v-if="!item.isStreaming && (item.appliedSkillName || item.appliedToolNames?.length)" class="message-usage-summary">
                <span v-if="item.appliedSkillName" class="message-usage-chip" :data-testid="`message-skill-${item.id}`">
                  Skill used: {{ item.appliedSkillName }}
                </span>
                <span v-if="item.appliedToolNames?.length" class="message-usage-chip" :data-testid="`message-tools-${item.id}`">
                  Tools used: {{ item.appliedToolNames.join(', ') }}
                </span>
              </div>
              <div v-if="getToolHistoryForMessage(item.id).length" class="message-tool-history">
                <n-collapse accordion>
                  <n-collapse-item
                    v-for="tool in getToolHistoryForMessage(item.id)"
                    :key="tool.id"
                    :name="tool.id"
                    :title="tool.toolName"
                    :data-testid="`tool-history-${tool.id}`"
                  >
                    <template #header>
                      <div class="tool-history-header">
                        <span class="tool-history-name">{{ tool.toolName }}</span>
                        <n-tag
                          size="small"
                          :type="tool.status === 'Completed' ? 'success' : tool.status === 'Denied' ? 'error' : tool.status === 'Failed' ? 'error' : 'default'"
                        >
                          {{ tool.status }}
                        </n-tag>
                      </div>
                    </template>
                    <div class="tool-history-content">
                      <div class="tool-history-label">Command</div>
                      <pre class="approval-command tool-history-preview">{{ extractCommandPreview(tool.argumentsJson) }}</pre>
                      <div v-if="tool.resultContent" class="tool-history-result">
                        <div class="tool-history-label">Details</div>
                        <pre class="approval-command tool-history-preview result-preview">{{ tool.resultContent }}</pre>
                      </div>
                    </div>
                  </n-collapse-item>
                </n-collapse>
              </div>
            </div>
            <p v-else>{{ item.content }}</p>
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
            @keydown.enter.exact.prevent="submitPrompt"
          />
          <div class="composer-actions chat-composer-actions">
            <p v-if="store.isStreaming || store.streamError" class="chat-status-line">
              <span v-if="store.isStreaming">Streaming response in progress...</span>
              <span v-if="store.streamError">{{ store.streamError }}</span>
            </p>
            <p v-if="activeModelSummary" class="chat-model-line" data-testid="chat-model-line">Model: {{ activeModelSummary }}</p>
            <div class="chat-send-row">
              <n-button class="chat-send-button" type="primary" :loading="isSending" :disabled="Boolean(pendingApproval)" data-testid="send-message" @click="submitPrompt">Send</n-button>
            </div>
          </div>
        </div>
      </n-card>

      <!-- Right Sidebar: Conversation History -->
      <n-card class="glass-card chat-side" embedded>
        <template #header>
          <div class="chat-side-header">
            <span class="chat-side-title">Sessions</span>
            <n-button size="small" type="primary" secondary @click="handleNewSession">New Session</n-button>
          </div>
        </template>
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
              <span>{{ formatConversationMeta(conv) }}</span>
            </n-card>
          </div>
        </div>
      </n-card>
    </div>

    <n-modal
      :show="Boolean(pendingApproval)"
      :mask-closable="false"
      :close-on-esc="false"
      :auto-focus="false"
      :trap-focus="true"
    >
      <n-card
        v-if="pendingApproval"
        class="glass-card approval-modal-card"
        title="Approval Required"
        :bordered="false"
        size="huge"
        role="dialog"
        aria-modal="true"
        data-testid="approval-modal"
      >
        <div class="approval-header approval-modal-header">
          <strong>{{ pendingApproval.toolName }}</strong>
          <n-tag type="warning" size="small">Pending</n-tag>
        </div>
        <p class="approval-warning">This tool call will change local workspace state and needs your approval.</p>
        <pre class="approval-command">{{ extractCommandPreview(pendingApproval.argumentsJson) }}</pre>
        <n-checkbox v-model:checked="alwaysApproveInSession" data-testid="approval-auto-approve">
          Always approve tool calls in this session
        </n-checkbox>
        <p v-if="pendingApproval.decisionComment" class="approval-comment">{{ pendingApproval.decisionComment }}</p>
        <template #action>
          <div class="approval-actions">
            <n-button
              size="small"
              type="error"
              secondary
              :loading="store.resolvingApprovalIds.includes(pendingApproval.id)"
              :data-testid="`approval-reject-${pendingApproval.id}`"
              @click="resolveApproval(pendingApproval.id, false)"
            >
              Reject
            </n-button>
            <n-button
              size="small"
              type="primary"
              :loading="store.resolvingApprovalIds.includes(pendingApproval.id)"
              :data-testid="`approval-approve-${pendingApproval.id}`"
              @click="resolveApproval(pendingApproval.id, true)"
            >
              Approve
            </n-button>
          </div>
        </template>
      </n-card>
    </n-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useMessage, NButton, NCard, NCheckbox, NCollapse, NCollapseItem, NEmpty, NInput, NModal, NSpin, NTag } from 'naive-ui'
import DOMPurify from 'dompurify'
import { marked } from 'marked'
import { useStudioStore } from '../stores/studio'

const route = useRoute()
const store = useStudioStore()
const message = useMessage()

const prompt = ref('')
const selectedAgentId = ref('')
const isSending = ref(false)
const transcriptRef = ref<HTMLElement | null>(null)
const alwaysApproveInSession = ref(false)

const activeConversation = computed(() => store.activeConversation)
const activeMessages = computed(() => store.activeMessages)
const activeToolApprovals = computed(() => store.activeToolApprovals)
const pendingApproval = computed(() => activeToolApprovals.value.find((item) => item.status === 'Pending') ?? null)

marked.setOptions({
  breaks: true,
  gfm: true,
})

function renderAssistantMessage(content: string) {
  const html = marked.parse(content || '', { async: false })
  return DOMPurify.sanitize(html, {
    USE_PROFILES: { html: true },
  })
}

const activeAgent = computed(() => {
  if (activeConversation.value?.agentId) {
    return store.agents.find((item) => item.id === activeConversation.value?.agentId) ?? null
  }

  if (selectedAgentId.value) {
    return store.agents.find((item) => item.id === selectedAgentId.value) ?? null
  }

  return null
})

const activeModel = computed(() => {
  if (!activeAgent.value) {
    return null
  }

  return store.models.find((item) => item.id === activeAgent.value?.studioModelId) ?? null
})

const activeModelSummary = computed(() => {
  if (!activeModel.value) {
    return activeAgent.value?.modelDisplayName ?? ''
  }

  return `${activeModel.value.providerConnectionName} · ${activeModel.value.displayName}`
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

  const conversation = await store.createConversation(selectedAgentId.value, 'New Session')
  await store.fetchMessages(conversation.id)
}

async function handleNewSession() {
  await createConversationForAgent()
}

async function selectConversation(id: string) {
  const conversation = store.conversations.find((item) => item.id === id)
  if (conversation) {
    selectedAgentId.value = conversation.agentId
  }
  await store.fetchMessages(id)
}

const APPROVAL_PREVIEW_MAX_CHARS = 1200

function truncatePreviewText(text: string) {
  if (text.length <= APPROVAL_PREVIEW_MAX_CHARS) {
    return text
  }
  const remaining = text.length - APPROVAL_PREVIEW_MAX_CHARS
  return `${text.slice(0, APPROVAL_PREVIEW_MAX_CHARS)}\n… [truncated ${remaining} more characters]`
}

function extractCommandPreview(argumentsJson: string) {
  let raw: string
  try {
    const parsed = JSON.parse(argumentsJson) as { command?: string; workingDirectory?: string; shell?: string; timeoutMs?: number }
    const parts = [parsed.command ?? argumentsJson]
    if (parsed.workingDirectory) {
      parts.push(`cwd: ${parsed.workingDirectory}`)
    }
    if (parsed.shell) {
      parts.push(`shell: ${parsed.shell}`)
    }
    if (parsed.timeoutMs) {
      parts.push(`timeout: ${parsed.timeoutMs}ms`)
    }
    raw = parts.join('\n')
  } catch {
    raw = argumentsJson
  }
  return truncatePreviewText(raw)
}

function getToolHistoryForMessage(messageId: string) {
  return activeToolApprovals.value.filter((item) => item.assistantMessageId === messageId)
}

async function resolveApproval(approvalId: string, approved: boolean) {
  try {
    const conversationId = pendingApproval.value?.conversationId ?? store.activeConversationId
    if (approved && conversationId && alwaysApproveInSession.value) {
      store.setAutoApproveToolCallsForConversation(conversationId, true)
    }

    await store.resolveToolApprovalAction(approvalId, approved)
    message.success(approved ? 'Tool call approved' : 'Tool call rejected')
    if (!approved) {
      alwaysApproveInSession.value = false
    }
  } catch (error) {
    message.error((error as Error).message)
  }
}

watch(
  pendingApproval,
  (approval) => {
    if (!approval) {
      alwaysApproveInSession.value = false
      return
    }

    const shouldAutoApprove = Boolean(store.autoApproveToolCallsByConversation[approval.conversationId])
    alwaysApproveInSession.value = shouldAutoApprove
    if (shouldAutoApprove && !store.resolvingApprovalIds.includes(approval.id)) {
      void store.resolveToolApprovalAction(approval.id, true, 'Auto-approved for this session.')
    }
  },
  { immediate: true },
)

async function submitPrompt() {
  const content = prompt.value.trim()

  if (!content) {
    return
  }

  if (!selectedAgentId.value) {
    message.warning('Select an agent first')
    return
  }

  if (pendingApproval.value) {
    message.warning('Approve or reject the pending tool request before sending another message.')
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

  prompt.value = ''
  isSending.value = true
  try {
    await store.streamMessage(store.activeConversationId, content)
    await store.fetchMessages(store.activeConversationId)
  } catch (error) {
    prompt.value = content
    message.error((error as Error).message)
  } finally {
    isSending.value = false
  }
}

function formatConversationMeta(conversation: { createdAtUtc: string; messageCount: number }) {
  const createdAt = new Date(conversation.createdAtUtc)
  const dateLabel = Number.isNaN(createdAt.getTime())
    ? conversation.createdAtUtc
    : createdAt.toLocaleString([], {
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      })

  return `${dateLabel} · ${conversation.messageCount} messages`
}
</script>

<style scoped>
.chat-side-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.chat-side-title {
  font-size: 15px;
  font-weight: 600;
}

.chat-composer-actions {
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 12px;
  align-items: stretch;
}

.chat-send-row {
  display: flex;
  width: 100%;
  flex: 1 1 auto;
  justify-content: flex-end;
  align-self: stretch;
}

.chat-send-button {
  margin-left: auto;
}

.chat-skill-state-row {
  display: flex;
  gap: 8px;
  margin-top: 8px;
  justify-content: flex-end;
  flex-wrap: wrap;
}

.chat-heading-meta {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.chat-heading-side {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  margin-left: auto;
}

.approval-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.approval-modal-card {
  width: min(560px, calc(100vw - 32px));
}

.approval-modal-header {
  margin-bottom: 12px;
}

.approval-warning,
.approval-comment {
  margin: 0 0 10px;
  color: var(--text-color-2);
}

.approval-command {
  margin: 0 0 12px;
  padding: 12px;
  border-radius: 12px;
  background: rgba(15, 23, 42, 0.78);
  color: #e2e8f0;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 320px;
  overflow-y: auto;
  font-size: 12px;
  line-height: 1.5;
}

.approval-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.message-tool-history {
  margin-top: 14px;
}

.tool-history-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  width: 100%;
}

.tool-history-name {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace;
  font-size: 0.92em;
}

.tool-history-content {
  padding-top: 4px;
}

.tool-history-label {
  margin-bottom: 8px;
  font-size: 0.84em;
  color: var(--text-color-3);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.tool-history-result {
  margin-top: 12px;
}

.tool-history-preview {
  margin-bottom: 0;
  padding: 10px 12px;
  font-size: 0.86em;
}

.result-preview {
  max-height: 240px;
  overflow-y: auto;
}

.message-tool-history :deep(.n-collapse) {
  border-top: 1px solid rgba(148, 163, 184, 0.18);
}

.message-tool-history :deep(.n-collapse-item__header) {
  padding: 10px 0;
}

.message-tool-history :deep(.n-collapse-item__content-inner) {
  padding: 0 0 12px;
}

.chat-heading-copy {
  min-width: 0;
}

.chat-heading-title {
  margin: 0;
  font-size: 22px;
  line-height: 1.2;
}

.message-markdown {
  margin-top: 6px;
  line-height: 1.7;
}

.message-usage-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
}

.message-usage-chip {
  display: inline-flex;
  align-items: center;
  padding: 4px 10px;
  border-radius: 999px;
  background: rgba(148, 163, 184, 0.14);
  color: var(--text-color-2);
  font-size: 12px;
  line-height: 1.4;
}

.chat-model-line {
  margin: 0;
  color: var(--text-color-3);
  font-size: 12px;
  line-height: 1.5;
}

.message-markdown :deep(p) {
  margin: 0 0 12px;
}

.message-markdown :deep(p:last-child) {
  margin-bottom: 0;
}

.message-markdown :deep(ul),
.message-markdown :deep(ol) {
  margin: 0 0 12px;
  padding-left: 20px;
}

.message-markdown :deep(li + li) {
  margin-top: 4px;
}

.message-markdown :deep(pre) {
  overflow-x: auto;
  margin: 0 0 12px;
  padding: 12px 14px;
  border-radius: 14px;
  background: rgba(15, 23, 42, 0.78);
  color: #e2e8f0;
}

.message-markdown :deep(code) {
  font-family: ui-monospace, SFMono-Regular, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace;
  font-size: 0.92em;
}

.message-markdown :deep(:not(pre) > code) {
  padding: 0.16em 0.4em;
  border-radius: 8px;
  background: rgba(148, 163, 184, 0.2);
}

.message-markdown :deep(blockquote) {
  margin: 0 0 12px;
  padding-left: 12px;
  border-left: 3px solid rgba(20, 184, 166, 0.45);
  color: var(--text-soft);
}

.message-markdown :deep(a) {
  color: var(--accent);
  text-decoration: underline;
}

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

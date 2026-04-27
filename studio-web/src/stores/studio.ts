import { defineStore } from 'pinia'
import {
  createAgent,
  createConversation,
  createModel,
  createProviderConnection,
  deleteAgent,
  deleteModel,
  deleteProviderConnection,
  getAgents,
  getAgentTools,
  getConversations,
  getConversationToolApprovals,
  getMessages,
  getModels,
  getOverview,
  getProviderConnections,
  getSkills,
  resolveToolApproval,
  resolveToolApprovalStream,
  sendMessage,
  streamMessage,
  testModel,
  updateAgent,
  updateModel,
  updateProviderConnection,
  type AgentPayload,
  type ModelPayload,
  type ProviderConnectionPayload,
  type StreamHandlers,
} from '../api/studio'
import type { AgentItem, ConversationItem, MessageItem, ModelItem, Overview, ProviderConnection, SkillItem, ToolApprovalItem, ToolOption } from '../types'

export const useStudioStore = defineStore('studio', {
  state: () => ({
    overview: null as Overview | null,
    providerConnections: [] as ProviderConnection[],
    models: [] as ModelItem[],
    agents: [] as AgentItem[],
    agentTools: [] as ToolOption[],
    skills: [] as SkillItem[],
    conversations: [] as ConversationItem[],
    messagesByConversation: {} as Record<string, MessageItem[]>,
    toolApprovalsByConversation: {} as Record<string, ToolApprovalItem[]>,
    autoApproveToolCallsByConversation: {} as Record<string, boolean>,
    activeConversationId: '' as string,
    isLoading: false,
    isStreaming: false,
    streamError: '' as string,
    resolvingApprovalIds: [] as string[],
    deletingProviderIds: [] as string[],
    deletingModelIds: [] as string[],
    deletingAgentIds: [] as string[],
    validatingModelIds: [] as string[],
  }),
  getters: {
    activeConversation(state) {
      return state.conversations.find((item) => item.id === state.activeConversationId) ?? null
    },
    activeMessages(state) {
      return state.messagesByConversation[state.activeConversationId] ?? []
    },
    activeToolApprovals(state) {
      return state.toolApprovalsByConversation[state.activeConversationId] ?? []
    },
  },
  actions: {
    async bootstrap() {
      this.isLoading = true
      try {
        const [overview, providerConnections, models, agents, agentTools, skills, conversations] = await Promise.all([
          getOverview(),
          getProviderConnections(),
          getModels(),
          getAgents(),
          getAgentTools(),
          getSkills(),
          getConversations(),
        ])
        this.overview = overview
        this.providerConnections = providerConnections
        this.models = models
        this.agents = agents
        this.agentTools = agentTools
        this.skills = skills
        this.conversations = conversations
        if (!this.activeConversationId && conversations.length > 0) {
          this.activeConversationId = conversations[0].id
          await this.fetchMessages(this.activeConversationId)
        }
      } finally {
        this.isLoading = false
      }
    },
    async refreshOverview() {
      this.overview = await getOverview()
    },
    async createProviderConnection(payload: ProviderConnectionPayload) {
      const item = await createProviderConnection(payload)
      this.providerConnections.unshift(item)
      return item
    },
    async updateProviderConnection(id: string, payload: ProviderConnectionPayload) {
      const item = await updateProviderConnection(id, payload)
      this.providerConnections = this.providerConnections.map((entry) => (entry.id === id ? item : entry))
      return item
    },
    async deleteProviderConnection(id: string) {
      this.deletingProviderIds = [...this.deletingProviderIds, id]
      try {
        await deleteProviderConnection(id)
        this.providerConnections = this.providerConnections.filter((item) => item.id !== id)
      } finally {
        this.deletingProviderIds = this.deletingProviderIds.filter((item) => item !== id)
      }
    },
    async createModel(payload: ModelPayload) {
      const item = await createModel(payload)
      this.models.unshift(item)
      await this.refreshOverview()
      return item
    },
    async updateModel(id: string, payload: ModelPayload) {
      const item = await updateModel(id, payload)
      this.models = this.models.map((entry) => (entry.id === id ? item : entry))
      return item
    },
    async deleteModel(id: string) {
      this.deletingModelIds = [...this.deletingModelIds, id]
      try {
        await deleteModel(id)
        this.models = this.models.filter((item) => item.id !== id)
        await this.refreshOverview()
      } finally {
        this.deletingModelIds = this.deletingModelIds.filter((item) => item !== id)
      }
    },
    async testModel(id: string) {
      this.validatingModelIds = [...this.validatingModelIds, id]
      try {
        return await testModel(id)
      } finally {
        this.validatingModelIds = this.validatingModelIds.filter((item) => item !== id)
      }
    },
    async createAgent(payload: AgentPayload) {
      const item = await createAgent(payload)
      this.agents.unshift(item)
      await this.refreshOverview()
      return item
    },
    async updateAgent(id: string, payload: AgentPayload) {
      const item = await updateAgent(id, payload)
      this.agents = this.agents.map((entry) => (entry.id === id ? item : entry))
      return item
    },
    async deleteAgent(id: string) {
      this.deletingAgentIds = [...this.deletingAgentIds, id]
      try {
        await deleteAgent(id)
        this.agents = this.agents.filter((item) => item.id !== id)
        await this.refreshOverview()
      } finally {
        this.deletingAgentIds = this.deletingAgentIds.filter((item) => item !== id)
      }
    },
    async createConversation(agentId: string, title?: string) {
      const item = await createConversation(agentId, title)
      this.conversations.unshift(item)
      this.activeConversationId = item.id
      this.messagesByConversation[item.id] = []
      await this.refreshOverview()
      return item
    },
    async fetchMessages(conversationId: string) {
      const [items, approvals] = await Promise.all([
        getMessages(conversationId),
        getConversationToolApprovals(conversationId),
      ])
      this.messagesByConversation[conversationId] = items
      this.toolApprovalsByConversation[conversationId] = approvals
      this.activeConversationId = conversationId
      return items
    },
    setAutoApproveToolCallsForConversation(conversationId: string, enabled: boolean) {
      this.autoApproveToolCallsByConversation = {
        ...this.autoApproveToolCallsByConversation,
        [conversationId]: enabled,
      }
    },
    patchMessage(conversationId: string, messageId: string, patch: Partial<MessageItem>) {
      const messages = [...(this.messagesByConversation[conversationId] ?? [])]
      const index = messages.findIndex((item) => item.id === messageId)
      if (index < 0) {
        return
      }

      messages[index] = {
        ...messages[index],
        ...patch,
      }
      this.messagesByConversation[conversationId] = messages
    },
    upsertToolApproval(conversationId: string, approval: ToolApprovalItem) {
      const approvals = this.toolApprovalsByConversation[conversationId] ?? []
      const index = approvals.findIndex((item) => item.id === approval.id)
      if (index >= 0) {
        const next = [...approvals]
        next[index] = approval
        this.toolApprovalsByConversation[conversationId] = next
        return
      }

      this.toolApprovalsByConversation[conversationId] = [...approvals, approval]
    },
    patchToolApproval(conversationId: string, approvalId: string, patch: Partial<ToolApprovalItem>) {
      const approvals = [...(this.toolApprovalsByConversation[conversationId] ?? [])]
      const index = approvals.findIndex((item) => item.id === approvalId)
      if (index < 0) {
        return
      }

      approvals[index] = {
        ...approvals[index],
        ...patch,
      }
      this.toolApprovalsByConversation[conversationId] = approvals
    },
    upsertConversation(conversation: ConversationItem) {
      const matched = this.conversations.some((item) => item.id === conversation.id)
      this.conversations = matched
        ? this.conversations.map((item) => (item.id === conversation.id ? conversation : item))
        : [conversation, ...this.conversations]
    },
    async resolveAutoApprovedPendingApproval(conversationId: string) {
      if (!this.autoApproveToolCallsByConversation[conversationId]) {
        return
      }

      const pendingApproval = (this.toolApprovalsByConversation[conversationId] ?? [])
        .find((item) => item.status === 'Pending' && !this.resolvingApprovalIds.includes(item.id))

      if (!pendingApproval) {
        return
      }

      await this.resolveToolApprovalAction(pendingApproval.id, true, 'Auto-approved for this session.')
    },
    _addOptimisticChatTurn(conversationId: string, content: string) {
      const existing = this.messagesByConversation[conversationId] ?? []
      const optimisticUserId = `temp-user-${Date.now()}`
      const optimisticAssistantId = `temp-assistant-${Date.now()}`

      this.activeConversationId = conversationId
      this.messagesByConversation[conversationId] = [
        ...existing,
        {
          id: optimisticUserId,
          conversationId,
          role: 'User',
          content,
          isStreaming: false,
          createdAtUtc: new Date().toISOString(),
        },
        {
          id: optimisticAssistantId,
          conversationId,
          role: 'Assistant',
          content: '',
          isStreaming: true,
          createdAtUtc: new Date().toISOString(),
        },
      ]

      return { existing, optimisticUserId, optimisticAssistantId }
    },
    _createStreamHandlers(conversationId: string, getAssistantMessageId: () => string): StreamHandlers {
      return {
        onDelta: (delta) => {
          const id = getAssistantMessageId()
          const current = (this.messagesByConversation[conversationId] ?? []).find((item) => item.id === id)
          if (!current) {
            return
          }

          this.patchMessage(conversationId, id, {
            content: `${current.content}${delta}`,
            isStreaming: true,
          })
        },
        onUsage: ({ inputTokens, outputTokens }) => {
          this.patchMessage(conversationId, getAssistantMessageId(), {
            inputTokens,
            outputTokens,
          })
        },
        onCompleted: ({ finishReason }) => {
          this.patchMessage(conversationId, getAssistantMessageId(), {
            finishReason: finishReason ?? null,
            isStreaming: false,
          })
        },
        onFinalMessage: ({ content, finishReason, inputTokens, outputTokens, appliedSkillName, appliedToolNames }) => {
          this.patchMessage(conversationId, getAssistantMessageId(), {
            content,
            finishReason: finishReason ?? null,
            inputTokens,
            outputTokens,
            appliedSkillName: appliedSkillName ?? null,
            appliedToolNames: appliedToolNames ?? null,
            isStreaming: false,
          })
        },
        onApprovalRequired: (approval) => {
          this.upsertToolApproval(conversationId, approval)

          void this.resolveAutoApprovedPendingApproval(conversationId)
        },
        onError: (message) => {
          this.streamError = message
          this.patchMessage(conversationId, getAssistantMessageId(), {
            content: message,
            isStreaming: false,
          })
        },
      }
    },
    async resolveToolApprovalAction(approvalId: string, approved: boolean, comment?: string) {
      this.resolvingApprovalIds = [...this.resolvingApprovalIds, approvalId]
      try {
        const existingApproval = Object.values(this.toolApprovalsByConversation)
          .flat()
          .find((item) => item.id === approvalId)

        if (!existingApproval) {
          const result = await resolveToolApproval(approvalId, approved, comment)
          const conversationId = result.approval.conversationId
          this.upsertToolApproval(conversationId, result.approval)
          if (result.pendingApproval) {
            this.upsertToolApproval(conversationId, result.pendingApproval)
          }
          this.patchMessage(conversationId, result.assistantMessage.id, result.assistantMessage)
          this.conversations = this.conversations.map((item) =>
            item.id === conversationId ? result.conversation : item,
          )
          await this.refreshOverview()
          await this.resolveAutoApprovedPendingApproval(conversationId)
          return result
        }

        const conversationId = existingApproval.conversationId
        const assistantMessageId = existingApproval.assistantMessageId
        const previousMessages = [...(this.messagesByConversation[conversationId] ?? [])]
        const previousApprovals = [...(this.toolApprovalsByConversation[conversationId] ?? [])]
        this.patchMessage(conversationId, assistantMessageId, { isStreaming: true, content: '' })
        this.patchToolApproval(conversationId, approvalId, {
          decisionComment: comment ?? null,
          status: approved ? 'Approved' : 'Denied',
        })

        try {
          await resolveToolApprovalStream(approvalId, approved, comment, this._createStreamHandlers(conversationId, () => assistantMessageId))
        } catch (error) {
          try {
            const [refreshedApprovals, refreshedMessages] = await Promise.all([
              getConversationToolApprovals(conversationId),
              getMessages(conversationId),
            ])
            this.toolApprovalsByConversation[conversationId] = refreshedApprovals
            this.messagesByConversation[conversationId] = refreshedMessages
          } catch {
            this.toolApprovalsByConversation[conversationId] = previousApprovals
            this.messagesByConversation[conversationId] = previousMessages
          }

          throw error
        }

        const [refreshedApprovals, refreshedMessages] = await Promise.all([
          getConversationToolApprovals(conversationId),
          getMessages(conversationId),
        ])
        this.toolApprovalsByConversation[conversationId] = refreshedApprovals
        this.messagesByConversation[conversationId] = refreshedMessages
        this.conversations = await getConversations()
        await this.refreshOverview()
        await this.resolveAutoApprovedPendingApproval(conversationId)
        return null
      } finally {
        this.resolvingApprovalIds = this.resolvingApprovalIds.filter((item) => item !== approvalId)
      }
    },
    async sendMessage(conversationId: string, content: string) {
      const normalizedContent = content.trim()
      const { existing } = this._addOptimisticChatTurn(conversationId, normalizedContent)

      try {
        const result = await sendMessage(conversationId, normalizedContent)
        this.messagesByConversation[conversationId] = [...existing, result.userMessage, result.assistantMessage]
        this.upsertConversation(result.conversation)
        await this.refreshOverview()
        return result
      } catch (error) {
        this.messagesByConversation[conversationId] = existing
        throw error
      }
    },
    async streamMessage(conversationId: string, content: string) {
      const normalizedContent = content.trim()
      const { existing, optimisticAssistantId } = this._addOptimisticChatTurn(conversationId, normalizedContent)

      this.isStreaming = true
      this.streamError = ''
      let currentAssistantMessageId = optimisticAssistantId

      try {
        await streamMessage(conversationId, normalizedContent, {
          onStart: ({ conversation, userMessage, assistantMessage }) => {
            this.messagesByConversation[conversationId] = [...existing, userMessage, assistantMessage]
            currentAssistantMessageId = assistantMessage.id
            this.upsertConversation(conversation)
          },
          ...this._createStreamHandlers(conversationId, () => currentAssistantMessageId),
        })

        await this.refreshOverview()
        this.conversations = await getConversations()
      } catch (error) {
        this.messagesByConversation[conversationId] = existing
        throw error
      } finally {
        this.isStreaming = false
      }
    },
  },
})

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
    async resolveToolApprovalAction(approvalId: string, approved: boolean, comment?: string) {
      this.resolvingApprovalIds = [...this.resolvingApprovalIds, approvalId]
      try {
        const existingApproval = Object.values(this.toolApprovalsByConversation)
          .flat()
          .find((item) => item.id === approvalId)

        if (!existingApproval) {
          const result = await resolveToolApproval(approvalId, approved, comment)
          const conversationId = result.approval.conversationId
          const approvals = this.toolApprovalsByConversation[conversationId] ?? []
          const updatedApprovals = approvals.map((item) =>
            item.id === approvalId ? result.approval : item,
          )

          this.toolApprovalsByConversation[conversationId] = result.pendingApproval
            ? updatedApprovals.some((item) => item.id === result.pendingApproval?.id)
              ? updatedApprovals.map((item) => item.id === result.pendingApproval?.id ? result.pendingApproval : item)
              : [...updatedApprovals, result.pendingApproval]
            : updatedApprovals

          const messages = [...(this.messagesByConversation[conversationId] ?? [])]
          const index = messages.findIndex((item) => item.id === result.assistantMessage.id)
          if (index >= 0) {
            messages[index] = result.assistantMessage
          } else {
            messages.push(result.assistantMessage)
          }

          this.messagesByConversation[conversationId] = messages
          this.conversations = this.conversations.map((item) =>
            item.id === conversationId ? result.conversation : item,
          )
          await this.refreshOverview()
          return result
        }

        const conversationId = existingApproval.conversationId
        const assistantMessageId = existingApproval.assistantMessageId
        const patchAssistantMessage = (patch: Partial<MessageItem>) => {
          const messages = [...(this.messagesByConversation[conversationId] ?? [])]
          const index = messages.findIndex((item) => item.id === assistantMessageId)
          if (index < 0) {
            return
          }

          messages[index] = {
            ...messages[index],
            ...patch,
          }
          this.messagesByConversation[conversationId] = messages
        }

        const updateApproval = (approval: ToolApprovalItem) => {
          const approvals = this.toolApprovalsByConversation[conversationId] ?? []
          const index = approvals.findIndex((item) => item.id === approval.id)
          if (index >= 0) {
            const next = [...approvals]
            next[index] = approval
            this.toolApprovalsByConversation[conversationId] = next
          } else {
            this.toolApprovalsByConversation[conversationId] = [...approvals, approval]
          }
        }

        patchAssistantMessage({ isStreaming: true, content: '' })

        await resolveToolApprovalStream(approvalId, approved, comment, {
          onDelta: (delta) => {
            const current = (this.messagesByConversation[conversationId] ?? []).find((item) => item.id === assistantMessageId)
            patchAssistantMessage({
              content: `${current?.content ?? ''}${delta}`,
              isStreaming: true,
            })
          },
          onUsage: ({ inputTokens, outputTokens }) => {
            patchAssistantMessage({
              inputTokens,
              outputTokens,
            })
          },
          onFinalMessage: ({ content, finishReason, inputTokens, outputTokens, appliedSkillName, appliedToolNames }) => {
            patchAssistantMessage({
              content,
              finishReason: finishReason ?? null,
              inputTokens,
              outputTokens,
              appliedSkillName: appliedSkillName ?? null,
              appliedToolNames: appliedToolNames ?? null,
              isStreaming: false,
            })
          },
          onCompleted: ({ finishReason }) => {
            patchAssistantMessage({
              finishReason: finishReason ?? null,
              isStreaming: false,
            })
          },
          onApprovalRequired: (approval) => {
            updateApproval(approval)
            if (this.autoApproveToolCallsByConversation[conversationId]) {
              void this.resolveToolApprovalAction(approval.id, true, 'Auto-approved for this session.')
            }
          },
          onError: (message) => {
            this.streamError = message
            patchAssistantMessage({
              content: message,
              isStreaming: false,
            })
          },
        })

        const refreshedApprovals = await getConversationToolApprovals(conversationId)
        const refreshedMessages = await getMessages(conversationId)
        this.toolApprovalsByConversation[conversationId] = refreshedApprovals
        this.messagesByConversation[conversationId] = refreshedMessages
        this.conversations = await getConversations()
        await this.refreshOverview()
        return null
      } finally {
        this.resolvingApprovalIds = this.resolvingApprovalIds.filter((item) => item !== approvalId)
      }
    },
    async sendMessage(conversationId: string, content: string) {
      const normalizedContent = content.trim()
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
          content: normalizedContent,
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

      const result = await sendMessage(conversationId, content)
      this.messagesByConversation[conversationId] = [...existing, result.userMessage, result.assistantMessage]
      this.conversations = this.conversations.map((item) =>
        item.id === conversationId ? result.conversation : item,
      )
      await this.refreshOverview()
      return result
    },
    async streamMessage(conversationId: string, content: string) {
      const normalizedContent = content.trim()
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
          content: normalizedContent,
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

      this.isStreaming = true
      this.streamError = ''

      try {
        await streamMessage(conversationId, normalizedContent, {
          onStart: ({ conversation, userMessage, assistantMessage }) => {
            this.messagesByConversation[conversationId] = [...existing, userMessage, assistantMessage]
            const matched = this.conversations.some((item) => item.id === conversationId)
            this.conversations = matched
              ? this.conversations.map((item) => (item.id === conversationId ? conversation : item))
              : [conversation, ...this.conversations]
          },
          onDelta: (delta) => {
            const list = [...(this.messagesByConversation[conversationId] ?? [])]
            const last = list.at(-1)
            if (!last) {
              return
            }

            list[list.length - 1] = {
              ...last,
              content: `${last.content}${delta}`,
              isStreaming: true,
            }
            this.messagesByConversation[conversationId] = list
          },
          onUsage: ({ inputTokens, outputTokens }) => {
            const list = [...(this.messagesByConversation[conversationId] ?? [])]
            const last = list.at(-1)
            if (!last) {
              return
            }

            list[list.length - 1] = {
              ...last,
              inputTokens: inputTokens ?? last.inputTokens,
              outputTokens: outputTokens ?? last.outputTokens,
            }
            this.messagesByConversation[conversationId] = list
          },
          onCompleted: ({ finishReason }) => {
            const list = [...(this.messagesByConversation[conversationId] ?? [])]
            const last = list.at(-1)
            if (!last) {
              return
            }

            list[list.length - 1] = {
              ...last,
              finishReason: finishReason ?? last.finishReason,
              isStreaming: false,
            }
            this.messagesByConversation[conversationId] = list
          },
          onFinalMessage: ({ content, finishReason, inputTokens, outputTokens, appliedSkillName, appliedToolNames }) => {
            const list = [...(this.messagesByConversation[conversationId] ?? [])]
            const last = list.at(-1)
            if (!last) {
              return
            }

            list[list.length - 1] = {
              ...last,
              content,
              finishReason: finishReason ?? last.finishReason,
              inputTokens: inputTokens ?? last.inputTokens,
              outputTokens: outputTokens ?? last.outputTokens,
              appliedSkillName: appliedSkillName ?? last.appliedSkillName ?? null,
              appliedToolNames: appliedToolNames ?? last.appliedToolNames ?? null,
              isStreaming: false,
            }
            this.messagesByConversation[conversationId] = list
          },
          onApprovalRequired: (approval) => {
            const approvals = this.toolApprovalsByConversation[conversationId] ?? []
            const index = approvals.findIndex((item) => item.id === approval.id)
            if (index >= 0) {
              const next = [...approvals]
              next[index] = approval
              this.toolApprovalsByConversation[conversationId] = next
            } else {
              this.toolApprovalsByConversation[conversationId] = [...approvals, approval]
            }

            if (this.autoApproveToolCallsByConversation[conversationId]) {
              void this.resolveToolApprovalAction(approval.id, true, 'Auto-approved for this session.')
            }
          },
          onError: (message) => {
            this.streamError = message
            const list = [...(this.messagesByConversation[conversationId] ?? [])]
            const last = list.at(-1)
            if (!last) {
              return
            }

            list[list.length - 1] = {
              ...last,
              content: message,
              isStreaming: false,
            }
            this.messagesByConversation[conversationId] = list
          },
        })

        await this.refreshOverview()
        this.conversations = await getConversations()
      } finally {
        this.isStreaming = false
      }
    },
  },
})

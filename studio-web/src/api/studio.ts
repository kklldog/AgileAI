import { http } from './http'
import type { AgentItem, ChatStreamStart, ConversationItem, MessageItem, ModelItem, Overview, ProviderConnection } from '../types'

const messageRoleMap = ['System', 'User', 'Assistant', 'Tool'] as const

function normalizeMessageRole(role: MessageItem['role'] | number | string): MessageItem['role'] {
  if (typeof role === 'string') {
    return role as MessageItem['role']
  }

  return messageRoleMap[role] ?? 'Assistant'
}

function normalizeMessage(item: MessageItem & { role: MessageItem['role'] | number | string }): MessageItem {
  return {
    ...item,
    role: normalizeMessageRole(item.role),
  }
}

export interface ProviderConnectionPayload {
  name: string
  providerType: 'OpenAI' | 'OpenAICompatible' | 'AzureOpenAI'
  apiKey: string
  baseUrl?: string | null
  endpoint?: string | null
  providerName?: string | null
  relativePath?: string | null
  apiKeyHeaderName?: string | null
  authMode?: string | null
  apiVersion?: string | null
  isEnabled: boolean
}

export interface ModelPayload {
  providerConnectionId: string
  displayName: string
  modelKey: string
  supportsStreaming: boolean
  supportsTools: boolean
  supportsVision: boolean
  isEnabled: boolean
}

export interface AgentPayload {
  studioModelId: string
  name: string
  description: string
  systemPrompt: string
  temperature: number
  maxTokens: number
  enableSkills: boolean
  isPinned: boolean
}

export async function getOverview() {
  const { data } = await http.get<Overview>('/overview')
  return data
}

export async function getProviderConnections() {
  const { data } = await http.get<ProviderConnection[]>('/provider-connections')
  return data
}

export async function createProviderConnection(payload: ProviderConnectionPayload) {
  const { data } = await http.post<ProviderConnection>('/provider-connections', payload)
  return data
}

export async function updateProviderConnection(id: string, payload: ProviderConnectionPayload) {
  const { data } = await http.put<ProviderConnection>(`/provider-connections/${id}`, payload)
  return data
}

export async function deleteProviderConnection(id: string) {
  await http.delete(`/provider-connections/${id}`)
}

export async function getModels() {
  const { data } = await http.get<ModelItem[]>('/models')
  return data
}

export async function createModel(payload: ModelPayload) {
  const { data } = await http.post<ModelItem>('/models', payload)
  return data
}

export async function updateModel(id: string, payload: ModelPayload) {
  const { data } = await http.put<ModelItem>(`/models/${id}`, payload)
  return data
}

export async function deleteModel(id: string) {
  await http.delete(`/models/${id}`)
}

export async function testModel(id: string) {
  const { data } = await http.post<{ success: boolean; message: string }>(`/models/${id}/test`)
  return data
}

export async function getAgents() {
  const { data } = await http.get<AgentItem[]>('/agents')
  return data
}

export async function createAgent(payload: AgentPayload) {
  const { data } = await http.post<AgentItem>('/agents', payload)
  return data
}

export async function updateAgent(id: string, payload: AgentPayload) {
  const { data } = await http.put<AgentItem>(`/agents/${id}`, payload)
  return data
}

export async function deleteAgent(id: string) {
  await http.delete(`/agents/${id}`)
}

export async function getConversations() {
  const { data } = await http.get<ConversationItem[]>('/conversations')
  return data
}

export async function createConversation(agentId: string, title?: string) {
  const { data } = await http.post<ConversationItem>('/conversations', { agentId, title })
  return data
}

export async function getMessages(conversationId: string) {
  const { data } = await http.get<MessageItem[]>(`/conversations/${conversationId}/messages`)
  return data.map((item) => normalizeMessage(item as MessageItem & { role: MessageItem['role'] | number | string }))
}

export async function sendMessage(conversationId: string, content: string) {
  const { data } = await http.post(`/conversations/${conversationId}/messages`, { content })
  return data as {
    conversation: ConversationItem
    userMessage: MessageItem
    assistantMessage: MessageItem
  }
}

export interface StreamHandlers {
  onStart?: (payload: ChatStreamStart) => void
  onDelta?: (delta: string) => void
  onUsage?: (payload: { inputTokens?: number | null; outputTokens?: number | null }) => void
  onCompleted?: (payload: { finishReason?: string | null }) => void
  onFinalMessage?: (payload: { content: string; finishReason?: string | null; inputTokens?: number | null; outputTokens?: number | null }) => void
  onError?: (message: string) => void
}

export async function streamMessage(conversationId: string, content: string, handlers: StreamHandlers) {
  const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5117/api').replace(/\/$/, '')
  const response = await fetch(`${baseUrl}/conversations/${conversationId}/stream`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ content }),
  })

  if (!response.ok || !response.body) {
    throw new Error('Unable to start streaming response.')
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  const emitEvent = (chunk: string) => {
    const lines = chunk.split('\n')
    let eventName = ''
    let data = ''

    for (const line of lines) {
      if (line.startsWith('event:')) {
        eventName = line.slice(6).trim()
      }
      if (line.startsWith('data:')) {
        data += line.slice(5).trim()
      }
    }

    if (!eventName || !data) {
      return
    }

        const payload = JSON.parse(data)
        switch (eventName) {
      case 'message-created':
        handlers.onStart?.({
          ...payload,
          userMessage: normalizeMessage(payload.userMessage),
          assistantMessage: normalizeMessage(payload.assistantMessage),
        } as ChatStreamStart)
        break
      case 'text-delta':
        handlers.onDelta?.(payload.delta ?? '')
        break
      case 'usage':
        handlers.onUsage?.(payload)
        break
      case 'completed':
        handlers.onCompleted?.(payload)
        break
      case 'final-message':
        handlers.onFinalMessage?.(payload)
        break
      case 'error':
        handlers.onError?.(payload.message ?? 'Streaming failed')
        break
    }
  }

  while (true) {
    const { value, done } = await reader.read()
    if (done) {
      break
    }

    buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n')
    let separatorIndex = buffer.indexOf('\n\n')
    while (separatorIndex >= 0) {
      const chunk = buffer.slice(0, separatorIndex)
      buffer = buffer.slice(separatorIndex + 2)
      emitEvent(chunk)
      separatorIndex = buffer.indexOf('\n\n')
    }
  }

  const finalChunk = buffer.trim()
  if (finalChunk) {
    emitEvent(finalChunk)
  }
}

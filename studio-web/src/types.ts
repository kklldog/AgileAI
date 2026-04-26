export type ProviderType = 'OpenAI' | 'OpenAICompatible' | 'AzureOpenAI' | 'DeepSeek'

export interface ProviderConnection {
  id: string
  name: string
  providerType: ProviderType
  apiKeyPreview: string
  baseUrl?: string | null
  endpoint?: string | null
  providerName?: string | null
  relativePath?: string | null
  apiKeyHeaderName?: string | null
  authMode?: string | null
  apiVersion?: string | null
  isEnabled: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ModelItem {
  id: string
  providerConnectionId: string
  providerConnectionName: string
  providerType: ProviderType
  displayName: string
  modelKey: string
  supportsStreaming: boolean
  supportsTools: boolean
  supportsVision: boolean
  isEnabled: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface AgentItem {
  id: string
  studioModelId: string
  name: string
  description: string
  systemPrompt: string
  temperature: number
  maxTokens: number
  enableSkills: boolean
  isPinned: boolean
  selectedToolNames: string[]
  allowedSkillNames: string[]
  modelDisplayName: string
  runtimeModelId: string
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ToolOption {
  name: string
  description?: string | null
}

export interface ConversationSkillState {
  name: string
  description?: string | null
}

export interface SkillItem {
  name: string
  description?: string | null
  version?: string | null
  entryMode?: string | null
  triggers: string[]
  files: string[]
}

export interface ConversationItem {
  id: string
  agentId: string
  agentName: string
  title: string
  createdAtUtc: string
  updatedAtUtc: string
  messageCount: number
  activeSkill?: ConversationSkillState | null
}

export type MessageRole = 'System' | 'User' | 'Assistant' | 'Tool'

export interface MessageItem {
  id: string
  conversationId: string
  role: MessageRole
  content: string
  isStreaming: boolean
  finishReason?: string | null
  inputTokens?: number | null
  outputTokens?: number | null
  appliedSkillName?: string | null
  appliedToolNames?: string[] | null
  createdAtUtc: string
}

export interface ChatStreamStart {
  conversation: ConversationItem
  userMessage: MessageItem
  assistantMessage: MessageItem
}

export type ToolApprovalStatus = 'Pending' | 'Approved' | 'Denied' | 'Completed' | 'Failed'

export interface ToolApprovalItem {
  id: string
  conversationId: string
  assistantMessageId: string
  approvalRequestId: string
  toolCallId: string
  toolName: string
  argumentsJson: string
  status: ToolApprovalStatus
  decisionComment?: string | null
  resultContent?: string | null
  exitCode?: number | null
  standardOutput?: string | null
  standardError?: string | null
  requestedAtUtc: string
  decidedAtUtc?: string | null
  completedAtUtc?: string | null
}

export interface ToolApprovalResolutionResult {
  approval: ToolApprovalItem
  assistantMessage: MessageItem
  conversation: ConversationItem
  pendingApproval?: ToolApprovalItem | null
}

export interface Overview {
  modelCount: number
  agentCount: number
  conversationCount: number
  recentConversations: ConversationItem[]
}

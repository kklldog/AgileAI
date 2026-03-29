# SESSION_HANDOFF.md — AgileAI 当前交接状态

> 最后更新: 2026-03-29

---

## 1. 本次交付摘要

本轮工作已经从“Agent 工具选择”继续推进到 **可审批的本地命令执行能力**，并补齐了 core / Studio / 测试 / 文档这几层。

当前已完成并验证的结果：

- `dotnet build AgileAI.slnx` ✅
- `dotnet test tests/AgileAI.Tests/AgileAI.Tests.csproj --filter "FullyQualifiedName~ChatSessionTests|FullyQualifiedName~ToolApprovalServiceTests"` ✅
- `studio-web npm run build` ✅
- `cd studio-web && npm run test:e2e` ✅（默认套件 7 passed / 1 skipped）

---

## 2. 本轮已完成内容

### 2.1 Core：可审批的工具执行机制

修改文件：

- `src/AgileAI.Abstractions/IApprovalAwareTool.cs`
- `src/AgileAI.Abstractions/IToolExecutionGate.cs`
- `src/AgileAI.Abstractions/ToolApprovalDecision.cs`
- `src/AgileAI.Abstractions/ToolApprovalMode.cs`
- `src/AgileAI.Abstractions/ToolApprovalRequest.cs`
- `src/AgileAI.Abstractions/ToolExecutionStatus.cs`
- `src/AgileAI.Abstractions/ToolDefinition.cs`
- `src/AgileAI.Abstractions/ToolExecutionContext.cs`
- `src/AgileAI.Abstractions/ToolResult.cs`
- `src/AgileAI.Abstractions/ChatTurnResult.cs`
- `src/AgileAI.Abstractions/IChatSession.cs`
- `src/AgileAI.Core/AutoApproveToolExecutionGate.cs`
- `src/AgileAI.Core/ToolExecutor.cs`
- `src/AgileAI.Core/ChatSession.cs`
- `src/AgileAI.Core/ChatSessionBuilder.cs`
- `src/AgileAI.Core/InMemoryToolRegistry.cs`

已完成：

- core 新增 approval-aware tool execution primitives
- 工具可声明是否需要审批（`ToolApprovalMode.PerExecution`）
- `ChatSession` 不再只能“自动执行工具直到结束”，现在支持：
  - `SendTurnAsync(...)`
  - `ContinueAsync(...)`
- 当工具需要审批时，会返回 `PendingApprovalRequest`
- tool loop 可以暂停，并在审批后继续
- 默认仍保留 auto-approve gate，保证旧宿主不被破坏

### 2.2 Studio Backend：本地命令工具、审批持久化与恢复

修改文件：

- `src/AgileAI.Studio.Api/Contracts/ToolApprovals.cs`
- `src/AgileAI.Studio.Api/Data/StudioDbContext.cs`
- `src/AgileAI.Studio.Api/Domain/Conversation.cs`
- `src/AgileAI.Studio.Api/Domain/ToolApprovalRequestEntity.cs`
- `src/AgileAI.Studio.Api/Domain/ToolApprovalStatus.cs`
- `src/AgileAI.Studio.Api/Infrastructure/StudioDbSeeder.cs`
- `src/AgileAI.Studio.Api/Program.cs`
- `src/AgileAI.Studio.Api/Services/AgentExecutionService.cs`
- `src/AgileAI.Studio.Api/Services/AgentService.cs`
- `src/AgileAI.Studio.Api/Services/ProcessExecutionService.cs`
- `src/AgileAI.Studio.Api/Services/StudioToolExecutionGate.cs`
- `src/AgileAI.Studio.Api/Services/StudioToolRegistryFactory.cs`
- `src/AgileAI.Studio.Api/Services/ToolApprovalService.cs`
- `src/AgileAI.Studio.Api/Tools/RunLocalCommandTool.cs`
- `src/AgileAI.Studio.Api/Services/MockChatModelProvider.cs`

已完成：

- 新增 `run_local_command` 工具
- 工具本身 **不限制命令能力**
- 每次执行默认都需要人工审批
- 审批请求会持久化到 SQLite
- 新增 API：
  - `GET /api/conversations/{id}/tool-approvals`
  - `POST /api/tool-approvals/{id}/resolve`
- 审批后支持恢复同一轮对话
- 已支持 **链式审批**：
  - approve 一个工具后
  - 模型如果又请求新的审批型工具
  - 会继续进入下一次 pending approval
- `ProcessExecutionService` 已兼容：
  - Windows
  - Linux
  - macOS
- `MockChatModelProvider` 已支持稳定触发本地命令审批路径，便于 Playwright 测试

### 2.3 Studio Frontend：工具历史、阻塞审批模态框、会话内自动同意

修改文件：

- `studio-web/src/api/studio.ts`
- `studio-web/src/stores/studio.ts`
- `studio-web/src/types.ts`
- `studio-web/src/views/ChatPage.vue`

已完成：

- chat 页面现在会把工具调用历史展示在 assistant 回复区域内
- 当前实现是基于 `ToolApprovalItem.assistantMessageId` 将历史挂到对应 assistant message
- 展示规则：
  - 默认直接显示工具名与状态
  - 详细内容折叠显示
- 原本 transcript 下方的 approval cards 已移除
- 改为 **blocking modal** 审批：
  - 不能点遮罩关闭
  - 不能按 ESC 关闭
  - pending approval 存在时，发送按钮禁用
  - `submitPrompt()` 也会拦截
- 审批弹框新增选项：
  - `Always approve tool calls in this session`
- 勾选后，当前 conversation 后续 pending approvals 会被前端自动 resolve，不再继续人工审批

### 2.4 测试与验证增强

修改文件：

- `tests/AgileAI.Tests/ChatSessionTests.cs`
- `tests/AgileAI.Tests/ToolApprovalServiceTests.cs`
- `studio-web/playwright.config.ts`
- `studio-web/tests/studio.spec.ts`

已完成：

- core 测试覆盖：
  - pending approval 中断
  - `ContinueAsync(...)` 恢复
  - 链式 approval continuation
- Studio backend 测试覆盖：
  - `ToolApprovalService` resolve 流程
- Playwright harness 现在可同时启动：
  - Studio API
  - Vite frontend
- 默认 e2e 套件现状：
  - 7 passed
  - 1 skipped
- 被 skip 的场景是“真实 provider 浏览器测试”，已改为 **显式 opt-in**：
  - `PW_REAL_ENDPOINT`
  - `PW_REAL_API_KEY`
  - `PW_REAL_MODEL_KEY`
- 新的 Playwright 用例已覆盖：
  - 审批模态框出现
  - approval modal 内 approve
  - assistant 回复中出现 inline tool history
  - 勾选“本 session 永久同意”后，后续同会话工具调用自动审批

### 2.5 文档更新

修改文件：

- `README.md`

已完成：

- README 新增 approval-aware tool execution 说明
- README 更新了 Studio 的本地命令执行说明
- README 更新了 Playwright / real-provider 测试说明

---

## 3. 当前运行方式

### 后端

```bash
dotnet run --project src/AgileAI.Studio.Api/AgileAI.Studio.Api.csproj
```

默认访问：

- `http://localhost:5117`

### 前端

```bash
cd studio-web
npm install
npm run dev -- --host 127.0.0.1 --port 5173
```

默认访问：

- `http://localhost:5173`

### 默认验证命令

```bash
dotnet build AgileAI.slnx

dotnet test tests/AgileAI.Tests/AgileAI.Tests.csproj --filter "FullyQualifiedName~ChatSessionTests|FullyQualifiedName~ToolApprovalServiceTests"

cd studio-web
npm run build
npm run test:e2e
```

### 可选真实 provider 浏览器验证

```bash
export PW_REAL_ENDPOINT="http://your-openai-compatible-endpoint"
export PW_REAL_API_KEY="your-real-api-key"
export PW_REAL_MODEL_KEY="your-model-name"

cd studio-web
npm run test:e2e
```

---

## 4. 当前已知情况

- 默认本地验证链路已经稳定：build / unit tests / Playwright 默认套件都通过
- 当前 inline tool history 主要来自 **审批记录**，因此：
  - 审批型工具调用会显示得很好
  - 如果未来要“所有工具调用都展示历史”，则需要后端扩展 message-level tool history contract / SSE event
- “本 session 永久同意”目前是 **frontend session policy**：
  - 按 conversation 生效
  - 不会全局生效
  - 不会跨新 session 继承
- 真实 provider 仍然可能有配置/endpoint 差异问题，尤其是非 mock 的 OpenAI-compatible 路径差异，这不影响当前默认本地套件通过

---

## 5. 下一步建议（待做）

### 5.1 把工具历史下沉成正式后端消息模型

当前前端可以展示审批型工具调用历史，但若要覆盖所有工具，建议补：

- assistant message 上的正式 `toolHistory` contract
- 或者新增 SSE tool-call 事件
- conversation reload 时直接返回 message-linked tool history

这样之后像 `read_file` / `search_files` / `write_file` 之类不需要审批的工具，也能统一显示在回答区域里。

### 5.2 评估是否把“本 session 永久同意”下沉到后端

当前自动同意是前端行为。若未来希望：

- 页面刷新后仍保留
- 多客户端一致
- 或更强审计能力

则可以考虑后端记录 conversation-level approval policy。

### 5.3 真实 provider 更深入回归

如果后面拿到稳定的真实 endpoint，建议回归：

- OpenAI-compatible 相对路径拼接
- streaming / non-streaming 行为差异
- Azure deployment 映射

---

## 6. 关键文件清单

- `SESSION_HANDOFF.md`
- `README.md`
- `src/AgileAI.Abstractions/IApprovalAwareTool.cs`
- `src/AgileAI.Abstractions/IToolExecutionGate.cs`
- `src/AgileAI.Abstractions/ToolApprovalDecision.cs`
- `src/AgileAI.Abstractions/ToolApprovalMode.cs`
- `src/AgileAI.Abstractions/ToolApprovalRequest.cs`
- `src/AgileAI.Abstractions/ToolExecutionStatus.cs`
- `src/AgileAI.Abstractions/ToolDefinition.cs`
- `src/AgileAI.Abstractions/ToolExecutionContext.cs`
- `src/AgileAI.Abstractions/ToolResult.cs`
- `src/AgileAI.Abstractions/ChatTurnResult.cs`
- `src/AgileAI.Abstractions/IChatSession.cs`
- `src/AgileAI.Core/AutoApproveToolExecutionGate.cs`
- `src/AgileAI.Core/ToolExecutor.cs`
- `src/AgileAI.Core/ChatSession.cs`
- `src/AgileAI.Core/ChatSessionBuilder.cs`
- `src/AgileAI.Core/InMemoryToolRegistry.cs`
- `src/AgileAI.Studio.Api/Contracts/ToolApprovals.cs`
- `src/AgileAI.Studio.Api/Data/StudioDbContext.cs`
- `src/AgileAI.Studio.Api/Domain/Conversation.cs`
- `src/AgileAI.Studio.Api/Domain/ToolApprovalRequestEntity.cs`
- `src/AgileAI.Studio.Api/Domain/ToolApprovalStatus.cs`
- `src/AgileAI.Studio.Api/Infrastructure/StudioDbSeeder.cs`
- `src/AgileAI.Studio.Api/Program.cs`
- `src/AgileAI.Studio.Api/Services/AgentExecutionService.cs`
- `src/AgileAI.Studio.Api/Services/AgentService.cs`
- `src/AgileAI.Studio.Api/Services/MockChatModelProvider.cs`
- `src/AgileAI.Studio.Api/Services/ProcessExecutionService.cs`
- `src/AgileAI.Studio.Api/Services/StudioToolExecutionGate.cs`
- `src/AgileAI.Studio.Api/Services/StudioToolRegistryFactory.cs`
- `src/AgileAI.Studio.Api/Services/ToolApprovalService.cs`
- `src/AgileAI.Studio.Api/Tools/RunLocalCommandTool.cs`
- `studio-web/playwright.config.ts`
- `studio-web/src/api/studio.ts`
- `studio-web/src/stores/studio.ts`
- `studio-web/src/types.ts`
- `studio-web/src/views/ChatPage.vue`
- `studio-web/tests/studio.spec.ts`
- `tests/AgileAI.Tests/ChatSessionTests.cs`
- `tests/AgileAI.Tests/ToolApprovalServiceTests.cs`

---

**交接人**: Sisyphus  
**日期**: 2026-03-29

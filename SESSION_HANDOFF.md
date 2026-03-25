# SESSION_HANDOFF.md — AgileAI Studio 项目交接

> 最后更新: 2025-03-25

---

## 1. 项目概述

**AgileAI.Studio** 是一个基于大语言模型的 AI Agent 应用，包含：
- **AgileAI.Studio.Api** — 后端 API (.NET 9)
- **AgileAI.Studio** — 前端 Web UI (Vue 3 + Vite)
- **AgileAI.Extensions.FileSystem** — 文件系统工具扩展（当前重点）

项目结构采用 Clean Architecture，支持多 Provider（OpenAI、Azure、Custom）。

---

## 2. 当前状态

### ✅ 已完成 (Phase 1-2)

| 组件 | 状态 | 说明 |
|------|------|------|
| `list_directory` | ✅ | 列出目录内容 |
| `search_files` | ✅ | 搜索文件（含内容） |
| `read_file` | ✅ | 读取单个文件 |
| `read_files_batch` | ✅ | 批量读取文件 |
| `write_file` | ✅ | 写入/覆盖文件 |
| `create_directory` | ✅ | 创建目录（含父目录） |
| **`move_file`** | ✅ **Phase 2** | 移动/重命名文件或目录 |

**Phase 1 新增实现：**
- `CreateDirectoryTool.cs` — 完整工具实现
- `FileSystemToolRegistryFactory.cs` — 已注册新工具
- `ServiceCollectionExtensions.cs` — 已添加 DI 注册
- `CreateDirectoryToolTests.cs` — 9 个单元测试覆盖所有场景

**Phase 2 新增实现：**
- `MoveFileTool.cs` — 完整工具实现（支持文件和目录移动）
- `MoveFileToolTests.cs` — 4 个单元测试覆盖：文件移动、目录移动、源不存在异常
- `FileSystemToolRegistryFactory.cs` — 已注册 `move_file` 工具
- `ServiceCollectionExtensions.cs` — 已添加 DI 注册

### 📋 待办工作 (Phase 3-4)

| Phase | 工具 | 优先级 | 说明 |
|-------|------|--------|------|
| **Phase 3** | `patch_file` | 低 | 文本级别的 diff/patch 操作 |
| **Phase 4** | `delete_file` | 低 | 删除文件（需更强的 guardrails） |
| | `delete_directory` | 低 | 删除目录（需更强的 guardrails） |

**Delete 操作的 Guardrails 建议：**
1. 确认机制（如 `--force` 标志或二次确认）
2. 回收站/垃圾桶（移到临时目录而非永久删除）
3. 白名单机制（只允许删除特定扩展名的文件）
4. 审计日志（记录所有删除操作）

---

## 3. 技术细节

### 文件系统工具架构

```
AgileAI.Extensions.FileSystem/
├── ITool (from AgileAI.Abstractions)
├── FileSystemPathGuard          # 路径安全检查
├── FileSystemToolOptions        # 配置选项
├── FileSystemToolRegistryFactory # 工具注册工厂
├── DependencyInjection/
│   └── ServiceCollectionExtensions.cs
└── Tools/
    ├── ListDirectoryTool.cs
    ├── SearchFilesTool.cs
    ├── ReadFileTool.cs
    ├── ReadFilesBatchTool.cs
    ├── WriteFileTool.cs
    └── CreateDirectoryTool.cs  # 新增
```

### 安全模型

所有工具都通过 `FileSystemPathGuard` 进行路径验证：

1. **ResolvePath**: 将相对路径解析为绝对路径
2. **Root Path 验证**: 确保解析后的路径在配置的根目录内
3. **路径逃逸防护**: 检测 `../` 等逃逸尝试并抛出异常

```csharp
// 示例：路径验证逻辑
var resolvedPath = pathGuard.ResolvePath(request.Path);
// 如果 request.Path = "../../../etc/passwd"
// 将抛出 InvalidOperationException: "Path escapes the configured filesystem root"
```

---

## 4. 环境配置

### 快速开始

```bash
# 1. 启动后端 API
cd AgileAI/src/AgileAI.Studio.Api
dotnet run --urls "http://localhost:5100"

# 2. 启动前端
cd AgileAI/src/AgileAI.Studio
npm run dev

# 3. 运行测试
cd AgileAI
dotnet test
```

### 关键配置

`appsettings.Development.json`:
```json
{
  "FileSystemTools": {
    "RootPath": "/path/to/workspace"
  },
  "ModelCatalog": {
    "DefaultModelId": "gpt-4o"
  }
}
```

---

## 5. 交接人备注

### 当前焦点

Phase 2 (`move_file`) **已实现完成**，包含完整的工具实现和单元测试。下一步建议：

1. **代码审查**: 检查 `MoveFileTool.cs` 和 `MoveFileToolTests.cs`
2. **集成测试**: 在 Studio 中实际使用 `move_file` 工具（注：dotnet test 运行测试需要完整 .NET SDK 环境）
3. **Phase 3 规划**: 确定 `patch_file` 的优先级和实现方式

### 已知问题

- 无已知 Blocker
- 单元测试已编写但 dotnet SDK 环境缺失，本地测试运行被阻塞

### 建议

- **Delete 操作 (Phase 4)**: 建议在实际需求明确后再实现，因为需要复杂的安全 guardrails
- **Patch 操作 (Phase 3)**: 优先级较低，因为 `write_file` 已能满足大多数场景

---

## 6. 相关链接

- 项目仓库: `https://github.com/kklldog/AgileAI`
- 相关文档: `README.md`, `ARCHITECTURE.md` (待创建)

---

**交接人**: Sisyphus  
**接收人**: (待填写)  
**日期**: 2025-01-22

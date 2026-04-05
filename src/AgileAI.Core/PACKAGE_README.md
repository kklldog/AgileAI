# AgileAI.Core

`AgileAI.Core` contains the runtime building blocks for AgileAI agents, tool execution, sessions, middleware, and local skill orchestration.

## Included building blocks

- agent runtime primitives and middleware pipeline support
- in-memory registries for tools, skills, and sessions
- chat session orchestration and tool execution helpers
- prompt skill execution helpers and local file skill support
- built-in reusable tools such as `run_local_command` and `web_fetch`

## Typical usage

Reference `AgileAI.Core` from your application, register or construct the runtime services you need, and compose them with provider packages such as the OpenAI, Azure OpenAI, Claude, Gemini, or OpenAI-compatible integrations in this repository.

For end-to-end examples and Studio integration details, see the repository README:

- <https://github.com/kklldog/AgileAI>

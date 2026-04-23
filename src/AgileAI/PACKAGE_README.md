# AgileAI

`AgileAI` is the starter metapackage for the AgileAI SDK family.

It provides a simple entry point for consumers who want the shared SDK foundations without choosing every package manually.

## Included packages

This metapackage currently pulls in:

- `AgileAI.Abstractions`
- `AgileAI.Core`

## Not included

This package does **not** include provider packages or optional extensions.

Install providers explicitly based on the backend you want to use, for example:

- `AgileAI.Providers.OpenAI`
- `AgileAI.Providers.AzureOpenAI`
- `AgileAI.Providers.OpenAICompatible`
- `AgileAI.Providers.OpenAIResponses`
- `AgileAI.Providers.Gemini`
- `AgileAI.Providers.Claude`

Install extensions explicitly when you need them, for example:

- `AgileAI.Extensions.FileSystem`

## Typical installation

```bash
dotnet add package AgileAI
dotnet add package AgileAI.Providers.OpenAI
```

## Why this package exists

AgileAI is intentionally modular. This starter package gives you the common runtime building blocks while keeping provider and extension selection explicit.

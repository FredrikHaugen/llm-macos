# LlmMacos

A C# macOS desktop app (Avalonia + .NET 8) for local LLM workflows:

- Search Hugging Face for GGUF models.
- Download selected files with resumable transfer and progress updates.
- Load downloaded models with LLamaSharp (Metal-first with CPU fallback).
- Chat in a modern tabbed UI with streaming responses and stop-generation support.
- Persist chat sessions/settings locally and store optional HF token in macOS Keychain.

## Current Status

This repository now contains a full MVP codebase aligned to the implementation plan:

- `src/LlmMacos.App` - Avalonia UI + MVVM + DI composition root.
- `src/LlmMacos.Core` - models, interfaces, and shared logic.
- `src/LlmMacos.Infrastructure` - Hugging Face client, download manager, persistence, keychain wrapper, LLamaSharp adapter.
- `tests/LlmMacos.Core.Tests` - unit tests for core logic.
- `tests/LlmMacos.Infrastructure.Tests` - infrastructure behavior tests with mocked HTTP/filesystem.

## Requirements

- macOS on Apple Silicon (`arm64`)
- .NET 8 SDK
- Xcode Command Line Tools

### Install .NET 8 SDK

If `dotnet --info` is not available:

```bash
brew install --cask dotnet-sdk
```

Then add to shell profile if needed:

```bash
export PATH="/usr/local/share/dotnet:$PATH"
```

(Use `/opt/homebrew` alternatives if your setup differs.)

## Run (Local Dev)

```bash
dotnet restore llm-macos.sln
dotnet build llm-macos.sln
dotnet run --project src/LlmMacos.App/LlmMacos.App.csproj
```

## Test

```bash
dotnet test llm-macos.sln
```

## App Data Paths

The app uses:

`~/Library/Application Support/LlmMacos`

Created folders/files:

- `models/`
- `downloads/`
- `chats/`
- `settings/settings.json`
- `logs/app-<date>.log`
- `model-registry.json`

## Key Runtime Notes

- Only GGUF models are supported in v1.
- Inference service targets local LLamaSharp runtime.
- Model load defaults to Metal acceleration and falls back to CPU on initialization failure.
- Hugging Face token is optional; when provided, it is stored in Keychain under service:
  `com.figge.llm-macos.hf-token`

## Known Limitations (MVP)

- No signed/notarized macOS distribution pipeline yet.
- One active loaded model at a time.
- Prompt formatting is a generic chat template and may need model-specific templates for best quality.

## Validation

Validated in this repository with .NET SDK `8.0.418`:

```bash
dotnet build llm-macos.sln
dotnet test llm-macos.sln
```

## High-Level Flow

1. Search model repos in **Models** tab.
2. Pick a GGUF file and start download.
3. Watch progress/cancel in **Downloads** tab.
4. Load the downloaded model in **Chat** tab.
5. Send prompts and stream responses.
6. Configure token/system prompt/runtime defaults in **Settings** tab.

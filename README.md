# OneShotPrompt

OneShotPrompt is a .NET 10 console application for running one-shot AI jobs from YAML configuration.

It uses Microsoft Agent Framework for agent execution, supports OpenAI, Anthropic, and OpenAI-compatible endpoints, persists lightweight job memory when enabled, and is configured for Native AOT publishing by default.

## What It Does

- Loads jobs from `config.yaml`.
- Runs all enabled jobs or a single named job.
- Loads bundled and user-provided Agent Skills.
- Runs an automatic tool-selection pass before execution.
- Streams live agent activity (thinking, tool calls, results) via Spectre.Console when running interactively.
- Writes structured logs to a `logs/` folder next to the config file.
- Persists per-job memory in `.oneshotprompt/memory/` when enabled.
- Leaves scheduling to the operating system.

## How It Fits Together

```mermaid
flowchart LR
  Config["config.yaml"] --> Console["CLI / interactive console"]
  Skills["Bundled + custom skills"] --> Selector["Tool-selection pass"]
  Console --> Selector
  Selector --> Agent["Execution agent"]
  Agent --> Tools["Filesystem + process tools"]
  Agent --> Memory["Per-job memory"]
  Agent --> Providers["OpenAI / Anthropic / Compatible APIs"]
  Agent --> Events["Job events"]
  Events --> Terminal["Spectre.Console live view"]
  Events --> Logs["logs/*.log"]
```

## Quick Start

1. Copy `config.yaml.example` to `config.yaml`.
2. Fill in the provider settings you need and add at least one job.
3. Validate the config.
4. List jobs or run one.

```powershell
dotnet run --project src/OneShotPrompt.Console -- validate --config config.yaml
dotnet run --project src/OneShotPrompt.Console -- jobs --config config.yaml
dotnet run --project src/OneShotPrompt.Console -- run --config config.yaml
dotnet run --project src/OneShotPrompt.Console -- run --config config.yaml --job downloads-cleanup
dotnet run --project src/OneShotPrompt.Console -- interactive
```

If you run the app with no arguments from an interactive terminal, it opens a Spectre.Console selection menu. You can also open that menu explicitly with `interactive` or `-i`. When output is redirected (for example in scheduled tasks), a no-argument invocation defaults to `run --config config.yaml`.

## Interactive Console

When launched without arguments in an interactive terminal, or with `interactive` / `-i`, OneShotPrompt presents a menu:

```
────────────── OneShotPrompt ──────────────
Config file: config.yaml
Select action:
> Run direct prompt
  Run all jobs
  Run specific job
  Validate
  List jobs
  Clear memories
```

`Run direct prompt` lets you choose a provider, decide whether mutation tools are allowed, and execute ad-hoc prompts against the same config-backed provider settings used by named jobs.

During job execution, live streaming shows agent reasoning, tool calls, and tool results:

```
> Running job: downloads-cleanup
  ⏳ Reasoning...
  → GetKnownFolder (name: downloads)
  ← GetKnownFolder: C:\Users\user\Downloads
  ⏳ Reasoning...
  → ListDirectory (path: C:\Users\user\Downloads)
  ← ListDirectory: [dir] images [file] doc.pdf...
  ⏳ Reasoning...
  → MoveFiles (sourcePaths: [...], destinationPaths: [...])
  ← MoveFiles: Batch move completed: 5 succeeded, 0 failed.
```

## Logging

Every `run` command writes a timestamped log file to `logs/` next to the active config file. Interactive direct prompts also write logs there. Log entries include thinking events, tool calls with arguments, tool results, response chunks, and job lifecycle events.

## Docs

- [Configuration guide](docs/configuration.md)
- [Operations guide](docs/operations.md)
- [Windows Task Scheduler walkthrough](docs/windows-task-scheduler.md)
- [Linux scheduling walkthrough](docs/linux-scheduling.md)

## Project Layout

- `src/OneShotPrompt.Core`: domain models and configuration types.
- `src/OneShotPrompt.Application`: use cases and orchestration.
- `src/OneShotPrompt.Infrastructure`: YAML loading, provider integration, low-level built-in tools, and memory persistence.
- `src/OneShotPrompt.Console`: CLI entrypoint with Native AOT enabled and bundled Agent Skills.

## Notes

- `ThinkingLevel` accepts `low`, `medium`, or `high`.
- `AutoApprove: false` exposes read-only tools only.
- `AutoApprove: true` enables file-changing tools and process execution tools.
- `MoveFiles` moves multiple files in parallel for faster batch operations.
- `interactive` and `-i` open the Spectre.Console menu explicitly.
- `AllowedTools` can further restrict the tool catalog before selection.
- Custom skills can be placed in a `skills/` directory next to the active config file.
- Job memory is stored in `.oneshotprompt/memory/` next to the active config file.

## Build

```powershell
dotnet build OneShotPrompt.slnx
```

## Development Conventions

- NuGet package versions are managed centrally in `Directory.Packages.props`.
- Repo-wide C# formatting and code style preferences live in `.editorconfig`.

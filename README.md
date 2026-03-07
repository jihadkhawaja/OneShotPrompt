# OneShotPrompt

OneShotPrompt is a .NET 10 console application for running one-shot AI jobs from YAML configuration.

It uses Microsoft Agent Framework for agent execution, supports OpenAI, Anthropic, and OpenAI-compatible endpoints, persists lightweight job memory when enabled, and is configured for Native AOT publishing by default.

## What It Does

- Loads jobs from `config.yaml`.
- Runs all enabled jobs or a single named job.
- Uses local filesystem tools so jobs can inspect and change files.
- Persists per-job execution memory in `.oneshotprompt/memory/` when enabled.
- Leaves scheduling to the operating system, which keeps the app simple and predictable.

## Solution Layout

- `src/OneShotPrompt.Core`: domain models and configuration types.
- `src/OneShotPrompt.Application`: use cases and orchestration.
- `src/OneShotPrompt.Infrastructure`: YAML loading, provider integration, filesystem tools, and memory persistence.
- `src/OneShotPrompt.Console`: CLI entrypoint with Native AOT enabled.

## Docs And Scripts

- `docs/configuration.md`: supported config fields, validation rules, and safety notes.
- `docs/operations.md`: validate, list, run, publish, and inspect runtime artifacts.
- `docs/windows-task-scheduler.md`: Windows scheduling workflow for the published executable.
- `docs/linux-scheduling.md`: Linux scheduling with `cron` or `systemd` timers.
- `scripts/validate-config.ps1`: validate a config file.
- `scripts/list-jobs.ps1`: print configured jobs.
- `scripts/run-job.ps1`: run all enabled jobs or one named job.
- `scripts/publish-aot.ps1`: publish a Native AOT build.
- `scripts/register-daily-task.ps1`: register a daily Windows scheduled task for a published binary.

## Configuration

Start from `config.yaml.example` and create your own `config.yaml`.

```yaml
OpenAI:
	ApiKey: ""
	Model: "gpt-5-nano"

Anthropic:
	ApiKey: ""
	Model: "claude-haiku-4-5"

OpenAICompatible:
	Endpoint: "http://localhost:1234/v1"
	ApiKey: "lm-studio"
	Model: "default"

ThinkingLevel: "low"

PersistMemory: true

Jobs:
	- Name: "downloads-cleanup"
		Prompt: "Organize files in Downloads by type"
		Provider: "OpenAI"
		AutoApprove: true
		PersistMemory: false
		ThinkingLevel: "low"
		Schedule: "Daily at midnight"
		Enabled: true
```

### Notes

- `ThinkingLevel` accepts `low`, `medium`, or `high`.
- Job-level `ThinkingLevel` and `PersistMemory` override the global values.
- `Schedule` is metadata for humans and deployment scripts. The app does not run its own scheduler.
- `AutoApprove: true` enables file-changing tools like move, copy, delete, create directory, and write file.
- `AutoApprove: false` exposes read-only tools only, so the agent can inspect and propose a plan without mutating files.

## Commands

```powershell
dotnet run --project src/OneShotPrompt.Console -- run --config config.yaml
dotnet run --project src/OneShotPrompt.Console -- run --config config.yaml --job downloads-cleanup
dotnet run --project src/OneShotPrompt.Console -- validate --config config.yaml
dotnet run --project src/OneShotPrompt.Console -- jobs --config config.yaml
```

If you run the app with no arguments, it defaults to `run --config config.yaml`.

## Scheduling

Scheduling is intentionally delegated to the OS.

### Windows Task Scheduler

Use the published executable as the action target.

Program:

```text
OneShotPrompt.Console.exe
```

Arguments:

```text
run --config C:\path\to\config.yaml --job downloads-cleanup
```

### Linux cron

```cron
0 0 * * * /opt/oneshotprompt/OneShotPrompt.Console run --config /etc/oneshotprompt/config.yaml --job downloads-cleanup
```

## Native AOT

Native AOT is enabled in `src/OneShotPrompt.Console/OneShotPrompt.Console.csproj`.

Publish for Windows ARM64:

```powershell
dotnet publish src/OneShotPrompt.Console/OneShotPrompt.Console.csproj -c Release -r win-arm64
```

The published binary is emitted under:

```text
src/OneShotPrompt.Console/bin/Release/net10.0/win-arm64/publish/
```

### Current AOT Status

- OpenAI and OpenAI-compatible flows publish successfully.
- Anthropic currently publishes with upstream trim/AOT warnings from the `Anthropic` dependency. The native publish still succeeds, but this remains the main runtime risk area.

## Runtime Artifacts

- Job memory is stored in `.oneshotprompt/memory/` next to the active config file.
- This folder is ignored by Git.

## Build

```powershell
dotnet build OneShotPrompt.slnx
```

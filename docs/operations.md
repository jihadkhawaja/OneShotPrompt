# Operations Guide

This guide is for running OneShotPrompt reliably in day-to-day use, whether you are invoking it manually, using the interactive console, or preparing it for scheduled execution.

For command syntax, see [cli-reference.md](./cli-reference.md). For config shape, see [configuration.md](./configuration.md).

## Standard Workflow

Use this sequence when introducing a new config or changing an existing one:

1. Validate the config.
2. List jobs to confirm names and enabled state.
3. Run one job explicitly if you are testing a change.
4. Run all enabled jobs only when the config is stable.
5. Publish a Native AOT build before wiring the app into an operating-system scheduler.

## Validate First

```powershell
dotnet run --project src/OneShotPrompt.Console -- validate --config config.yaml
```

Or use the helper script:

```powershell
./scripts/validate-config.ps1 -ConfigPath ./config.yaml
```

Successful validation prints the number of jobs loaded from the file.

## Confirm Job Names

```powershell
dotnet run --project src/OneShotPrompt.Console -- jobs --config config.yaml
```

Or:

```powershell
./scripts/list-jobs.ps1 -ConfigPath ./config.yaml
```

Output format:

```text
- downloads-cleanup | Provider=OpenAI | Enabled=True | Schedule=Daily at midnight
```

This is the safest way to confirm the exact `--job` name before automating anything.

## Run Jobs

Run all enabled jobs:

```powershell
dotnet run --project src/OneShotPrompt.Console -- run --config config.yaml
```

Run one named job:

```powershell
dotnet run --project src/OneShotPrompt.Console -- run --config config.yaml --job downloads-cleanup
```

Helper script for either form:

```powershell
./scripts/run-job.ps1 -ConfigPath ./config.yaml
./scripts/run-job.ps1 -ConfigPath ./config.yaml -JobName downloads-cleanup
```

If a named job is missing or disabled, the command returns a non-zero exit code.

If multiple jobs are selected, execution continues through the set and returns non-zero if any selected job fails.

## Interactive Console

Start the menu explicitly:

```powershell
dotnet run --project src/OneShotPrompt.Console -- interactive
```

Short alias:

```powershell
dotnet run --project src/OneShotPrompt.Console -- -i
```

Behavior details:

- No arguments in an interactive terminal open the same menu automatically.
- No arguments in redirected or scheduled contexts do not open the menu; those flows should use explicit commands.
- The menu prompts for a config path first and then offers these actions:
  - Run direct prompt
  - Run all jobs
  - Run specific job
  - Validate
  - List jobs
  - Clear memories
  - Exit

`Run direct prompt` lets you choose a provider, decide whether mutation tools are allowed, and then enter prompts repeatedly until you submit an empty prompt.

`Clear memories` deletes all `*.json` files under `.oneshotprompt/memory/` next to the active config file after confirmation.

## Console Output And Logs

During job execution, OneShotPrompt writes:

- The job or ad-hoc prompt header.
- Tool-selection telemetry.
- Workflow telemetry.
- The model response.
- Failure messages when exceptions occur.

When the app is attached to an interactive terminal, it also streams live job events through Spectre.Console, including reasoning, tool calls, tool results, and generated group-chat messages for `corporate-planning` jobs.

Tool-selection telemetry includes:

- Total tools available before any allowlist filtering.
- Tools eligible after allowlist filtering.
- The effective workflow for the job.
- The configured allowlist, when one exists.
- Whether the selector was used.
- The final selected tool set.
- Any selector rationale returned by the model.

For `corporate-planning` jobs, the telemetry also includes each generated planning participant and the subset of tools assigned to that participant.

Every `run` invocation writes a timestamped log file under `logs/` next to the active config file. Interactive direct prompts write to the same location.

If a job uses `Workflow: "corporate-planning"`, the log file also records streamed group-chat events and explicit output boundaries around the final rendered response.

## Workflow Selection

Jobs default to `Workflow: "single-agent"`.

Use `Workflow: "corporate-planning"` when the task benefits from a short planning discussion among dynamically generated specialists before OneShotPrompt returns the final answer. Keep `single-agent` for straightforward tasks where extra coordination would add latency without improving the result.

If you adopt `corporate-planning`, validate the related top-level settings as part of rollout:

- `CorporatePlanning.MaxAgents`
- `CorporatePlanning.MaxIterations`

## Memory Behavior

When memory persistence is enabled, each job stores a compact JSON history under:

```text
.oneshotprompt/memory/
```

Operationally important details:

- Memory is scoped to the config directory, not to the repository root unless the config lives there.
- Each job keeps up to 10 entries.
- Stored entries include timestamp, prompt, and response.
- The interactive menu can clear all persisted memory files in one step.

If you do not want persisted context between runs, set `PersistMemory: false` globally or per job.

## Publish For Scheduled Runs

The console app is set up for Native AOT publishing.

Direct publish:

```powershell
dotnet publish src/OneShotPrompt.Console/OneShotPrompt.Console.csproj -c Release -r win-arm64
```

Helper script:

```powershell
./scripts/publish-aot.ps1 -RuntimeIdentifier win-arm64
```

Supported runtime identifiers in the helper script:

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`

Typical output path:

```text
src/OneShotPrompt.Console/bin/Release/net10.0/<rid>/publish/
```

Before registering a scheduler entry, run the published binary manually once with explicit arguments.

## Scheduled Execution

OneShotPrompt does not contain an internal scheduler.

- Windows: use Task Scheduler. See [windows-task-scheduler.md](./windows-task-scheduler.md).
- Linux: use `cron` or `systemd`. See [linux-scheduling.md](./linux-scheduling.md).

For unattended runs, prefer the explicit form below over relying on default behavior:

```text
run --config <path> --job <name>
```

## Release Packaging

The repository includes a manual GitHub Actions workflow named `Publish Release`.

Provide `release_version` as the exact tag to publish. The workflow creates or updates the corresponding release and attaches zipped Native AOT publish outputs for the supported platforms.
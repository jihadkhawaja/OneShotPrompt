# CLI Reference

This page is the command-level reference for OneShotPrompt.

For operator workflow, see [operations.md](./operations.md). For config semantics, see [configuration.md](./configuration.md).

## Command Summary

```text
OneShotPrompt

Commands:
  run [--config <path>] [--job <name>]
  validate [--config <path>]
  jobs [--config <path>]
  interactive
  help
```

## Default Behavior

Behavior depends on how the app is launched:

- No arguments in an interactive terminal: open the interactive menu.
- No arguments with redirected input or output: behave like `run --config config.yaml`.
- `interactive` or `-i`: always open the menu.

In scheduled or scripted contexts, use explicit commands instead of relying on the no-argument fallback.

## Commands

### `help`

Prints the usage summary and exits successfully.

Examples:

```powershell
dotnet run --project src/OneShotPrompt.Console -- help
dotnet run --project src/OneShotPrompt.Console -- --help
dotnet run --project src/OneShotPrompt.Console -- -h
```

### `validate`

Loads and validates the config file without running any jobs.

```powershell
dotnet run --project src/OneShotPrompt.Console -- validate --config config.yaml
```

Success output:

```text
Configuration is valid. Jobs: 4.
```

Returns non-zero if the config file is missing or invalid.

### `jobs`

Prints every configured job, ordered by job name.

```powershell
dotnet run --project src/OneShotPrompt.Console -- jobs --config config.yaml
```

Output format:

```text
- downloads-cleanup | Provider=OpenAI | Enabled=True | Schedule=Daily at midnight
```

The `Schedule` field is metadata from the config. It does not create a scheduler entry.

### `run`

Runs all enabled jobs or one named enabled job.

Run all enabled jobs:

```powershell
dotnet run --project src/OneShotPrompt.Console -- run --config config.yaml
```

Run one enabled job:

```powershell
dotnet run --project src/OneShotPrompt.Console -- run --config config.yaml --job downloads-cleanup
```

Behavior details:

- If `--job` is omitted, all enabled jobs run.
- If `--job` is provided, matching is case-insensitive.
- Disabled jobs are not selected.
- If no enabled job matches, the command returns non-zero.
- If several jobs run and any one fails, the overall command returns non-zero.

During execution, the command prints:

- `> Running job: <name>`
- Tool-selection telemetry
- The model response
- Failure messages when exceptions occur

### `interactive` and `-i`

Opens the Spectre.Console menu.

```powershell
dotnet run --project src/OneShotPrompt.Console -- interactive
dotnet run --project src/OneShotPrompt.Console -- -i
```

Menu actions:

- Run direct prompt
- Run all jobs
- Run specific job
- Validate
- List jobs
- Clear memories
- Exit

`Run direct prompt` lets you pick a provider, choose whether mutation tools are available, and then submit ad-hoc prompts until you enter an empty prompt.

## Options

### `--config <path>`

Supported by:

- `run`
- `validate`
- `jobs`

Default value: `config.yaml`

The config file path also determines where sibling runtime content is resolved:

- `logs/`
- `skills/`
- `.oneshotprompt/memory/`

### `--job <name>`

Supported by:

- `run`

When omitted, all enabled jobs run. When provided, only the matching enabled job runs.

## Exit Behavior

High-level exit behavior:

- `help`: `0`
- `validate`: `0` on success, non-zero on invalid config or missing file
- `jobs`: `0` on success, non-zero on config load failure
- `run`: `0` only when every selected job succeeds
- `interactive`: returns the last action's exit code

## Helper Scripts

The repository includes PowerShell wrappers around the main CLI:

### Validate Config

```powershell
./scripts/validate-config.ps1 -ConfigPath ./config.yaml
```

### List Jobs

```powershell
./scripts/list-jobs.ps1 -ConfigPath ./config.yaml
```

### Run All Jobs Or One Job

```powershell
./scripts/run-job.ps1 -ConfigPath ./config.yaml
./scripts/run-job.ps1 -ConfigPath ./config.yaml -JobName downloads-cleanup
```

### Publish AOT

```powershell
./scripts/publish-aot.ps1 -RuntimeIdentifier win-arm64
```

Supported runtime identifiers:

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`
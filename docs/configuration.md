# Configuration Guide

OneShotPrompt reads a single YAML file and validates it before any job runs. Start with `config.example.yaml`, copy it to `config.yaml`, and then fill in only the provider settings you actually use.

## Root Settings

### `OpenAI`

- `ApiKey`: API key used when a job sets `Provider: "OpenAI"`.
- `Model`: Chat model name. The default sample uses `gpt-5-nano`.

### `Anthropic`

- `ApiKey`: API key used when a job sets `Provider: "Anthropic"`.
- `Model`: Model name. The default sample uses `claude-haiku-4-5`.

### `Gemini`

- `ApiKey`: API key used when a job sets `Provider: "Gemini"`.
- `Model`: Gemini model name. The default sample uses `gemini-2.5-flash`.

Gemini jobs are backed by `GeminiDotnet.Extensions.AI`, so they continue to use the same `Microsoft.Extensions.AI` and Microsoft Agent Framework execution path as the other chat-backed providers while remaining compatible with Native AOT publishing.

### `OpenAICompatible`

- `Endpoint`: Base endpoint for an OpenAI-compatible server.
- `ApiKey`: API key or local token for that endpoint.
- `Model`: Model name exposed by that endpoint.

### `GitHubCopilot`

- `Model`: Default model name for jobs that set `Provider: "GitHubCopilot"`. Leave it blank to let the CLI choose its default.
- `CliPath`: Optional explicit path to the GitHub Copilot CLI executable.
- `CliUrl`: Optional URL for an already running Copilot CLI server. This is mutually exclusive with `CliPath`.
- `LogLevel`: Optional Copilot CLI log level. The default is `info`.
- `GitHubToken`: Optional explicit GitHub token for Copilot CLI auth.
- `UseLoggedInUser`: Optional auth override. When omitted, the SDK defaults to the logged-in user unless `GitHubToken` is set.
- `AutoStart`: Optional. Defaults to `true`.
- `AutoRestart`: Optional. Defaults to `true`.

GitHub Copilot jobs require the GitHub Copilot CLI to be installed and authenticated. This runtime keeps Copilot's built-in shell, file, and URL permissions disabled and continues to expose only the selected OneShotPrompt tools for local mutation and inspection.

### `ThinkingLevel`

Allowed values are `low`, `medium`, or `high`.

This is the global default reasoning hint. A job can override it with its own `ThinkingLevel` value.

### `PersistMemory`

When `true`, a job keeps a small rolling memory of prior prompt and response pairs unless the job explicitly overrides it.

Memory files are written next to the active config file under `.oneshotprompt/memory/`.

## Job Settings

Each entry under `Jobs:` supports the following properties:

- `Name`: Required. Must be unique across the file.
- `Prompt`: Required. The task given to the agent.
- `Provider`: Required. Must be `OpenAI`, `Anthropic`, `Gemini`, `OpenAICompatible`, or `GitHubCopilot`.
- `AutoApprove`: Optional. Defaults to `false`.
- `AllowedTools`: Optional comma-separated tool allowlist applied before automatic tool selection.
- `PersistMemory`: Optional job-level override for the root value.
- `ThinkingLevel`: Optional job-level override for the root value.
- `Schedule`: Optional metadata for humans and deployment scripts.
- `Enabled`: Optional. Defaults to `true`.

## Tool Access Model

`AutoApprove` controls whether a job can mutate the local environment.

- `false`: the agent can inspect files and directories only.
- `true`: the agent can also create directories, move files, copy files, delete files, write files, and run built-in process execution tools.

`AllowedTools` is an additional restriction layer. When present, it filters the registered tool catalog before the selector agent runs.

- Use a comma-separated list such as `GetKnownFolder, ListDirectory, ReadTextFile`.
- Unknown tool names are rejected during config validation.
- Mutation tools in `AllowedTools` require `AutoApprove: true`.

Current built-in tools:

- Inspection: `GetKnownFolder`, `ListDirectory`, `ReadTextFile`, `ReadTextFileLines`, `GetTextFileLength`
- File mutation: `CreateDirectory`, `MoveFile`, `MoveFiles`, `CopyFile`, `DeleteFile`, `WriteTextFile`
- Process execution: `RunCommand`, `RunDotNetCommand`

`MoveFiles` accepts pipe-delimited source and destination path strings and moves files concurrently. Prefer it over repeated `MoveFile` calls when organizing many files.

This is the main safety boundary in the current design. Use `false` for planning or audit-style jobs and `true` only for deterministic automation you trust.

## Agent Skills

OneShotPrompt automatically exposes Agent Skills from two locations:

- Bundled skills shipped with the console app.
- A `skills/` directory next to the active config file.

Skills package instructions and references for the agent. They do not replace concrete file I/O or process execution in the current runtime, so local actions still happen through the built-in tools.

Bundled skills include a tool-selection optimizer plus CLI operating guidance for `validate`, `jobs`, `run`, and `interactive`. Before the main execution agent is created, OneShotPrompt runs a selector pass that uses the optimizer skill to choose the smallest relevant tool subset for the job. The execution agent then runs with only that selected subset, which keeps jobs more deterministic when many tools are registered.

The bundled CLI guidance tells agents to prefer explicit subcommands, pass `--config` in scripted contexts, validate before running when config state is uncertain, list jobs before assuming a job name, and treat the active config directory as the runtime root for sibling `skills/`, `logs/`, and `.oneshotprompt/memory/` content.

## Validation Rules

The application rejects a config when any of these are true:

- No jobs are defined.
- Two jobs use the same `Name`.
- A job is missing `Name`.
- A job is missing `Prompt`.
- `Provider` is not one of the supported values.
- `GitHubCopilot.CliUrl` and `GitHubCopilot.CliPath` are both set.
- `GitHubCopilot.CliUrl` is combined with `GitHubCopilot.GitHubToken` or `GitHubCopilot.UseLoggedInUser`.
- `ThinkingLevel` is not `low`, `medium`, or `high`.
- The file contains unsupported YAML sections or properties.

The YAML reader is intentionally minimal. Keep the file simple and close to the structure in [config.example.yaml](../config.example.yaml).

## Example

```yaml
OpenAI:
  ApiKey: ""
  Model: "gpt-5-nano"

Anthropic:
  ApiKey: ""
  Model: "claude-haiku-4-5"

Gemini:
  ApiKey: ""
  Model: "gemini-2.5-flash"

GitHubCopilot:
  Model: "gpt-5"
  CliPath: ""
  CliUrl: ""
  LogLevel: "info"
  GitHubToken: ""
  AutoStart: true
  AutoRestart: true

ThinkingLevel: "low"
PersistMemory: true

Jobs:
  - Name: "downloads-cleanup"
    Prompt: "Organize files in Downloads by type"
    Provider: "OpenAI"
    AutoApprove: true
    AllowedTools: "GetKnownFolder, ListDirectory, MoveFile, MoveFiles, CreateDirectory"
    PersistMemory: false
    ThinkingLevel: "low"
    Schedule: "Daily at midnight"
    Enabled: true
```

## Validate Before Running

Use either of these:

```powershell
dotnet run --project src/OneShotPrompt.Console -- validate --config config.yaml
```

```powershell
./scripts/validate-config.ps1 -ConfigPath ./config.yaml
```
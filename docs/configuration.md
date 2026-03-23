# Configuration Guide

OneShotPrompt reads one YAML file, validates it before any execution, and treats the directory containing that file as the runtime root for related state.

That matters because these paths are resolved relative to the active config file:

- `logs/`
- `skills/`
- `.oneshotprompt/memory/`

Start from [config.example.yaml](../config.example.yaml), keep the structure close to that file, and prefer explicit values over YAML tricks. The parser is intentionally small and rejects unsupported sections or properties.

## Runtime Model

One config file contains:

- Provider settings at the top level.
- Optional global defaults such as `ThinkingLevel` and `PersistMemory`.
- A `Jobs:` collection that defines runnable jobs.

Each job chooses one provider, one prompt, and an execution policy. At runtime, OneShotPrompt narrows the tool catalog first, then runs the execution agent with only the selected subset.

Jobs can optionally switch to `Workflow: "corporate-planning"`. In that mode, the runtime still performs tool selection first, then creates a temporary team of specialist agents on the fly, assigns them subsets of the selected tools, and runs them in a group chat until the final response is complete or the iteration limit is reached.

## Supported Top-Level Sections

These top-level sections are recognized:

- `OpenAI`
- `Anthropic`
- `Gemini`
- `OpenAICompatible`
- `GitHubCopilot`
- `CorporatePlanning`
- `ThinkingLevel`
- `PersistMemory`
- `Jobs`

Any other top-level section fails validation.

## Provider Settings

Only configure the providers you actually use, but any provider referenced by a job must have its required settings populated.

### OpenAI

- `ApiKey`: required for `Provider: "OpenAI"` jobs.
- `Model`: required for `Provider: "OpenAI"` jobs.

Default model in the sample config: `gpt-5-nano`

### Anthropic

- `ApiKey`: required for `Provider: "Anthropic"` jobs.
- `Model`: required for `Provider: "Anthropic"` jobs.

Default model in the sample config: `claude-haiku-4-5`

### Gemini

- `ApiKey`: required for `Provider: "Gemini"` jobs.
- `Model`: required for `Provider: "Gemini"` jobs.

Default model in the sample config: `gemini-2.5-flash`

Gemini runs through `GeminiDotnet.Extensions.AI`, but still follows the same Microsoft Agent Framework execution path as the other chat-client-backed providers.

### OpenAICompatible

- `Endpoint`: required for `Provider: "OpenAICompatible"` jobs.
- `ApiKey`: required for `Provider: "OpenAICompatible"` jobs.
- `Model`: required for `Provider: "OpenAICompatible"` jobs.

This is intended for OpenAI-style local or hosted endpoints such as LM Studio-compatible servers.

### GitHubCopilot

- `Model`: optional default model for `Provider: "GitHubCopilot"` jobs.
- `CliPath`: optional explicit path to the GitHub Copilot CLI executable.
- `CliUrl`: optional URL for an already running Copilot CLI server.
- `LogLevel`: optional Copilot CLI log level. Default: `info`.
- `GitHubToken`: optional explicit token for Copilot CLI authentication.
- `UseLoggedInUser`: optional boolean auth override.
- `AutoStart`: optional. Default: `true`.
- `AutoRestart`: optional. Default: `true`.

Rules enforced by validation:

- `CliUrl` and `CliPath` cannot both be set.
- `CliUrl` cannot be combined with `GitHubToken`.
- `CliUrl` cannot be combined with `UseLoggedInUser`.

GitHub Copilot jobs require the GitHub Copilot CLI to be installed and authenticated. The runtime disables Copilot's built-in shell, file, and URL permissions and keeps local access scoped to OneShotPrompt's selected built-in tools.

### CorporatePlanning

- `MaxAgents`: optional. Maximum number of dynamically generated planning participants. Minimum: `2`. Default: `4`.
- `MaxIterations`: optional. Maximum number of group-chat turns before the workflow stops. Minimum: `1`. Default: `8`.

These settings matter only for jobs that set `Workflow: "corporate-planning"`.

## Global Defaults

### `ThinkingLevel`

Allowed values:

- `low`
- `medium`
- `high`

This value becomes the default reasoning hint for all jobs unless a job overrides it.

### `PersistMemory`

When `true`, jobs keep a compact rolling history of prompt and response pairs unless a job overrides the setting.

Persisted memory lives under `.oneshotprompt/memory/` next to the active config file.

## Job Schema

Each entry under `Jobs:` supports these properties:

- `Name`: required, unique across the file.
- `Prompt`: required.
- `Provider`: required. Must be `OpenAI`, `Anthropic`, `Gemini`, `OpenAICompatible`, or `GitHubCopilot`.
- `Workflow`: optional. Must be `single-agent` or `corporate-planning`. Default: `single-agent`.
- `AutoApprove`: optional. Default: `false`.
- `AllowedTools`: optional tool allowlist.
- `PersistMemory`: optional job-level override for the global setting.
- `ThinkingLevel`: optional job-level override for the global setting.
- `Schedule`: optional human-readable schedule metadata.
- `Enabled`: optional. Default: `true`.

`Schedule` is descriptive only. OneShotPrompt does not schedule jobs itself.

## Workflow Modes

`single-agent` keeps the existing behavior: one selector pass, then one execution agent.

`corporate-planning` inserts a collaborative planning layer that:

- Generates a small specialist team per task.
- Assigns each generated agent an allowed subset of the selected tools.
- Runs the team in a Microsoft Agent Framework group chat.
- Stops when a generated agent emits the final response payload or the configured iteration limit is reached.

## Tool Access Model

`AutoApprove` is the main safety boundary.

- `false`: inspection-only execution.
- `true`: inspection plus mutation and process tools.

`AllowedTools` is an extra restriction layer applied before the selector runs. If it is omitted, the full built-in catalog is eligible. If it is present, only the named tools can be considered.

Accepted formats:

- Comma-separated string: `GetKnownFolder, ListDirectory, ReadTextFile`
- Single-line bracketed list: `[GetKnownFolder, ListDirectory, ReadTextFile]`

Current built-in tools:

- Inspection: `GetKnownFolder`, `ListDirectory`, `ReadTextFile`, `ReadTextFileLines`, `GetTextFileLength`
- File mutation: `CreateDirectory`, `MoveFile`, `MoveFiles`, `CopyFile`, `DeleteFile`, `WriteTextFile`
- Process execution: `RunCommand`, `RunDotNetCommand`

Validation rules for `AllowedTools`:

- Unknown tool names are rejected.
- Duplicate tool names are rejected.
- Mutation tools are rejected when `AutoApprove: false`.

`MoveFiles` is the batch-oriented file mover and is preferred over repeated `MoveFile` calls when a job is organizing many files.

## Skills

OneShotPrompt exposes Agent Skills from two locations:

- Bundled skills shipped with the console app.
- A sibling `skills/` directory next to the active config file.

Skills provide instructions and reference material. They do not replace the built-in filesystem or process tools.

The runtime performs a tool-selection pass before the main execution agent runs. That pass uses the available skill context plus the registered tool catalog to choose the smallest relevant subset for the job.

## Validation Behavior

Validation fails when any of these conditions is true:

- The config file does not define any jobs.
- A job is missing `Name`.
- A job is missing `Prompt`.
- Two jobs use the same `Name`.
- A job references an unsupported provider.
- A provider used by a job is missing required settings.
- `ThinkingLevel` is not one of `low`, `medium`, or `high`.
- `Workflow` is not `single-agent` or `corporate-planning`.
- `AllowedTools` contains duplicates.
- `AllowedTools` contains unknown tool names.
- `AllowedTools` includes mutation tools while `AutoApprove` is `false`.
- `CorporatePlanning.MaxAgents` is less than `2`.
- `CorporatePlanning.MaxIterations` is less than `1`.
- `GitHubCopilot.CliUrl` conflicts with other GitHub Copilot connection settings.
- The YAML file contains unsupported sections or unsupported properties inside known sections.

The parser is strict by design. Keep the file close to [config.example.yaml](../config.example.yaml) rather than relying on broader YAML features.

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

OpenAICompatible:
  Endpoint: "http://localhost:1234/v1"
  ApiKey: "lm-studio"
  Model: "default"

GitHubCopilot:
  Model: "gpt-5"
  CliPath: ""
  CliUrl: ""
  LogLevel: "info"
  GitHubToken: ""
  AutoStart: true
  AutoRestart: true

CorporatePlanning:
  MaxAgents: 4
  MaxIterations: 8

ThinkingLevel: "low"
PersistMemory: true

Jobs:
  - Name: "downloads-cleanup"
    Prompt: "Organize files in Downloads by type"
    Provider: "OpenAI"
    Workflow: "single-agent"
    AutoApprove: true
    AllowedTools: "GetKnownFolder, ListDirectory, MoveFile, MoveFiles, CreateDirectory"
    PersistMemory: false
    ThinkingLevel: "low"
    Schedule: "Daily at midnight"
    Enabled: true

  - Name: "compatible-system-info"
    Prompt: "Gather basic system information for this machine. Use available commands to report the OS, hostname, current user, current working directory, and installed .NET SDK/runtime details. Do not make changes."
    Provider: "OpenAICompatible"
    Workflow: "single-agent"
    AutoApprove: true
    AllowedTools: "RunCommand"
    PersistMemory: false
    ThinkingLevel: "low"
    Enabled: false

  - Name: "repo-build-check"
    Prompt: "Build the repository next to this config file with dotnet and summarize any failures."
    Provider: "Anthropic"
    Workflow: "corporate-planning"
    AutoApprove: true
    AllowedTools: "RunDotNetCommand, ReadTextFile, ListDirectory"
    PersistMemory: false
    ThinkingLevel: "medium"
    Enabled: false

  - Name: "gemini-repo-summary"
    Prompt: "Inspect the repository next to this config file and summarize the main architecture and any obvious risks without making changes."
    Provider: "Gemini"
    Workflow: "single-agent"
    AutoApprove: false
    AllowedTools: "ListDirectory, ReadTextFile, ReadTextFileLines, GetTextFileLength"
    PersistMemory: false
    ThinkingLevel: "medium"
    Enabled: false

  - Name: "copilot-repo-audit"
    Prompt: "Inspect the repository next to this config file and summarize the highest-risk issues without making changes."
    Provider: "GitHubCopilot"
    Workflow: "corporate-planning"
    AutoApprove: false
    AllowedTools: "ListDirectory, ReadTextFile, ReadTextFileLines, GetTextFileLength"
    PersistMemory: false
    ThinkingLevel: "medium"
    Enabled: false
```

## Validate Before Running

```powershell
dotnet run --project src/OneShotPrompt.Console -- validate --config config.yaml
```

```powershell
./scripts/validate-config.ps1 -ConfigPath ./config.yaml
```
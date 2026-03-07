# Configuration Guide

OneShotPrompt reads a single YAML file and validates it before any job runs. Start with `config.yaml.example`, copy it to `config.yaml`, and then fill in only the provider settings you actually use.

## Root Settings

### `OpenAI`

- `ApiKey`: API key used when a job sets `Provider: "OpenAI"`.
- `Model`: Chat model name. The default sample uses `gpt-5-nano`.

### `Anthropic`

- `ApiKey`: API key used when a job sets `Provider: "Anthropic"`.
- `Model`: Model name. The default sample uses `claude-haiku-4-5`.

### `OpenAICompatible`

- `Endpoint`: Base endpoint for an OpenAI-compatible server.
- `ApiKey`: API key or local token for that endpoint.
- `Model`: Model name exposed by that endpoint.

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
- `Provider`: Required. Must be `OpenAI`, `Anthropic`, or `OpenAICompatible`.
- `AutoApprove`: Optional. Defaults to `false`.
- `PersistMemory`: Optional job-level override for the root value.
- `ThinkingLevel`: Optional job-level override for the root value.
- `Schedule`: Optional metadata for humans and deployment scripts.
- `Enabled`: Optional. Defaults to `true`.

## Tool Access Model

`AutoApprove` controls whether a job can mutate the filesystem.

- `false`: the agent can inspect files and directories only.
- `true`: the agent can also create directories, move files, copy files, delete files, and write files.

This is the main safety boundary in the current design. Use `false` for planning or audit-style jobs and `true` only for deterministic automation you trust.

## Validation Rules

The application rejects a config when any of these are true:

- No jobs are defined.
- Two jobs use the same `Name`.
- A job is missing `Name`.
- A job is missing `Prompt`.
- `Provider` is not one of the supported values.
- `ThinkingLevel` is not `low`, `medium`, or `high`.
- The file contains unsupported YAML sections or properties.

The YAML reader is intentionally minimal. Keep the file simple and close to the structure in [config.yaml.example](../config.yaml.example).

## Example

```yaml
OpenAI:
  ApiKey: ""
  Model: "gpt-5-nano"

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

## Validate Before Running

Use either of these:

```powershell
dotnet run --project src/OneShotPrompt.Console -- validate --config config.yaml
```

```powershell
./scripts/validate-config.ps1 -ConfigPath ./config.yaml
```
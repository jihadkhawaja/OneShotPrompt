# OneShotPrompt 1.0

The 1.0 release establishes the core OneShotPrompt workflow: define AI jobs in YAML, validate them, and run them from a small .NET CLI with constrained local tool access.

## Highlights

- YAML-driven job execution from `config.yaml`
- Support for OpenAI, Anthropic, and OpenAI-compatible providers
- Automatic tool-selection pass before execution
- Optional per-job memory persistence next to the active config
- Native AOT publishing support for Windows, Linux, and macOS
- Helper scripts and documentation for validation, execution, publishing, and OS-level scheduling

## Added

- CLI commands for `validate`, `jobs`, and `run`
- Config validation for required fields, duplicate job names, supported providers, and allowed thinking levels
- Job-level controls for `AutoApprove`, `AllowedTools`, `PersistMemory`, `ThinkingLevel`, `Schedule`, and `Enabled`
- Built-in safety boundary based on `AutoApprove`
- Bundled Agent Skills plus support for custom skills in a sibling `skills/` directory
- Per-job memory storage under `.oneshotprompt/memory/`
- Native AOT publish flow and release packaging support

## Safety Model

- `AutoApprove: false` exposes read-only filesystem inspection tools
- `AutoApprove: true` enables filesystem mutation tools such as create, move, copy, delete, and write
- `AllowedTools` can further restrict the tool catalog before the execution agent runs

## Supported Runtime Targets

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`

## Operational Notes

- Running without arguments defaults to `run --config config.yaml`
- Scheduling is intentionally delegated to the operating system
- Windows scheduling is supported through Task Scheduler guidance and helper scripts
- Linux scheduling is documented for `cron` and `systemd`

## Summary

Version 1.0 delivers the first stable foundation for config-driven, one-shot AI automation with a constrained tool model, multi-provider support, memory persistence, and deployment-ready Native AOT publishing.
---
name: oneshotprompt-cli
description: Operate the OneShotPrompt CLI predictably for validation, job discovery, targeted execution, and publishing. Use when a task is specifically about this project's command surface, config-driven runs, or the published console binary.
metadata:
  author: oneshotprompt
  version: "1.0"
---

Use this skill when the task is about interacting with OneShotPrompt itself rather than an arbitrary external CLI.

Workflow:
1. Identify whether the goal is help, config validation, job listing, running all jobs, running one job, opening the interactive menu, or publishing the binary.
2. Prefer `RunDotNetCommand` when operating from a source checkout. Use `RunCommand` only when the task is explicitly about a published executable.
3. Pass the subcommand and arguments explicitly. Do not rely on shell syntax, redirection, or implicit defaults that depend on terminal interactivity.
4. In automation or scripted contexts, pass `--config <path>` explicitly instead of relying on the default `config.yaml`.
5. Use `validate --config <path>` before `run` when the config may be new or recently edited.
6. Use `jobs --config <path>` before `run --job <name>` when the exact job name is not already confirmed.
7. Use `run --config <path> --job <name>` for a targeted run and `run --config <path>` for all enabled jobs.
8. Use `interactive` or `-i` only when the task explicitly calls for menu-driven or ad-hoc prompting behavior.
9. Summarize the actual command outcome, including any validation errors, selected job, logs path, or publish output path.

CLI contract:
- Supported commands are `run`, `validate`, `jobs`, `interactive`, and `help`.
- `--job` is only valid with `run`.
- With no arguments, the app defaults to `run --config config.yaml`, but interactive terminals may open the menu instead. Agents should prefer explicit subcommands.
- `logs/`, sibling `skills/`, and `.oneshotprompt/memory/` are all resolved relative to the active config file location.
- Native AOT publish output is written under `src/OneShotPrompt.Console/bin/Release/net10.0/<rid>/publish/`.

Guardrails:
- Do not invent unsupported flags or subcommands.
- Do not claim a job ran, a config validated, or a publish succeeded unless the command output confirms it.
- Prefer the dedicated filesystem skills for simple reads or writes around the CLI instead of wrapping everything in command execution.
- If the task depends on interactive menu choices, say that human input is required instead of pretending the menu was completed automatically.

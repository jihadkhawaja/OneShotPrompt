---
name: oneshotprompt-cli
description: Repository guidance for agents that need to communicate with the OneShotPrompt CLI, validate configs, list jobs, run specific jobs, or publish the console app.
argument-hint: What OneShotPrompt CLI action, config path, or job should be operated on?
user-invocable: false
metadata:
  author: oneshotprompt
  version: "1.0"
---

Use this skill when working inside the OneShotPrompt repository and the task involves the product CLI, scripts, docs, tests, or examples that exercise that CLI.

Primary commands:
1. `dotnet restore OneShotPrompt.slnx`
2. `dotnet build OneShotPrompt.slnx`
3. `dotnet test tests/OneShotPrompt.Tests/OneShotPrompt.Tests.csproj`
4. `dotnet run --project src/OneShotPrompt.Console -- validate --config <path>`
5. `dotnet run --project src/OneShotPrompt.Console -- jobs --config <path>`
6. `dotnet run --project src/OneShotPrompt.Console -- run --config <path>`
7. `dotnet run --project src/OneShotPrompt.Console -- run --config <path> --job <name>`
8. `dotnet run --project src/OneShotPrompt.Console -- listen --config <path> --job <name>`
9. `dotnet run --project src/OneShotPrompt.Console -- interactive`

Workflow:
1. Prefer explicit CLI commands over the no-argument launch path when writing automation, docs, tests, or examples.
2. Treat the config file directory as the runtime root for sibling `skills/`, `logs/`, and `.oneshotprompt/memory/` content.
3. Validate a config before assuming a job can be listed or run.
4. List jobs before referencing a job name that was not already established.
5. When documenting or testing single-job execution, use `run --config <path> --job <name>`.
6. When documenting the WhatsApp personal channel flow, use `listen --config <path> --job <name>` if the intent is continuous event-driven replies rather than a single pass.
7. When describing interactive behavior, distinguish between explicit `interactive` and the no-argument behavior that depends on whether the terminal is interactive.
8. For build or test validation in this repo, use the solution and test commands above rather than ad hoc project combinations unless the task is intentionally scoped to one project.
9. If jobs or config examples mention execution behavior, account for both `Workflow: "single-agent"` and `Workflow: "corporate-planning"` where relevant.
10. If you change CLI behavior, command examples, scheduling flows, or bundled skills, update `README.md` and the relevant `docs/` pages in the same change.

Guardrails:
- Do not document or script unsupported subcommands or flags.
- Do not treat `--job` as valid for `validate` or `jobs`.
- Do not describe `listen` as a general scheduler; it is a targeted bridge-driven listener mode.
- Do not describe logs or memory as repo-root relative unless the active config file is at the repo root.
- Do not describe every run as a single execution agent when a job can opt into the corporate-planning workflow.
- Do not rely on shell-specific syntax in examples unless the task is explicitly shell-specific.

# Use Cases

This page walks through concrete scenarios where OneShotPrompt fits well, along with minimal config snippets and notes on choosing the right tools and workflow for each case.

For config semantics, see [configuration.md](./configuration.md). For command syntax, see [cli-reference.md](./cli-reference.md).

---

## 1. Downloads Folder Cleanup

**Goal:** Sort files in a cluttered Downloads folder into subfolders by type (images, documents, archives, and so on) on a schedule without manual effort.

**Why OneShotPrompt:** The task is well-bounded, repeatable, and benefits from an LLM deciding which files belong where rather than a hard-coded script with fixed extensions.

**Config:**

```yaml
OpenAI:
  ApiKey: "sk-..."
  Model: "gpt-5-nano"

ThinkingLevel: "low"
PersistMemory: false

Jobs:
  - Name: "downloads-cleanup"
    Prompt: "Organize files in the Downloads folder by type. Create subfolders for Images, Documents, Archives, and Other. Move each file into the correct subfolder."
    Provider: "OpenAI"
    Workflow: "single-agent"
    AutoApprove: true
    AllowedTools: "GetKnownFolder, ListDirectory, MoveFile, MoveFiles, CreateDirectory"
    ThinkingLevel: "low"
    Schedule: "Daily at midnight"
    Enabled: true
```

**Notes:**

- `GetKnownFolder` lets the agent resolve the real Downloads path without hard-coding it.
- `MoveFiles` is preferred over `MoveFile` because it moves a batch concurrently.
- `AutoApprove: true` is required here because the job moves files.
- Wire the job to Windows Task Scheduler or cron using the published binary. See [windows-task-scheduler.md](./windows-task-scheduler.md) or [linux-scheduling.md](./linux-scheduling.md).

---

## 2. Repository Architecture Summary

**Goal:** Get a written summary of a codebase's layout, main components, and any obvious structural risks without making changes.

**Why OneShotPrompt:** A read-only inspection job can be run safely on any repository because it cannot write or execute anything. The job is useful before onboarding to an unfamiliar codebase or before a code review.

**Config:**

```yaml
Gemini:
  ApiKey: "AI..."
  Model: "gemini-2.5-flash"

ThinkingLevel: "medium"
PersistMemory: false

Jobs:
  - Name: "repo-summary"
    Prompt: "Inspect the repository next to this config file and summarize the main architecture, key components, and any obvious structural risks. Do not make any changes."
    Provider: "Gemini"
    Workflow: "single-agent"
    AutoApprove: false
    AllowedTools: "ListDirectory, ReadTextFile, ReadTextFileLines, GetTextFileLength"
    ThinkingLevel: "medium"
    Enabled: true
```

**Notes:**

- `AutoApprove: false` is correct here. Read-only inspection tools are available without it.
- `ReadTextFileLines` is useful for large files because the agent can page through them without loading the whole file into context.
- `GetTextFileLength` lets the agent decide whether to read a file in full or in chunks.
- Increase `ThinkingLevel` to `high` if the codebase is large and you want more thorough reasoning.

---

## 3. .NET Build Check

**Goal:** Run `dotnet build` against a repository and get a human-readable summary of any failures.

**Why OneShotPrompt:** Wrapping a build command in an LLM job lets the agent read relevant source and project files to produce a more useful diagnosis than raw compiler output.

**Config:**

```yaml
Anthropic:
  ApiKey: "sk-ant-..."
  Model: "claude-haiku-4-5"

ThinkingLevel: "medium"
PersistMemory: false

CorporatePlanning:
  MaxAgents: 3
  MaxIterations: 6

Jobs:
  - Name: "repo-build-check"
    Prompt: "Build the repository next to this config file with dotnet and summarize any failures. Include the affected project, the error message, and a suggested fix for each failure."
    Provider: "Anthropic"
    Workflow: "corporate-planning"
    AutoApprove: true
    AllowedTools: "RunDotNetCommand, ReadTextFile, ListDirectory"
    ThinkingLevel: "medium"
    Enabled: true
```

**Notes:**

- `corporate-planning` is useful here because multiple specialist agents can independently review the build output, read failing files, and produce a consolidated diagnosis.
- `RunDotNetCommand` runs `dotnet` commands inside the sandbox. It does not allow arbitrary shell commands.
- If you only want the raw build output without LLM-assisted analysis, switch to `single-agent`.

---

## 4. System Information Snapshot

**Goal:** Collect basic machine details (OS, hostname, current user, installed .NET SDK versions) and write a summary to the console.

**Why OneShotPrompt:** Useful for inventory or diagnostics scripts that need to run across machines with different environments and report findings in a consistent format.

**Config:**

```yaml
OpenAICompatible:
  Endpoint: "http://localhost:1234/v1"
  ApiKey: "lm-studio"
  Model: "default"

ThinkingLevel: "low"
PersistMemory: false

Jobs:
  - Name: "system-info"
    Prompt: "Gather basic system information for this machine. Report the OS, hostname, current user, current working directory, and installed .NET SDK and runtime versions. Do not make changes."
    Provider: "OpenAICompatible"
    Workflow: "single-agent"
    AutoApprove: true
    AllowedTools: "RunCommand"
    ThinkingLevel: "low"
    Enabled: true
```

**Notes:**

- This example uses a locally hosted OpenAI-compatible model such as LM Studio, making it suitable for air-gapped or offline environments.
- `RunCommand` grants general shell access. Use it only for read-only commands and keep the prompt explicit about not making changes.
- For tighter control, replace `RunCommand` with `RunDotNetCommand` if only .NET commands are needed.

---

## 5. Log File Analysis

**Goal:** Read a log file and produce a concise summary of errors, warnings, or anomalies.

**Why OneShotPrompt:** LLMs are well-suited for free-text log parsing where patterns are inconsistent or undocumented. The job runs on demand or on a schedule after log rotation.

**Config:**

```yaml
OpenAI:
  ApiKey: "sk-..."
  Model: "gpt-5-nano"

ThinkingLevel: "medium"
PersistMemory: false

Jobs:
  - Name: "log-analysis"
    Prompt: "Read the file logs/app.log next to this config file. Summarize all ERROR and WARNING entries. Group them by category if possible and note any recurring patterns. Do not make changes."
    Provider: "OpenAI"
    Workflow: "single-agent"
    AutoApprove: false
    AllowedTools: "ReadTextFile, ReadTextFileLines, GetTextFileLength"
    ThinkingLevel: "medium"
    Enabled: true
```

**Notes:**

- `ReadTextFileLines` is important for large log files. It lets the agent read the file in pages without loading the entire contents into one request.
- `GetTextFileLength` helps the agent decide whether the file needs paging.
- `AutoApprove: false` is safe here because no mutation tools are included.
- Adjust the prompt path to match your actual log location relative to the config file.

---

## 6. Markdown Documentation Review

**Goal:** Review a set of Markdown files for completeness, consistency, and formatting issues and produce a written report.

**Why OneShotPrompt:** Reviewing documentation by hand across many files is tedious. A read-only LLM job can scan all the files and flag gaps or inconsistencies in one pass.

**Config:**

```yaml
Anthropic:
  ApiKey: "sk-ant-..."
  Model: "claude-haiku-4-5"

ThinkingLevel: "medium"
PersistMemory: false

Jobs:
  - Name: "docs-review"
    Prompt: "Read all Markdown files in the docs/ directory next to this config file. Identify any sections that are missing, inconsistent between files, or lack examples. Produce a concise written report. Do not make changes."
    Provider: "Anthropic"
    Workflow: "single-agent"
    AutoApprove: false
    AllowedTools: "ListDirectory, ReadTextFile, ReadTextFileLines, GetTextFileLength"
    ThinkingLevel: "medium"
    Enabled: true
```

**Notes:**

- This is a read-only job. `AutoApprove: false` is correct.
- If you want the agent to also apply fixes, add `WriteTextFile` to `AllowedTools` and set `AutoApprove: true`. Review the changes carefully before committing them.
- For a deeper review that benefits from parallel specialist opinions, switch to `Workflow: "corporate-planning"`.

---

## 7. Source File Audit With Corporate Planning

**Goal:** Audit a source directory for common code quality issues and produce a ranked list of findings. Use multiple specialist agents to cover different quality dimensions in parallel.

**Why OneShotPrompt:** `corporate-planning` generates a small team of specialists on the fly, assigns each one a subset of the available tools, and runs them in a group chat. Each specialist can focus on a different dimension (naming, structure, patterns) and the results are consolidated before the final response is returned.

**Config:**

```yaml
Anthropic:
  ApiKey: "sk-ant-..."
  Model: "claude-haiku-4-5"

CorporatePlanning:
  MaxAgents: 4
  MaxIterations: 8

ThinkingLevel: "high"
PersistMemory: false

Jobs:
  - Name: "source-audit"
    Prompt: "Audit the source files in the src/ directory next to this config file. Identify naming inconsistencies, large files, missing error handling, and any obvious structural issues. Produce a ranked list of findings from most to least impactful. Do not make changes."
    Provider: "Anthropic"
    Workflow: "corporate-planning"
    AutoApprove: false
    AllowedTools: "ListDirectory, ReadTextFile, ReadTextFileLines, GetTextFileLength"
    ThinkingLevel: "high"
    Enabled: true
```

**Notes:**

- `MaxAgents: 4` and `MaxIterations: 8` are reasonable starting points. Increase `MaxAgents` if you want more perspectives; increase `MaxIterations` if the group chat needs more turns to converge.
- `ThinkingLevel: "high"` is appropriate here because the job spans many files and needs careful reasoning.
- The group chat is not visible in non-interactive terminals. Run in interactive mode to see the live planning discussion.

---

## 8. Scheduled Repository Backup Verification

**Goal:** Periodically verify that a backup directory contains recent files and report any gaps.

**Why OneShotPrompt:** A scheduled read-only inspection job can confirm that automated backup processes ran and flag missing or stale files without requiring a custom script.

**Config:**

```yaml
OpenAI:
  ApiKey: "sk-..."
  Model: "gpt-5-nano"

ThinkingLevel: "low"
PersistMemory: true

Jobs:
  - Name: "backup-check"
    Prompt: "List the contents of the backups/ directory next to this config file. Check whether any files were created or modified in the last 24 hours. If nothing recent is present, clearly report that the backup appears to be missing or stale."
    Provider: "OpenAI"
    Workflow: "single-agent"
    AutoApprove: false
    AllowedTools: "ListDirectory, ReadTextFile"
    ThinkingLevel: "low"
    Schedule: "Daily at 08:00"
    Enabled: true
```

**Notes:**

- `PersistMemory: true` lets the job retain a short history of previous check results. This allows the agent to notice trends such as backups being consistently absent on certain days.
- `Schedule` is descriptive only. Wire the job to your OS scheduler to run it on the indicated cadence.
- Adjust the prompt path to match your actual backup directory.

---

## Choosing the Right Workflow

| Scenario | Recommended workflow | Notes |
|---|---|---|
| Simple, single-step task | `single-agent` | Lowest latency; one execution agent |
| Multi-faceted analysis | `corporate-planning` | Multiple specialists; more thorough but slower |
| Scheduled file management | `single-agent` | Predictable; easy to monitor in logs |
| Deep code or document audit | `corporate-planning` | Benefits from parallel specialist perspectives |

## Choosing the Right Safety Level

| Task type | `AutoApprove` | Recommended `AllowedTools` |
|---|---|---|
| Read-only inspection | `false` | Inspection tools only |
| File organization | `true` | `GetKnownFolder`, `ListDirectory`, `MoveFiles`, `CreateDirectory` |
| Build and test | `true` | `RunDotNetCommand`, `ReadTextFile`, `ListDirectory` |
| General shell access | `true` | `RunCommand` (use sparingly) |
| Write or edit files | `true` | `WriteTextFile`, `ReadTextFile`, `ListDirectory` |

Never add mutation tools to a job with `AutoApprove: false`. Validation will reject that combination.

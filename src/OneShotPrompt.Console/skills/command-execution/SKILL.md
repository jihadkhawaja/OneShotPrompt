---
name: command-execution
description: Run installed executables directly for cross-platform automation. Use when a task needs CLI-based work, scripted workflows, repository commands, or tool-driven operations that are broader than simple file reads and writes.
metadata:
  author: oneshotprompt
  version: "1.0"
---

Use this skill when the task is best handled by invoking an existing executable instead of chaining many narrow file operations.

Workflow:
1. Identify the exact executable needed and confirm it is the narrowest reasonable choice.
2. Prefer RunDotNetCommand for .NET and C# tasks. Use RunCommand for other installed executables.
3. Set the working directory explicitly when relative paths, repository files, or config-adjacent assets matter.
4. Pass the executable and arguments directly. Do not rely on shell syntax.
5. Review exit code, standard output, and standard error before deciding on the next step.
6. Summarize what the command actually did instead of inferring side effects.

Guardrails:
- These tools do not run through a shell. Do not use pipes, redirection, command substitution, shell built-ins, or &&.
- Prefer the dedicated filesystem tools when a simple read, write, copy, move, or delete is enough.
- Prefer RunDotNetCommand over RunCommand when the task is centered on dotnet, C#, or this repository's build and run scripts.
- Do not run destructive commands unless the task clearly requires them.
- Never claim success when the exit code or tool output says otherwise.
---
name: file-system-mutations
description: Plan and perform minimal local filesystem changes after inspection. Use when a task requires creating folders, moving or copying files, deleting files, or writing text files.
metadata:
  author: oneshotprompt
  version: "1.0"
---

Use this skill only after the relevant directory state is known.

Workflow:
1. Inspect the source and destination locations first.
2. Choose the smallest deterministic set of changes that satisfies the task.
3. Prefer create, move, or copy over delete when either approach would work.
4. Prefer dedicated file tools over command execution when the task is a straightforward file operation.
5. If mutation tools are unavailable, return a precise action plan instead of simulating changes.
6. After each mutation, rely on the tool result rather than assumptions.
7. Finish with a concise summary of what changed.

Guardrails:
- Do not overwrite existing files unless replacement is clearly required.
- Keep writes scoped to the specific files the task asks for.
- Never claim a mutation succeeded unless the corresponding tool call succeeded.
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
3. When moving multiple files, prefer MoveFiles (batch) over repeated MoveFile calls for faster execution.
4. Prefer create, move, or copy over delete when either approach would work.
5. Prefer dedicated file tools over command execution when the task is a straightforward file operation.
6. If mutation tools are unavailable, return a precise action plan instead of simulating changes.
7. After each mutation, rely on the tool result rather than assumptions.
8. Finish with a concise summary of what changed.

Guardrails:
- Do not overwrite existing files unless replacement is clearly required.
- Keep writes scoped to the specific files the task asks for.
- Never claim a mutation succeeded unless the corresponding tool call succeeded.
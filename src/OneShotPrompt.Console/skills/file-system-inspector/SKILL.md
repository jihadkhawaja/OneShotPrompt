---
name: file-system-inspector
description: Inspect and understand local files and directories before planning or making changes. Use when a task mentions files, folders, paths, Downloads, Documents, Desktop, reading text files, or checking current filesystem state.
metadata:
  author: oneshotprompt
  version: "1.0"
---

Use this skill whenever a job needs to understand local files or directories.

Workflow:
1. Resolve well-known folders before guessing absolute paths.
2. List the relevant directory before reading or changing anything inside it.
3. Read only the text files needed to decide on the next step.
4. Summarize the current state briefly before proposing or applying changes.

Guidelines:
- Prefer the least invasive inspection that answers the task.
- If a path does not exist, report that clearly instead of inventing alternatives.
- For binary or unsupported files, report the limitation instead of pretending to inspect them.
- Never claim a filesystem change occurred from inspection alone.
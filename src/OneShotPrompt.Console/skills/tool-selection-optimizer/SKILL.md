---
name: tool-selection-optimizer
description: Triage large tool inventories and narrow them to the smallest relevant subset before making tool calls. Use when many tools are available, when multiple tools look plausible, or when the task does not obviously map to a single tool family.
metadata:
  author: oneshotprompt
  version: "1.0"
---

Use this skill whenever tool choice is ambiguous or the available tool catalog is large.

Workflow:
1. Restate the task in one sentence.
2. Identify the minimum capability needed for the next step, such as inspect, search, write, move, or delete.
3. Select only the few tools that can perform that next step safely.
4. Ignore unrelated tools until evidence shows the shortlist is insufficient.
5. Reassess after each result and expand the shortlist only when blocked.
6. Prefer domain skills before direct tool usage when a bundled skill already matches the task.

Heuristics:
- Prefer zero tools when the answer can be produced from the prompt, memory, or loaded skills alone.
- Prefer read-only tools before mutation or destructive tools.
- If two tools overlap, choose the narrower or safer tool first.
- Prefer dedicated filesystem tools over command execution when simple file inspection or file mutation is enough.
- Prefer `RunDotNetCommand` over `RunCommand` for .NET and C# work.
- Keep the active shortlist to at most 3 tools at a time unless the task clearly requires more.
- Do not compare many similar tools by trial and error.

Guardrails:
- Do not make speculative or exploratory tool calls outside the shortlist.
- Do not use mutation tools for discovery when an inspection tool can answer the question.
- Do not claim a tool was necessary unless its result materially changed the plan or outcome.
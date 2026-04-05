---
name: whatsapp-personal-channel
description: Operate the local unofficial WhatsApp personal channel bridge for a personal account, limited to the allowlisted phone number or numbers in the bridge config.
metadata:
  author: oneshotprompt
  version: "1.0"
---

Use this skill when the task is specifically about reading WhatsApp messages, replying to them, or carrying out a clear local machine action requested through the local bridge in `tools/whatsapp-personal-channel`.

This skill is for a single reply pass after a trigger has already fired. If the operator wants continuous waiting for new messages, the runtime should use the external `listen` command instead of implementing a loop inside the agent.

Workflow:
1. Verify the bridge is running with `RunCommand` using `fileName=node` and `arguments=tools/whatsapp-personal-channel/channel.mjs health`.
2. Use the current trigger details from the task prompt when present, then read unread inbound messages with `RunCommand` using `fileName=node` and `arguments=tools/whatsapp-personal-channel/channel.mjs list-unread --limit 10` to confirm the sender and message body.
3. If more context is needed, read recent history for that sender with `RunCommand` using `fileName=node` and `arguments=tools/whatsapp-personal-channel/channel.mjs list-recent --phone <digits> --limit 10`.
4. If the inbound message requests a clear and safe local machine action, load the `command-execution` skill and use the narrowest direct command needed to perform it.
5. After the action succeeds, send exactly one concise confirmation to that same sender with `RunCommand` using `fileName=node` and `arguments=tools/whatsapp-personal-channel/channel.mjs send --phone <digits> --text "..."`.
6. If the request is ambiguous, risky, or the local command fails, send exactly one concise clarification or failure message to that same sender with `--phone <digits>`.
7. Summarize what actually happened after the command sequence: no unread messages, waiting for bridge readiness, a local action that was completed, or the reply that was sent.

Guardrails:
- Do not attempt to message any number outside the bridge allowlist.
- The bridge already enforces `allowedPhoneNumbers`; treat any allowlist error as a hard stop.
- If the bridge is not ready, report that status instead of guessing.
- Always pass `--phone <digits>` when using `send` or `list-recent` so the reply is pinned to the correct sender.
- Do not send more than one reply in a single run unless the task explicitly says to do so.
- Prefer short, direct replies because this runs against a personal WhatsApp account.
- Only execute local actions when the request is explicit and the command is safe to run with the tools already selected for the job.
- For destructive, risky, credentialed, or unclear requests, ask for confirmation instead of acting.
- Never claim a local action succeeded unless the exit code and command output confirm it.
- If `list-unread` returns no messages but the current trigger clearly identifies the sender and request, you may still use that trigger context for this run.
- Do not build your own polling or waiting loop inside the agent; let the CLI `listen` mode handle continuous waiting.

Command behavior:
- `health` returns JSON describing readiness and the current linked account name.
- `list-unread` returns JSON with unread inbound messages only from allowlisted numbers.
- `list-recent` returns recent messages for the allowlisted number. Pass `--phone <digits>` to target the intended sender.
- `send` sends a message only to an allowlisted number. Pass `--phone <digits>` so the reply always goes back to the intended sender.
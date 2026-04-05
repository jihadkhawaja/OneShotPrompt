---
name: whatsapp-personal-channel
description: Operate the local unofficial WhatsApp personal channel bridge for a personal account, limited to the allowlisted phone number or numbers in the bridge config.
metadata:
  author: oneshotprompt
  version: "1.0"
---

Use this skill when the task is specifically about reading or replying to WhatsApp messages through the local bridge in `tools/whatsapp-personal-channel`.

This skill is for a single reply pass after a trigger has already fired. If the operator wants continuous waiting for new messages, the runtime should use the external `listen` command instead of implementing a loop inside the agent.

Workflow:
1. Verify the bridge is running with `RunCommand` using `fileName=node` and `arguments=tools/whatsapp-personal-channel/channel.mjs health`.
2. Read unread inbound messages with `RunCommand` using `fileName=node` and `arguments=tools/whatsapp-personal-channel/channel.mjs list-unread --limit 10`.
3. If more context is needed before replying, read recent history with `RunCommand` using `fileName=node` and `arguments=tools/whatsapp-personal-channel/channel.mjs list-recent --limit 10`.
4. If a reply is clearly warranted, send exactly one concise reply with `RunCommand` using `fileName=node` and `arguments=tools/whatsapp-personal-channel/channel.mjs send --text "..."`.
5. Summarize what happened after the command sequence: no unread messages, waiting for bridge readiness, or the reply that was sent.

Guardrails:
- Do not attempt to message any number outside the bridge allowlist.
- The bridge already enforces `allowedPhoneNumbers`; treat any allowlist error as a hard stop.
- If the bridge is not ready, report that status instead of guessing.
- Do not send more than one reply in a single run unless the task explicitly says to do so.
- Prefer short, direct replies because this runs against a personal WhatsApp account.
- Do not build your own polling or waiting loop inside the agent; let the CLI `listen` mode handle continuous waiting.

Command behavior:
- `health` returns JSON describing readiness and the current linked account name.
- `list-unread` returns JSON with unread inbound messages only from allowlisted numbers.
- `list-recent` returns recent messages for the allowlisted number. If more than one number is allowlisted, pass `--phone <digits>`.
- `send` sends a message only to an allowlisted number. If only one number is allowlisted, `--phone` can be omitted.
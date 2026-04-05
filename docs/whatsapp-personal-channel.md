# WhatsApp Personal Channel

This workspace now includes a local unofficial WhatsApp bridge that works with a personal WhatsApp account through WhatsApp Web.

It is designed for one narrow use case: let OneShotPrompt read unread messages and reply only to the phone number or numbers you explicitly allow in the bridge config.

## Files

- `tools/whatsapp-personal-channel/channel.mjs`: local bridge server and CLI client
- `tools/whatsapp-personal-channel/channel.config.json`: local allowlist and bridge settings
- `skills/whatsapp-personal-channel/SKILL.md`: instructions that teach OneShotPrompt how to use the bridge
- `config.yaml` or another config file with a `personal-whatsapp-reply` job

## Setup

1. Edit `tools/whatsapp-personal-channel/channel.config.json` and replace the placeholder `allowedPhoneNumbers` entry with the full international number you want to reply to.
2. Keep `browserExecutablePath` pointed at a local Chrome or Edge install. The default local file is already set to Chrome on this machine.
3. Leave `sessionDataPath` unset unless you need to override it. On Windows the bridge now stores the WhatsApp browser profile under `%LOCALAPPDATA%\OneShotPrompt\whatsapp-personal-channel\.wwebjs_auth` by default so it does not live inside a OneDrive-synced repo.
4. Install the bridge dependencies:

   ```powershell
   cd tools/whatsapp-personal-channel
   $env:PUPPETEER_SKIP_DOWNLOAD = "1"
   $env:PUPPETEER_CHROME_SKIP_DOWNLOAD = "1"
   npm install
   ```

5. Start the bridge in a separate terminal:

   ```powershell
   pwsh ./scripts/start-whatsapp-personal-channel.ps1
   ```

6. Scan the QR code in that terminal with your personal WhatsApp account.
7. When the bridge reports that it is ready, either run the job once:

   ```powershell
   dotnet run --project src/OneShotPrompt.Console -- run --config config.yaml --job personal-whatsapp-reply
   ```

   Or keep it listening for new inbound messages:

   ```powershell
   dotnet run --project src/OneShotPrompt.Console -- listen --config config.yaml --job personal-whatsapp-reply
   ```

## Bridge Commands

These commands all run from the repository root.

```powershell
node tools/whatsapp-personal-channel/channel.mjs doctor
node tools/whatsapp-personal-channel/channel.mjs health
node tools/whatsapp-personal-channel/channel.mjs list-unread --limit 10
node tools/whatsapp-personal-channel/channel.mjs list-recent --limit 10
node tools/whatsapp-personal-channel/channel.mjs wait-next-message --timeout-seconds 300
node tools/whatsapp-personal-channel/channel.mjs send --text "Hello"
```

If you allowlist more than one number, pass `--phone <digits>` to `list-recent` and `send`.

## Notes

- This is not an official WhatsApp API integration.
- Keep the bridge bound to `127.0.0.1`. Do not expose it publicly.
- The bridge uses a local Chrome or Edge executable instead of downloading a Puppeteer-managed browser.
- On Windows, the QR session files are kept under `%LOCALAPPDATA%\OneShotPrompt\whatsapp-personal-channel\.wwebjs_auth` by default because Chromium/WhatsApp Web databases can break when the browser profile lives under a OneDrive-synced repo folder.
- `sessionDataPath` can be set in `channel.config.json` if you need a custom absolute path for the WhatsApp browser profile.
- The `listen` command reruns the selected job whenever the bridge receives a new inbound allowlisted message.
- If `listen` reports that `/events/next` returned HTML or non-JSON, the running bridge process is stale or the port is occupied by something else. Restart the bridge process before retrying.
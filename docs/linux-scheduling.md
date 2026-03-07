# Linux Scheduling

OneShotPrompt does not include a built-in scheduler. On Linux, the usual execution model is a published binary plus either `cron` or a `systemd` timer.

## Recommended Flow

1. Validate the config.
2. Publish the app for the target runtime.
3. Copy the published output to a stable location.
4. Run the published binary manually once.
5. Register either a `cron` entry or a `systemd` timer.

## Publish

From the repository root:

```powershell
./scripts/publish-aot.ps1 -RuntimeIdentifier linux-x64
```

Or directly with `dotnet`:

```bash
dotnet publish src/OneShotPrompt.Console/OneShotPrompt.Console.csproj -c Release -r linux-x64
```

Typical output path:

```text
src/OneShotPrompt.Console/bin/Release/net10.0/linux-x64/publish/OneShotPrompt.Console
```

## Install Layout

One workable layout is:

```text
/opt/oneshotprompt/OneShotPrompt.Console
/etc/oneshotprompt/config.yaml
```

Because memory is stored relative to the active config file, runtime memory for that layout will end up under:

```text
/etc/oneshotprompt/.oneshotprompt/memory/
```

If you do not want runtime state under `/etc`, place the config in a writable application data directory instead and point the scheduler at that path.

## Cron

Use `cron` when you only need a simple time-based trigger.

Example daily midnight run:

```cron
0 0 * * * /opt/oneshotprompt/OneShotPrompt.Console run --config /etc/oneshotprompt/config.yaml --job downloads-cleanup >> /var/log/oneshotprompt.log 2>&1
```

Notes:

- Use absolute paths for both the executable and the config file.
- Redirect output to a log file because cron does not provide an interactive console.
- Make sure the user running the cron entry can read the config file and write wherever the job needs access.

## systemd Service And Timer

Use a `systemd` timer when you want stronger service management, centralized logs, and easier inspection.

### Service Unit

Create `/etc/systemd/system/oneshotprompt-downloads-cleanup.service`:

```ini
[Unit]
Description=OneShotPrompt job: downloads-cleanup

[Service]
Type=oneshot
WorkingDirectory=/opt/oneshotprompt
ExecStart=/opt/oneshotprompt/OneShotPrompt.Console run --config /etc/oneshotprompt/config.yaml --job downloads-cleanup
User=oneshotprompt
Group=oneshotprompt
```

### Timer Unit

Create `/etc/systemd/system/oneshotprompt-downloads-cleanup.timer`:

```ini
[Unit]
Description=Run OneShotPrompt downloads-cleanup daily

[Timer]
OnCalendar=*-*-* 00:00:00
Persistent=true

[Install]
WantedBy=timers.target
```

### Enable The Timer

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now oneshotprompt-downloads-cleanup.timer
```

### Inspect Status And Logs

```bash
systemctl status oneshotprompt-downloads-cleanup.timer
systemctl list-timers --all | grep oneshotprompt
journalctl -u oneshotprompt-downloads-cleanup.service
```

## Choosing Between Cron And systemd

- Choose `cron` for the smallest possible setup.
- Choose `systemd` if you want persistent missed-run handling, journal integration, and standard service lifecycle tooling.

## Equivalent Manual Arguments

If you configure the scheduler yourself, the command shape is always:

```text
/opt/oneshotprompt/OneShotPrompt.Console run --config /etc/oneshotprompt/config.yaml --job downloads-cleanup
```
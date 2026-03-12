# Linux Scheduling

OneShotPrompt does not schedule jobs itself. On Linux, the intended model is:

1. Publish a Linux binary.
2. Place the binary and config in stable locations.
3. Run the published binary manually once.
4. Register either a `cron` entry or a `systemd` timer.

For general operational guidance, see [operations.md](./operations.md).

## Before You Schedule Anything

Use this sequence first:

1. Validate the config.
2. Publish for the target runtime.
3. Copy the publish output to its final install directory.
4. Confirm the execution user can read the config and access any files the job touches.
5. Run the exact published command manually.

Validation example:

```bash
dotnet run --project src/OneShotPrompt.Console -- validate --config config.yaml
```

## Publish

Helper script:

```powershell
./scripts/publish-aot.ps1 -RuntimeIdentifier linux-x64
```

Direct `dotnet` command:

```bash
dotnet publish src/OneShotPrompt.Console/OneShotPrompt.Console.csproj -c Release -r linux-x64
```

Typical publish output:

```text
src/OneShotPrompt.Console/bin/Release/net10.0/linux-x64/publish/OneShotPrompt.Console
```

## Recommended Install Layout

One workable layout is:

```text
/opt/oneshotprompt/OneShotPrompt.Console
/etc/oneshotprompt/config.yaml
```

Because logs, custom skills, and persisted memory are all resolved from the config directory, that layout implies:

```text
/etc/oneshotprompt/logs/
/etc/oneshotprompt/skills/
/etc/oneshotprompt/.oneshotprompt/memory/
```

If you do not want runtime state under `/etc`, place the config file in a writable application data directory instead.

## Manual Verification Command

Before registering a scheduler, run the published executable directly:

```bash
/opt/oneshotprompt/OneShotPrompt.Console run --config /etc/oneshotprompt/config.yaml --job downloads-cleanup
```

That command shape is also the exact action schedulers should use.

## Cron

Use `cron` when you only need a simple time-based trigger and are comfortable handling output redirection yourself.

Example daily midnight run:

```cron
0 0 * * * /opt/oneshotprompt/OneShotPrompt.Console run --config /etc/oneshotprompt/config.yaml --job downloads-cleanup >> /var/log/oneshotprompt.log 2>&1
```

Operational notes:

- Use absolute paths for the executable and config file.
- Redirect output because cron does not provide an interactive console.
- Do not rely on no-argument invocation in automation.
- Ensure the cron user can read the config and access all target files and directories.

## systemd Service And Timer

Use `systemd` when you want standard service inspection, journal integration, and better control over missed runs.

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

### Enable And Start

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now oneshotprompt-downloads-cleanup.timer
```

### Inspect Status

```bash
systemctl status oneshotprompt-downloads-cleanup.timer
systemctl list-timers --all | grep oneshotprompt
journalctl -u oneshotprompt-downloads-cleanup.service
```

## Choosing Cron vs systemd

- Choose `cron` for the smallest possible setup.
- Choose `systemd` when you want centralized logs and `Persistent=true` behavior for missed runs.

## Common Failure Points

- The config path exists, but the execution user cannot write sibling `logs/` or `.oneshotprompt/memory/` content.
- Relative paths work in a shell session but fail under the scheduler.
- The job name in the scheduler command does not match the validated config exactly.
- The scheduler runs a different binary than the one you tested manually.
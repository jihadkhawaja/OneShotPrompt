# Operations Guide

This project is designed around a small CLI surface. The operational workflow is:

1. Validate the config.
2. List jobs.
3. Run all jobs or one named job.
4. Publish a Native AOT binary if you want scheduled execution without `dotnet run`.

## CLI Commands

### Validate

```powershell
dotnet run --project src/OneShotPrompt.Console -- validate --config config.yaml
```

PowerShell helper:

```powershell
./scripts/validate-config.ps1 -ConfigPath ./config.yaml
```

### List Jobs

```powershell
dotnet run --project src/OneShotPrompt.Console -- jobs --config config.yaml
```

PowerShell helper:

```powershell
./scripts/list-jobs.ps1 -ConfigPath ./config.yaml
```

### Run All Enabled Jobs

```powershell
dotnet run --project src/OneShotPrompt.Console -- run --config config.yaml
```

PowerShell helper:

```powershell
./scripts/run-job.ps1 -ConfigPath ./config.yaml
```

### Run One Job

```powershell
dotnet run --project src/OneShotPrompt.Console -- run --config config.yaml --job downloads-cleanup
```

PowerShell helper:

```powershell
./scripts/run-job.ps1 -ConfigPath ./config.yaml -JobName downloads-cleanup
```

## What The App Prints

During a run, the console prints the selected job name, the model response, and any per-job failure message.

Validation prints the job count when the file is valid.

Job listing prints one line per job in this format:

```text
- downloads-cleanup | Provider=OpenAI | Enabled=True | Schedule=Daily at midnight
```

## Memory Files

When memory persistence is enabled, each job stores a compact JSON history under:

```text
.oneshotprompt/memory/
```

Behavior details:

- Memory lives next to the active config file, not next to the repository root unless the config is there.
- Each job keeps up to 10 entries.
- Job names are sanitized before becoming filenames.

## Publishing

The console project is configured for Native AOT. Publish with either of these:

```powershell
dotnet publish src/OneShotPrompt.Console/OneShotPrompt.Console.csproj -c Release -r win-arm64
```

```powershell
./scripts/publish-aot.ps1 -RuntimeIdentifier win-arm64
```

The default publish output path is:

```text
src/OneShotPrompt.Console/bin/Release/net10.0/<rid>/publish/
```

## Windows Scheduling

For unattended Windows runs, publish first and then register a scheduled task against the produced executable.

Example:

```powershell
./scripts/register-daily-task.ps1 `
  -TaskName OneShotPrompt-DownloadsCleanup `
  -ExecutablePath ./src/OneShotPrompt.Console/bin/Release/net10.0/win-arm64/publish/OneShotPrompt.Console.exe `
  -ConfigPath ./config.yaml `
  -JobName downloads-cleanup `
  -Time 00:00
```

More detail is in [windows-task-scheduler.md](./windows-task-scheduler.md).

## Linux Scheduling

For unattended Linux runs, publish first and then use either `cron` or a `systemd` timer against the produced binary.

Simple `cron` example:

```cron
0 0 * * * /opt/oneshotprompt/OneShotPrompt.Console run --config /etc/oneshotprompt/config.yaml --job downloads-cleanup
```

More detail is in [linux-scheduling.md](./linux-scheduling.md).
# Windows Task Scheduler

OneShotPrompt does not include a built-in scheduler. On Windows, the intended execution model is a published executable plus a Task Scheduler entry.

## Recommended Flow

1. Validate the config.
2. Publish the app for the target runtime.
3. Run the published executable manually once.
4. Register a scheduled task.

## Publish

```powershell
./scripts/publish-aot.ps1 -RuntimeIdentifier win-arm64
```

Typical output path:

```text
src/OneShotPrompt.Console/bin/Release/net10.0/win-arm64/publish/OneShotPrompt.Console.exe
```

## Register A Daily Task

```powershell
./scripts/register-daily-task.ps1 `
  -TaskName OneShotPrompt-DownloadsCleanup `
  -ExecutablePath ./src/OneShotPrompt.Console/bin/Release/net10.0/win-arm64/publish/OneShotPrompt.Console.exe `
  -ConfigPath ./config.yaml `
  -JobName downloads-cleanup `
  -Time 00:00
```

This creates a daily task that runs only while the current user is logged in.

## Parameters

- `TaskName`: Scheduled task name.
- `ExecutablePath`: Full or relative path to `OneShotPrompt.Console.exe`.
- `ConfigPath`: Full or relative path to the config file.
- `JobName`: Name of the enabled job to run.
- `Time`: Daily trigger time in `HH:mm` 24-hour format.
- `Description`: Optional task description.

## Notes

- The script targets the current Windows user with an interactive logon principal.
- If your config path contains spaces, the generated scheduled task arguments quote it correctly.
- If you need a different trigger shape such as weekly or at-logon, use the same action arguments and replace the trigger manually in Task Scheduler.

## Equivalent Manual Arguments

If you prefer configuring Task Scheduler yourself, use:

Program:

```text
OneShotPrompt.Console.exe
```

Arguments:

```text
run --config C:\path\to\config.yaml --job downloads-cleanup
```
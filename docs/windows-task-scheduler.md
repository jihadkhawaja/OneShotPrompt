# Windows Task Scheduler

OneShotPrompt does not include an internal scheduler. On Windows, the supported unattended model is a published executable plus a Task Scheduler entry.

## Recommended Flow

1. Validate the config.
2. Publish a Windows binary.
3. Run that published executable manually once.
4. Register a scheduled task.

Do not skip the manual run. It confirms the real executable path, config path, and job name before Task Scheduler adds another layer of indirection.

## Publish

Helper script:

```powershell
./scripts/publish-aot.ps1 -RuntimeIdentifier win-arm64
```

Direct publish:

```powershell
dotnet publish src/OneShotPrompt.Console/OneShotPrompt.Console.csproj -c Release -r win-arm64
```

Typical output path:

```text
src/OneShotPrompt.Console/bin/Release/net10.0/win-arm64/publish/OneShotPrompt.Console.exe
```

## Manual Verification

Before registering the task, run the published executable directly:

```powershell
.\src\OneShotPrompt.Console\bin\Release\net10.0\win-arm64\publish\OneShotPrompt.Console.exe run --config .\config.yaml --job downloads-cleanup
```

The scheduler should use that same command shape.

## Register A Daily Task With The Helper Script

```powershell
./scripts/register-daily-task.ps1 `
  -TaskName OneShotPrompt-DownloadsCleanup `
  -ExecutablePath ./src/OneShotPrompt.Console/bin/Release/net10.0/win-arm64/publish/OneShotPrompt.Console.exe `
  -ConfigPath ./config.yaml `
  -JobName downloads-cleanup `
  -Time 00:00
```

What the script does:

- Resolves the executable and config paths to full paths.
- Fails fast if either path does not exist.
- Uses the executable's folder as the task working directory.
- Registers a daily trigger at the specified `HH:mm` time.
- Runs as the current Windows user.
- Uses an interactive logon principal with limited run level.

This means the scheduled task is designed to run while that user is logged in.

## Script Parameters

- `TaskName`: required scheduled task name.
- `ExecutablePath`: required path to `OneShotPrompt.Console.exe`.
- `ConfigPath`: required path to the config file.
- `JobName`: required enabled job name.
- `Time`: optional daily time in `HH:mm` format. Default: `00:00`.
- `Description`: optional task description.

## Equivalent Manual Task Scheduler Settings

If you prefer creating the task manually, use:

Program:

```text
C:\path\to\OneShotPrompt.Console.exe
```

Arguments:

```text
run --config C:\path\to\config.yaml --job downloads-cleanup
```

Start in:

```text
C:\path\to\publish\folder
```

Using an explicit `Start in` value avoids surprises when the task is launched outside your shell environment.

## Notes

- Use explicit `run --config ... --job ...` arguments for scheduled execution.
- Do not rely on no-argument invocation for automation.
- If the config path contains spaces, the helper script quotes it correctly in the registered action.
- If you need a different trigger shape such as weekly or at logon, reuse the same program and arguments and change only the trigger.

## Troubleshooting

- The task launches the wrong executable because you updated one publish folder but scheduled a different one.
- The job name is valid in the config you edited, but the task points at a different config file.
- The task starts successfully but the job cannot write sibling `logs/` or `.oneshotprompt/memory/` content next to the configured file.
- Manual runs work in PowerShell because of the current directory, but the task needs an explicit `Start in` folder.
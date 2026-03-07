using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace OneShotPrompt.Infrastructure.Tools;

public sealed class ProcessTools(string defaultWorkingDirectory)
{
    private readonly string _defaultWorkingDirectory = string.IsNullOrWhiteSpace(defaultWorkingDirectory)
        ? Environment.CurrentDirectory
        : Path.GetFullPath(defaultWorkingDirectory);

    [Description("Runs an installed executable directly without using a shell. Provide the executable name or path plus a single argument string. Supports cross-platform command execution, but not shell syntax such as pipes, redirection, or &&.")]
    public Task<string> RunCommand(string fileName, string arguments = "", string? workingDirectory = null, int timeoutSeconds = 60)
    {
        return ExecuteProcessAsync(fileName, arguments, workingDirectory, timeoutSeconds);
    }

    [Description("Runs a dotnet CLI command without using a shell. Use this for .NET and C# automation such as build, run, test, publish, format, or script-style workflows.")]
    public Task<string> RunDotNetCommand(string arguments, string? workingDirectory = null, int timeoutSeconds = 120)
    {
        return ExecuteProcessAsync("dotnet", arguments, workingDirectory, timeoutSeconds);
    }

    private async Task<string> ExecuteProcessAsync(string fileName, string arguments, string? workingDirectory, int timeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Executable name is required.";
        }

        arguments ??= string.Empty;

        if (timeoutSeconds is < 1 or > 600)
        {
            return "timeoutSeconds must be between 1 and 600.";
        }

        string resolvedWorkingDirectory;

        try
        {
            resolvedWorkingDirectory = ResolveWorkingDirectory(workingDirectory);
        }
        catch (Exception exception)
        {
            return $"Working directory resolution failed: {exception.Message}";
        }

        if (!Directory.Exists(resolvedWorkingDirectory))
        {
            return $"Working directory does not exist: {resolvedWorkingDirectory}";
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = resolvedWorkingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        try
        {
            if (!process.Start())
            {
                return $"Failed to start '{fileName}'.";
            }
        }
        catch (Exception exception)
        {
            return $"Failed to start '{fileName}': {exception.Message}";
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        var waitForExitTask = process.WaitForExitAsync();
        var completionTask = Task.WhenAll(standardOutputTask, standardErrorTask, waitForExitTask);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

        var completedTask = await Task.WhenAny(completionTask, timeoutTask);

        if (completedTask != completionTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            try
            {
                await Task.WhenAll(standardOutputTask, standardErrorTask, process.WaitForExitAsync());
            }
            catch
            {
            }

            return FormatResult(
                fileName,
                arguments,
                resolvedWorkingDirectory,
                process.HasExited ? process.ExitCode : null,
                await ReadCompletedTaskAsync(standardOutputTask),
                await ReadCompletedTaskAsync(standardErrorTask),
                timeoutSeconds,
                timedOut: true);
        }

        return FormatResult(
            fileName,
            arguments,
            resolvedWorkingDirectory,
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask,
            timeoutSeconds,
            timedOut: false);
    }

    private string ResolveWorkingDirectory(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return _defaultWorkingDirectory;
        }

        return Path.IsPathRooted(workingDirectory)
            ? Path.GetFullPath(workingDirectory)
            : Path.GetFullPath(Path.Combine(_defaultWorkingDirectory, workingDirectory));
    }

    private static async Task<string> ReadCompletedTaskAsync(Task<string> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            return task.Result;
        }

        try
        {
            return await task;
        }
        catch (Exception exception)
        {
            return $"<unavailable: {exception.Message}>";
        }
    }

    private static string FormatResult(string fileName, string arguments, string workingDirectory, int? exitCode, string standardOutput, string standardError, int timeoutSeconds, bool timedOut)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Command: {BuildCommandText(fileName, arguments)}");
        builder.AppendLine($"Working directory: {workingDirectory}");
        builder.AppendLine($"Timed out: {(timedOut ? $"yes after {timeoutSeconds}s" : "no")}");

        if (exitCode.HasValue)
        {
            builder.AppendLine($"Exit code: {exitCode.Value}");
        }

        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            builder.AppendLine("Standard output:");
            builder.AppendLine(Truncate(standardOutput.Trim(), 12_000));
        }

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            builder.AppendLine("Standard error:");
            builder.AppendLine(Truncate(standardError.Trim(), 12_000));
        }

        if (string.IsNullOrWhiteSpace(standardOutput) && string.IsNullOrWhiteSpace(standardError))
        {
            builder.AppendLine("Output: <empty>");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildCommandText(string fileName, string arguments)
    {
        return string.IsNullOrWhiteSpace(arguments)
            ? fileName
            : $"{fileName} {arguments}";
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }
}
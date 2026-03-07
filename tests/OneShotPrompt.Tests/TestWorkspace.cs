using System.Diagnostics;
using System.Reflection;

namespace OneShotPrompt.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "OneShotPrompt.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string GetPath(params string[] segments)
    {
        return segments.Aggregate(RootPath, Path.Combine);
    }

    public string WriteFile(string relativePath, string content)
    {
        var path = GetPath(relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch
        {
        }
    }
}

internal static class TestPaths
{
    public static string GetSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OneShotPrompt.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the solution root.");
    }

    public static string GetConsoleDllPath()
    {
        return Path.Combine(GetSolutionRoot(), "src", "OneShotPrompt.Console", "bin", "Debug", "net10.0", "OneShotPrompt.Console.dll");
    }
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput => string.Join(Environment.NewLine, new[] { StandardOutput, StandardError }.Where(text => !string.IsNullOrWhiteSpace(text)));
}

internal static class ProcessTestHarness
{
    public static async Task<ProcessResult> RunConsoleAsync(params string[] args)
    {
        var dllPath = TestPaths.GetConsoleDllPath();
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException("Console application was not built before test execution.", dllPath);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = TestPaths.GetSolutionRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.StartInfo.ArgumentList.Add(dllPath);
        foreach (var argument in args)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, await standardOutputTask, await standardErrorTask);
    }

    public static object? InvokePrivateStatic(Type type, string methodName, params object?[] arguments)
    {
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Could not find method '{methodName}'.");

        return method.Invoke(null, arguments);
    }

    public static async Task<object?> InvokePrivateStaticAsync(Type type, string methodName, params object?[] arguments)
    {
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Could not find method '{methodName}'.");

        var result = method.Invoke(null, arguments);

        if (result is not Task task)
        {
            return result;
        }

        await task;

        var taskType = task.GetType();
        return taskType.IsGenericType
            ? taskType.GetProperty("Result")!.GetValue(task)
            : null;
    }
}
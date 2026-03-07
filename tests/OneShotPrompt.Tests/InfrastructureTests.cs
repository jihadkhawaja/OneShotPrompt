using OneShotPrompt.Core.Models;
using OneShotPrompt.Infrastructure.Persistence;
using OneShotPrompt.Infrastructure.Tools;

namespace OneShotPrompt.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public async Task FileExecutionMemoryStore_LoadMissing_ReturnsEmptyDocument()
    {
        using var workspace = new TestWorkspace();
        var store = new FileExecutionMemoryStore();

        var document = await store.LoadAsync(workspace.RootPath, "nightly", CancellationToken.None);

        Assert.Empty(document.Entries);
    }

    [Fact]
    public async Task FileExecutionMemoryStore_SaveAndLoad_RoundTripsAndSanitizesName()
    {
        using var workspace = new TestWorkspace();
        var store = new FileExecutionMemoryStore();
        var document = new JobMemoryDocument
        {
            Entries =
            [
                new JobMemoryEntry
                {
                    TimestampUtc = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero),
                    Prompt = "prompt",
                    Response = "response",
                }
            ]
        };

        await store.SaveAsync(workspace.RootPath, "nightly:job", document, CancellationToken.None);

        var storedFiles = Directory.GetFiles(workspace.GetPath(".oneshotprompt", "memory"), "*.json");
        var storedPath = Assert.Single(storedFiles);
        Assert.Contains("nightly_job", Path.GetFileNameWithoutExtension(storedPath));

        var loaded = await store.LoadAsync(workspace.RootPath, "nightly:job", CancellationToken.None);

        var entry = Assert.Single(loaded.Entries);
        Assert.Equal("prompt", entry.Prompt);
        Assert.Equal("response", entry.Response);
    }

    [Fact]
    public void FileSystemTools_ReadOperations_HandleExpectedCases()
    {
        using var workspace = new TestWorkspace();
        var tools = new FileSystemTools();

        var emptyPath = workspace.WriteFile("empty.txt", string.Empty);
        var textPath = workspace.WriteFile("folder/sample.txt", "line1\nline2\nline3");
        var longPath = workspace.WriteFile("long.txt", new string('x', 8_200));
        Directory.CreateDirectory(workspace.GetPath("folder", "child"));
        workspace.WriteFile("folder/another.txt", "abc");

        Assert.Equal($"Unsupported known folder 'unknown'.", tools.GetKnownFolder("unknown"));
        Assert.Equal(Path.GetTempPath(), tools.GetKnownFolder("temp"));
        Assert.Contains("Directory does not exist", tools.ListDirectory(workspace.GetPath("missing")));

        var directoryListing = tools.ListDirectory(workspace.GetPath("folder"));
        Assert.Contains("[dir] child", directoryListing);
        Assert.Contains("[file] another.txt | 3 bytes", directoryListing);
        Assert.Contains("[file] sample.txt", directoryListing);

        Assert.Contains("File does not exist", tools.ReadTextFile(workspace.GetPath("missing.txt")));
        Assert.Equal(8_003, tools.ReadTextFile(longPath).Length);
        Assert.Equal("line1\nline2\nline3", tools.ReadTextFile(textPath).Replace("\r", string.Empty));

        Assert.Contains("Invalid line range", tools.ReadTextFileLines(textPath, 0, 1));
        Assert.Contains("File is empty", tools.ReadTextFileLines(emptyPath, 1, 1));
        Assert.Contains("beyond the end of the file", tools.ReadTextFileLines(textPath, 10, 12));

        var lineRange = tools.ReadTextFileLines(textPath, 2, 5).Replace("\r", string.Empty);
        Assert.Contains("Lines: 2-3 of 3", lineRange);
        Assert.Contains("2: line2", lineRange);
        Assert.Contains("3: line3", lineRange);

        var lengthInfo = tools.GetTextFileLength(textPath).Replace("\r", string.Empty);
        Assert.Contains("Characters: 17", lengthInfo);
        Assert.Contains("Lines: 3", lengthInfo);
        Assert.Contains("UTF-8 bytes: 17", lengthInfo);
        Assert.Contains("File does not exist", tools.GetTextFileLength(workspace.GetPath("missing.txt")));
    }

    [Fact]
    public void FileSystemTools_MutationOperations_HandleFiles()
    {
        using var workspace = new TestWorkspace();
        var tools = new FileSystemTools();

        var createPath = workspace.GetPath("created");
        Assert.Equal($"Directory ready: {createPath}", tools.CreateDirectory(createPath));
        Assert.True(Directory.Exists(createPath));

        var filePath = workspace.GetPath("created", "note.txt");
        Assert.Equal($"Wrote file: {filePath}", tools.WriteTextFile(filePath, "hello"));
        Assert.Equal($"File already exists: {filePath}", tools.WriteTextFile(filePath, "hello again"));
        Assert.Equal($"Wrote file: {filePath}", tools.WriteTextFile(filePath, "updated", overwrite: true));
        Assert.Equal("updated", File.ReadAllText(filePath));

        var copyPath = workspace.GetPath("copies", "note.txt");
        Assert.Contains("Copied", tools.CopyFile(filePath, copyPath));
        Assert.True(File.Exists(copyPath));
        Assert.Contains("Source file does not exist", tools.CopyFile(workspace.GetPath("missing.txt"), copyPath));

        var movedPath = workspace.GetPath("moved", "note.txt");
        Assert.Contains("Moved", tools.MoveFile(copyPath, movedPath));
        Assert.True(File.Exists(movedPath));
        Assert.False(File.Exists(copyPath));
        Assert.Contains("Source file does not exist", tools.MoveFile(copyPath, movedPath));

        Assert.Equal($"Deleted file: {movedPath}", tools.DeleteFile(movedPath));
        Assert.Equal($"File does not exist: {movedPath}", tools.DeleteFile(movedPath));
    }

    [Fact]
    public async Task ProcessTools_ReturnsValidationAndProcessResults()
    {
        using var workspace = new TestWorkspace();
        var tools = new ProcessTools(workspace.RootPath);

        Assert.Equal("Executable name is required.", await tools.RunCommand(string.Empty));
        Assert.Equal("timeoutSeconds must be between 1 and 600.", await tools.RunCommand("dotnet", timeoutSeconds: 0));
        Assert.Contains("Working directory does not exist", await tools.RunCommand("dotnet", workingDirectory: workspace.GetPath("missing")));
        Assert.Contains("Working directory resolution failed", await tools.RunCommand("dotnet", workingDirectory: "bad\0dir"));

        var failedStart = await tools.RunCommand("definitely-not-a-real-executable-12345");
        Assert.Contains("Failed to start 'definitely-not-a-real-executable-12345'", failedStart);

        var success = await tools.RunDotNetCommand("--version", timeoutSeconds: 30);
        Assert.Contains("Command: dotnet --version", success);
        Assert.Contains($"Working directory: {workspace.RootPath}", success);
        Assert.Contains("Timed out: no", success);
        Assert.Contains("Exit code: 0", success);
        Assert.Contains("Standard output:", success);
    }

    [Fact]
    public async Task ProcessTools_HandlesTimeoutAndEmptyOutputBranches()
    {
        using var workspace = new TestWorkspace();
        var tools = new ProcessTools(workspace.RootPath);

        var timeout = await tools.RunCommand("pwsh", "-NoLogo -NoProfile -Command Start-Sleep -Seconds 2", timeoutSeconds: 1);
        Assert.Contains("Command: pwsh -NoLogo -NoProfile -Command Start-Sleep -Seconds 2", timeout);
        Assert.Contains("Timed out: yes after 1s", timeout);

        var empty = await tools.RunCommand("pwsh", "-NoLogo -NoProfile -Command exit 0", timeoutSeconds: 10);
        Assert.Contains("Timed out: no", empty);
        Assert.Contains("Exit code: 0", empty);
        Assert.Contains("Output: <empty>", empty);
    }

    [Fact]
    public async Task ProcessTools_ReadCompletedTaskAsync_ReturnsUnavailableForFaultedTask()
    {
        var result = (string)(await ProcessTestHarness.InvokePrivateStaticAsync(
            typeof(ProcessTools),
            "ReadCompletedTaskAsync",
            Task.FromException<string>(new InvalidOperationException("boom"))))!;

        Assert.Equal("<unavailable: boom>", result);
    }
}
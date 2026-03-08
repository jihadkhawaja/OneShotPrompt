using System.ComponentModel;
using System.Text;

namespace OneShotPrompt.Infrastructure.Tools;

public sealed class FileSystemTools
{
    [Description("Returns a useful system path. Supported names: home, desktop, documents, downloads, temp.")]
    public string GetKnownFolder(string name)
    {
        return name.Trim().ToLowerInvariant() switch
        {
            "home" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "downloads" => GetDownloadsFolder(),
            "temp" => Path.GetTempPath(),
            _ => $"Unsupported known folder '{name}'.",
        };
    }

    [Description("Lists the contents of a directory. Use this before moving or deleting files.")]
    public string ListDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return $"Directory does not exist: {path}";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Directory: {path}");

        foreach (var directory in Directory.EnumerateDirectories(path).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"[dir] {Path.GetFileName(directory)}");
        }

        foreach (var file in Directory.EnumerateFiles(path).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(file);
            builder.AppendLine($"[file] {info.Name} | {info.Length} bytes");
        }

        return builder.ToString().TrimEnd();
    }

    [Description("Reads a UTF-8 text file and returns up to 8 KB of content.")]
    public string ReadTextFile(string path)
    {
        if (!File.Exists(path))
        {
            return $"File does not exist: {path}";
        }

        var text = File.ReadAllText(path, Encoding.UTF8);
        return text.Length <= 8_000 ? text : text[..8_000] + "...";
    }

    [Description("Reads a UTF-8 text file from startLine to endLine, inclusive. Line numbers are 1-based. Use this for large files when only a specific range is needed.")]
    public string ReadTextFileLines(string path, int startLine, int endLine)
    {
        if (!File.Exists(path))
        {
            return $"File does not exist: {path}";
        }

        if (startLine < 1 || endLine < startLine)
        {
            return "Invalid line range. startLine must be >= 1 and endLine must be >= startLine.";
        }

        var lines = File.ReadAllLines(path, Encoding.UTF8);

        if (lines.Length == 0)
        {
            return $"File is empty: {path}";
        }

        if (startLine > lines.Length)
        {
            return $"Requested startLine {startLine} is beyond the end of the file. Total lines: {lines.Length}.";
        }

        var clampedEndLine = Math.Min(endLine, lines.Length);
        var builder = new StringBuilder();
        builder.AppendLine($"File: {path}");
        builder.AppendLine($"Lines: {startLine}-{clampedEndLine} of {lines.Length}");

        for (var lineNumber = startLine; lineNumber <= clampedEndLine; lineNumber++)
        {
            builder.AppendLine($"{lineNumber}: {lines[lineNumber - 1]}");
        }

        return builder.ToString().TrimEnd();
    }

    [Description("Returns UTF-8 text size details for a file, including character, line, and byte counts. Use this before chunked reads of large files.")]
    public string GetTextFileLength(string path)
    {
        if (!File.Exists(path))
        {
            return $"File does not exist: {path}";
        }

        var text = File.ReadAllText(path, Encoding.UTF8);
        var lineCount = text.Length == 0
            ? 0
            : text.Split(["\r\n", "\n"], StringSplitOptions.None).Length;
        var byteCount = Encoding.UTF8.GetByteCount(text);

        return $"File: {path}{Environment.NewLine}Characters: {text.Length}{Environment.NewLine}Lines: {lineCount}{Environment.NewLine}UTF-8 bytes: {byteCount}";
    }

    [Description("Creates a directory if it does not exist.")]
    public string CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return $"Directory ready: {path}";
    }

    [Description("Moves a file. Set overwrite=true to replace an existing destination file.")]
    public string MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        if (!File.Exists(sourcePath))
        {
            return $"Source file does not exist: {sourcePath}";
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("Destination directory is invalid."));
        File.Move(sourcePath, destinationPath, overwrite);
        return $"Moved '{sourcePath}' to '{destinationPath}'.";
    }

    [Description("Moves multiple files in parallel for faster batch operations. Provide source and destination paths as pipe-delimited strings (e.g. 'a.txt|b.txt|c.txt'). Both lists must have the same number of entries. Set overwrite=true to replace existing destination files.")]
    public string MoveFiles(string sourcePaths, string destinationPaths, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(sourcePaths) || string.IsNullOrWhiteSpace(destinationPaths))
        {
            return "No files specified.";
        }

        var sources = sourcePaths.Split('|');
        var destinations = destinationPaths.Split('|');

        if (sources.Length != destinations.Length)
        {
            return "sourcePaths and destinationPaths must have the same number of entries.";
        }

        var results = new string[sources.Length];
        Parallel.For(0, sources.Length, index =>
        {
            try
            {
                if (!File.Exists(sources[index]))
                {
                    results[index] = $"SKIP: Source not found: {sources[index]}";
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinations[index])
                    ?? throw new InvalidOperationException("Destination directory is invalid."));
                File.Move(sources[index], destinations[index], overwrite);
                results[index] = $"OK: {sources[index]} -> {destinations[index]}";
            }
            catch (Exception exception)
            {
                results[index] = $"FAIL: {sources[index]} -- {exception.Message}";
            }
        });

        var succeeded = results.Count(result => result.StartsWith("OK", StringComparison.Ordinal));
        var builder = new StringBuilder();
        builder.AppendLine($"Batch move completed: {succeeded} succeeded, {sources.Length - succeeded} failed.");

        foreach (var result in results)
        {
            builder.AppendLine(result);
        }

        return builder.ToString().TrimEnd();
    }

    [Description("Copies a file. Set overwrite=true to replace an existing destination file.")]
    public string CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        if (!File.Exists(sourcePath))
        {
            return $"Source file does not exist: {sourcePath}";
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("Destination directory is invalid."));
        File.Copy(sourcePath, destinationPath, overwrite);
        return $"Copied '{sourcePath}' to '{destinationPath}'.";
    }

    [Description("Deletes a file.")]
    public string DeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return $"File does not exist: {path}";
        }

        File.Delete(path);
        return $"Deleted file: {path}";
    }

    [Description("Writes UTF-8 text to a file. Set overwrite=true to replace an existing file.")]
    public string WriteTextFile(string path, string content, bool overwrite = false)
    {
        if (File.Exists(path) && !overwrite)
        {
            return $"File already exists: {path}";
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Target directory is invalid."));
        File.WriteAllText(path, content, Encoding.UTF8);
        return $"Wrote file: {path}";
    }

    private static string GetDownloadsFolder()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, "Downloads");
    }
}
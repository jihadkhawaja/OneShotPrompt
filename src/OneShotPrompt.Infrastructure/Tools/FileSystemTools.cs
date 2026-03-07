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
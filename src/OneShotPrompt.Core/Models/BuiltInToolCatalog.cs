namespace OneShotPrompt.Core.Models;

public static class BuiltInToolCatalog
{
    public static IReadOnlyList<BuiltInToolDefinition> All { get; } =
    [
        new("GetKnownFolder", false),
        new("ListDirectory", false),
        new("ReadTextFile", false),
        new("ReadTextFileLines", false),
        new("GetTextFileLength", false),
        new("CreateDirectory", true),
        new("MoveFile", true),
        new("CopyFile", true),
        new("DeleteFile", true),
        new("WriteTextFile", true),
        new("RunCommand", true),
        new("RunDotNetCommand", true),
    ];

    public static bool IsKnown(string toolName)
    {
        return All.Any(tool => tool.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool RequiresMutation(string toolName)
    {
        return All.FirstOrDefault(tool => tool.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase))?.RequiresMutation ?? false;
    }
}

public sealed record BuiltInToolDefinition(string Name, bool RequiresMutation);
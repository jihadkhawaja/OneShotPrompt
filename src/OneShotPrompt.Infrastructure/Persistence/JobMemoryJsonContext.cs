using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Infrastructure.Persistence;

[ExcludeFromCodeCoverage]
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(JobMemoryDocument))]
internal sealed partial class JobMemoryJsonContext : JsonSerializerContext
{
}
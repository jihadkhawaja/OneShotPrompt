using System.Diagnostics;
using System.Text.Json;
using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Infrastructure.Channels;

public sealed class WhatsAppPersonalChannelListener(string workingDirectory)
{
    private const int WaitTimeoutSeconds = 300;

    private readonly string _workingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
        ? Environment.CurrentDirectory
        : Path.GetFullPath(workingDirectory);

    public async Task<JobTriggerSignal> WaitForNextMessageAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await ExecuteWaitCommandAsync(cancellationToken);

            if (result.TimedOut)
            {
                continue;
            }

            return new JobTriggerSignal(
                "whatsapp-personal-channel",
                FormatSummary(result.PhoneNumber, result.Body, result.MessageType));
        }
    }

    private async Task<WaitCommandResult> ExecuteWaitCommandAsync(CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "node",
                WorkingDirectory = _workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.StartInfo.ArgumentList.Add("--disable-warning=DEP0040");
        process.StartInfo.ArgumentList.Add(Path.Combine("tools", "whatsapp-personal-channel", "channel.mjs"));
        process.StartInfo.ArgumentList.Add("wait-next-message");
        process.StartInfo.ArgumentList.Add("--timeout-seconds");
        process.StartInfo.ArgumentList.Add(WaitTimeoutSeconds.ToString());

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start the WhatsApp personal channel bridge client.");
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to start the WhatsApp personal channel bridge client: {exception.Message}", exception);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;

            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(standardError)
                    ? standardOutput.Trim()
                    : standardError.Trim();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                    ? "The WhatsApp personal channel bridge client failed without output."
                    : detail);
            }

            return ParseWaitResult(standardOutput);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            throw;
        }
    }

    private static WaitCommandResult ParseWaitResult(string standardOutput)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            throw new InvalidOperationException("The WhatsApp personal channel bridge client returned an empty response.");
        }

        try
        {
            using var document = JsonDocument.Parse(standardOutput);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("The WhatsApp personal channel bridge client returned an invalid JSON payload.");
            }

            var timedOut = root.TryGetProperty("timedOut", out var timedOutElement) && timedOutElement.GetBoolean();
            if (timedOut)
            {
                return new WaitCommandResult(true, null, null, null);
            }

            if (!root.TryGetProperty("message", out var messageElement) || messageElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("The WhatsApp personal channel bridge client did not return a message payload.");
            }

            return new WaitCommandResult(
                false,
                messageElement.TryGetProperty("phoneNumber", out var phoneNumberElement) && phoneNumberElement.ValueKind == JsonValueKind.String
                    ? phoneNumberElement.GetString()
                    : null,
                messageElement.TryGetProperty("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.String
                    ? bodyElement.GetString()
                    : null,
                messageElement.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString()
                    : null);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The WhatsApp personal channel bridge client returned invalid JSON: {exception.Message}", exception);
        }
    }

    private static string FormatSummary(string? phoneNumber, string? body, string? messageType)
    {
        var prefix = string.IsNullOrWhiteSpace(phoneNumber)
            ? "Incoming message"
            : $"Incoming message from {phoneNumber}";

        if (!string.IsNullOrWhiteSpace(body))
        {
            return $"{prefix}: {Truncate(body.Trim(), 120)}";
        }

        return string.IsNullOrWhiteSpace(messageType)
            ? prefix
            : $"{prefix} ({messageType.Trim()})";
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }

    private sealed record WaitCommandResult(bool TimedOut, string? PhoneNumber, string? Body, string? MessageType);
}
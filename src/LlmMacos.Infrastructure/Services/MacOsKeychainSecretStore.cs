using System.Diagnostics;
using System.Text;
using LlmMacos.Core.Abstractions;

namespace LlmMacos.Infrastructure.Services;

public sealed class MacOsKeychainSecretStore : ISecretStore
{
    private const string ServiceName = "com.figge.llm-macos.hf-token";
    private const string AccountName = "huggingface";

    public async Task SetHfTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token must not be empty.", nameof(token));
        }

        var result = await RunSecurityAsync(
            [
                "add-generic-password",
                "-a", AccountName,
                "-s", ServiceName,
                "-w", token,
                "-U"
            ],
            ct,
            tolerateMissingItem: false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to save Hugging Face token to Keychain: {result.StdErr}");
        }
    }

    public async Task<string?> GetHfTokenAsync(CancellationToken ct)
    {
        var result = await RunSecurityAsync(
            [
                "find-generic-password",
                "-a", AccountName,
                "-s", ServiceName,
                "-w"
            ],
            ct,
            tolerateMissingItem: true);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var token = result.StdOut.Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public async Task ClearHfTokenAsync(CancellationToken ct)
    {
        await RunSecurityAsync(
            [
                "delete-generic-password",
                "-a", AccountName,
                "-s", ServiceName
            ],
            ct,
            tolerateMissingItem: true);
    }

    private static async Task<ProcessResult> RunSecurityAsync(
        IReadOnlyList<string> args,
        CancellationToken ct,
        bool tolerateMissingItem)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "security",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        if (tolerateMissingItem && process.ExitCode != 0 && stdErr.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessResult(0, string.Empty, string.Empty);
        }

        return new ProcessResult(process.ExitCode, stdOut, stdErr);
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}

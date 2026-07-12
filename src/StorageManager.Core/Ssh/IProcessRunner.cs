using System.Diagnostics;

namespace StorageManager.Ssh;

/// <summary>Result of running an external process.</summary>
public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Ok => ExitCode == 0;
    public static ProcessResult Failure(string message) => new(-1, "", message);
}

/// <summary>
/// Runs external processes (ssh, kinit, …). Abstracted so the connection logic
/// can be unit-tested with a fake instead of spawning real programs.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs <paramref name="file"/> with the given argument list, optionally feeding stdin.</summary>
    Task<ProcessResult> RunAsync(
        string file,
        IReadOnlyList<string> args,
        string? stdin = null,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}

/// <summary>The real system process runner.</summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    public static SystemProcessRunner Instance { get; } = new();

    public async Task<ProcessResult> RunAsync(
        string file,
        IReadOnlyList<string> args,
        string? stdin = null,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
        };
        foreach (var a in args)
            info.ArgumentList.Add(a);
        if (environment is not null)
            foreach (var (k, v) in environment)
                info.EnvironmentVariables[k] = v;

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException($"{file} did not start.");
        }
        catch (Exception ex)
        {
            return ProcessResult.Failure(ex.Message);
        }

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin);
                process.StandardInput.Close();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeout is { } t)
                timeoutCts.CancelAfter(t);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return ProcessResult.Failure($"{file} timed out.");
            }

            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (Exception ex)
        {
            return ProcessResult.Failure(ex.Message);
        }
        finally
        {
            process.Dispose();
        }
    }
}

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ResultsService.Services;

public record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public class ProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner> logger)
    {
        _logger = logger;
    }

    public async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing command: {FileName} {Arguments}", startInfo.FileName, startInfo.Arguments);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        var stdOut = new List<string>();
        var stdErr = new List<string>();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdOut.Add(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdErr.Add(args.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{startInfo.FileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WaitForExitAsync(process, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return new ProcessResult(process.ExitCode, string.Join(Environment.NewLine, stdOut), string.Join(Environment.NewLine, stdErr));
    }

    private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        var completionSource = new TaskCompletionSource<object?>();

        void Handler(object? sender, EventArgs _) => completionSource.TrySetResult(null);
        process.Exited += Handler;

        try
        {
            if (process.HasExited)
            {
                return;
            }

            using var registration = cancellationToken.Register(() =>
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
            });

            await completionSource.Task.ConfigureAwait(false);
        }
        finally
        {
            process.Exited -= Handler;
        }
    }
}

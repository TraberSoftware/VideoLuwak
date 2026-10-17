using System.Diagnostics;
using System.Text;
using VideoLuwak.Core.Abstractions;

namespace VideoLuwak.Core.Ffmpeg;

/// <summary>
/// Default <see cref="IProcessRunner"/> built on <see cref="Process"/>. Streams
/// stdout / stderr line-by-line to optional callbacks while accumulating the full
/// output, and enforces an optional timeout plus cancellation semantics. Output
/// streams are always read before waiting, so the runner can never deadlock on a
/// full pipe buffer.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        process.Start();

        var stdoutTask = PumpAsync(process.StandardOutput, standardOutput, onStdOutLine, cancellationToken);
        var stderrTask = PumpAsync(process.StandardError, standardError, onStdErrLine, cancellationToken);

        var timedOut = false;
        using var timeoutSource = new CancellationTokenSource();
        if (request.Timeout is { } timeout)
        {
            timeoutSource.CancelAfter(timeout);
        }

        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            try
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The process object was already torn down.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
                stopwatch.Stop();
                throw new OperationCanceledException(cancellationToken);
            }

            timedOut = true;
        }

        await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
        stopwatch.Stop();

        return new ProcessResult
        {
            ExitCode = SafeExitCode(process),
            StandardOutput = standardOutput.ToString(),
            StandardError = standardError.ToString(),
            TimedOut = timedOut,
            Duration = stopwatch.Elapsed,
        };
    }

    private static async Task PumpAsync(
        StreamReader reader,
        StringBuilder sink,
        Action<string>? onLine,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                sink.AppendLine(line);
                Invoke(onLine, line);
            }
        }
        catch (OperationCanceledException)
        {
            await DrainRemainingAsync(reader, sink, onLine).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The process was killed and its streams closed.
        }
    }

    private static async Task DrainRemainingAsync(StreamReader reader, StringBuilder sink, Action<string>? onLine)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                sink.AppendLine(line);
                Invoke(onLine, line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Nothing left to drain.
        }
    }

    private static async Task DrainAsync(Task first, Task second)
    {
        try
        {
            await Task.WhenAll(first, second).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Streams torn down while the process was killed.
        }
    }

    private static void Invoke(Action<string>? callback, string line)
    {
        if (callback is null)
        {
            return;
        }

        try
        {
            callback(line);
        }
        catch (Exception)
        {
            // A faulty progress callback must never abort process execution.
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // Best effort kill.
        }
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
}

using System.Diagnostics;

namespace GLauncher.Core.Launching;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration);

public sealed record GameRunResult(int? ExitCode, TimeSpan Duration, bool AutoStopped, string LogPath);

/// <summary>
/// 进程封装。两种用法：探测（抓 stdout/stderr）与启动游戏（输出重定向到日志文件）。
/// </summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunCapturedAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException($"进程启动失败：{executablePath}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"进程 {timeout.TotalSeconds:0}s 内未退出：{executablePath}");
        }

        return new ProcessResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false),
            stopwatch.Elapsed);
    }

    /// <summary>
    /// 启动游戏并把 stdout/stderr 写进日志文件。给了 <paramref name="autoStopAfter"/> 就
    /// 到点杀进程树（验收用：证明"能起来、能撑住"，而不是让无人值守的客户端一直挂着）。
    /// </summary>
    public static async Task<GameRunResult> RunLoggedAsync(
        LaunchCommand command,
        string logFilePath,
        TimeSpan? autoStopAfter,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var startInfo = new ProcessStartInfo(command.JavaExePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(command.WorkingDirectory)
                ? command.WorkingDirectory
                : Path.GetDirectoryName(command.JavaExePath) ?? string.Empty,
        };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var gate = new object();
        await using var log = new StreamWriter(logFilePath, append: false) { AutoFlush = true };
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => Write(log, gate, e.Data);
        process.ErrorDataReceived += (_, e) => Write(log, gate, e.Data);

        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException($"启动失败：{command.JavaExePath}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var autoStopped = false;
        if (autoStopAfter is { } limit)
        {
            using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stopCts.CancelAfter(limit);
            try
            {
                await process.WaitForExitAsync(stopCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                autoStopped = true;
                TryKill(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        else
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        return new GameRunResult(
            autoStopped ? null : process.ExitCode,
            stopwatch.Elapsed,
            autoStopped,
            logFilePath);
    }

    private static void Write(StreamWriter log, object gate, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (gate)
        {
            log.WriteLine(line);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 已经退出
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 权限问题：尽力而为
        }
    }
}

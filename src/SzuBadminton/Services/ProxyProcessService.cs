using System.Diagnostics;
using System.IO;

namespace SzuBadminton.Services;

public sealed class ProxyProcessService : IDisposable
{
    private Process? _process;

    public event Action<string>? OutputReceived;

    public bool IsRunning => _process is { HasExited: false };

    public string StatusText => IsRunning ? $"运行中（PID {_process!.Id}）" : "未运行";

    public void Start(string executablePath, string arguments)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("代理进程已经在运行。");
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("请先选择代理程序。");
        }

        var fullPath = Path.GetFullPath(executablePath.Trim());
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到代理程序，请检查路径。", fullPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fullPath,
            Arguments = arguments?.Trim() ?? string.Empty,
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, eventArgs) => ForwardOutput(eventArgs.Data);
        _process.ErrorDataReceived += (_, eventArgs) => ForwardOutput(eventArgs.Data);
        _process.Exited += (_, _) => OutputReceived?.Invoke("代理进程已退出。");

        try
        {
            if (!_process.Start())
            {
                throw new InvalidOperationException("代理程序未能启动。");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            OutputReceived?.Invoke($"代理进程已启动（PID {_process.Id}）。");
        }
        catch
        {
            _process.Dispose();
            _process = null;
            throw;
        }
    }

    public void Stop()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
                OutputReceived?.Invoke("代理进程已停止。");
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private void ForwardOutput(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            OutputReceived?.Invoke(message);
        }
    }

    public void Dispose() => Stop();
}

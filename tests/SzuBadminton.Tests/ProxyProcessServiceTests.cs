using SzuBadminton.Services;
using Xunit;

namespace SzuBadminton.Tests;

public sealed class ProxyProcessServiceTests
{
    [Fact]
    public void StartAndStop_ControlsOwnedProcess()
    {
        using var service = new ProxyProcessService();
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        service.Start(powershell, "-NoProfile -Command \"Start-Sleep -Seconds 30\"");
        Assert.True(service.IsRunning);
        Assert.Contains("PID", service.StatusText);

        service.Stop();
        Assert.False(service.IsRunning);
        Assert.Equal("未运行", service.StatusText);
    }

    [Fact]
    public void Start_RejectsMissingExecutable()
    {
        using var service = new ProxyProcessService();
        Assert.Throws<FileNotFoundException>(() => service.Start("F:\\not-present\\proxy.exe", string.Empty));
        Assert.False(service.IsRunning);
    }

    [Fact]
    public void Start_RejectsEmptyPath()
    {
        using var service = new ProxyProcessService();
        Assert.Throws<InvalidOperationException>(() => service.Start(string.Empty, string.Empty));
    }
}

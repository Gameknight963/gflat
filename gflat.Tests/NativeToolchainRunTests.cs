using System.Diagnostics;
using Xunit.Abstractions;

namespace gflat.Tests;

[Collection("Project SDK environment")]
public sealed class NativeToolchainRunTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(5000)]
    [InlineData(-1)]
    public void CapturesBothStreamsAndExitCode(int timeout)
    {
        var command = Shell("echo normal-out", "echo normal-err 1>&2", "exit 7");
        if (timeout == -1)
            command = OperatingSystem.IsWindows()
                ? PowerShell("Start-Sleep -Milliseconds 300; [Console]::Out.WriteLine('normal-out'); [Console]::Error.WriteLine('normal-err'); exit 7")
                : Shell("/bin/sleep 0.3", "echo normal-out", "echo normal-err 1>&2", "exit 7");
        ProcessResult result = NativeToolchain.Run(command.Executable, command.Arguments, timeout);
        Assert.Equal(7, result.ExitCode);
        Assert.Equal("normal-out", result.StandardOutput.Trim());
        Assert.Equal("normal-err", result.StandardError.Trim());
    }

    [Fact]
    public void RejectsInvalidTimeout()
    {
        var command = Shell("exit 0");
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeToolchain.Run(command.Executable, command.Arguments, -2));
    }

    [Fact]
    public async Task TimesOutWhileParentIsRunning()
    {
        var command = OperatingSystem.IsWindows()
            ? PowerShell("Start-Sleep -Seconds 4")
            : Shell("/bin/sleep 4");
        var result = await Task.Run(() => ObserveRun(command, 300)).WaitAsync(TimeSpan.FromSeconds(10));
        output.WriteLine($"Running-parent timeout: {result.ElapsedMilliseconds}ms, {result.Error}");
        Assert.IsType<TimeoutException>(result.Error);
        Assert.True(result.ElapsedMilliseconds < 3000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutIncludesPipeHeldByExitedParentsChild(bool holdStderr)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-run-pipes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Cold Windows process setup exceeded 1.2s. Grant 15s, with a 30s holder:
            // the old unbounded EOF wait still returns success and fails this test.
            int timeout = OperatingSystem.IsWindows() ? 15000 : 1200;
            int holderSeconds = OperatingSystem.IsWindows() ? 30 : 4;
            TimeSpan watchdog = TimeSpan.FromSeconds(OperatingSystem.IsWindows() ? 90 : 12);
            TimeSpan cleanupWatchdog = TimeSpan.FromSeconds(OperatingSystem.IsWindows() ? 60 : 7);
            string started = Path.Combine(directory, "child.started");
            string done = Path.Combine(directory, "child.done");
            string parentExit = Path.Combine(directory, "parent.exiting");
            string parentEntry = Path.Combine(directory, "parent.entry");
            string childPid = Path.Combine(directory, "parent.child-pid");
            string parentError = Path.Combine(directory, "parent.error");
            string childError = Path.Combine(directory, "child.error");
            (string Executable, string[] Arguments) command;
            if (OperatingSystem.IsWindows())
            {
                // No child pipe writes: cancellation may close the reader before the finite child exits.
                string child = "$ProgressPreference = 'SilentlyContinue'; $ErrorActionPreference = 'Stop'; try { " +
                    $"[IO.File]::WriteAllText({PowerShellQuote(started)}, [string]$PID + ' ' + [DateTime]::UtcNow.ToString('O')); " +
                    $"Start-Sleep -Seconds {holderSeconds}; [IO.File]::WriteAllText({PowerShellQuote(done)}, [DateTime]::UtcNow.ToString('O')) " +
                    $"}} catch {{ [IO.File]::WriteAllText({PowerShellQuote(childError)}, $_.Exception.ToString()); exit 1 }}";
                string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(child));
                // Launch directly so temporary paths never pass through batch expansion (% or !).
                command = PowerShell("$ErrorActionPreference = 'Stop'; try { " +
                    $"[IO.File]::WriteAllText({PowerShellQuote(parentEntry)}, [string]$PID + ' ' + [DateTime]::UtcNow.ToString('O')); " +
                    $"$info = [Diagnostics.ProcessStartInfo]::new({PowerShellQuote(PowerShellExecutable)}); " +
                    "$info.UseShellExecute = $false; $info.CreateNoWindow = $true; " +
                    $"$info.{(holdStderr ? "RedirectStandardOutput" : "RedirectStandardError")} = $true; " +
                    $"$info.Arguments = '-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}'; " +
                    "$child = [Diagnostics.Process]::Start($info); " +
                    $"[IO.File]::WriteAllText({PowerShellQuote(childPid)}, [string]($child.Id)); $child.Dispose(); " +
                    $"[IO.File]::WriteAllText({PowerShellQuote(parentExit)}, [DateTime]::UtcNow.ToString('O')); exit 0 " +
                    $"}} catch {{ [IO.File]::WriteAllText({PowerShellQuote(parentError)}, $_.Exception.ToString()); exit 1 }}");
            }
            else
            {
                string child = $"echo started > {ShellQuote(started)}; /bin/sleep 4; echo done > {ShellQuote(done)}";
                command = Shell($"({child}) {(holdStderr ? ">/dev/null" : "2>/dev/null")} &", $"echo exiting > {ShellQuote(parentExit)}", "exit 0");
            }
            var observation = await Task.Run(() =>
            {
                var result = ObserveRun(command, timeout);
                return (Run: result, ParentReachedExit: File.Exists(parentExit), ChildDoneAtReturn: File.Exists(done));
            }).WaitAsync(watchdog);
            output.WriteLine($"stderr={holdStderr}; timeout={timeout}ms; holder={holderSeconds}s; elapsed={observation.Run.ElapsedMilliseconds}ms; parent-exiting={observation.ParentReachedExit}; child-done-at-return={observation.ChildDoneAtReturn}; result={observation.Run.Result}; error={observation.Run.Error}");
            // Let the self-limiting child finish before either cleanup or assertion failure.
            var childWait = Stopwatch.StartNew();
            while (!File.Exists(done) && !File.Exists(childError) && childWait.Elapsed < cleanupWatchdog) await Task.Delay(25);
            foreach (string path in new[] { parentEntry, childPid, parentExit, started, done, parentError, childError })
                output.WriteLine($"{Path.GetFileName(path)}: {(File.Exists(path) ? File.ReadAllText(path) : "<missing>")}");
            Assert.True(observation.ParentReachedExit, "Parent must have reached its immediate exit before the timeout.");
            Assert.True(File.Exists(started), "Child must have started and inherited the pipe.");
            Assert.True(File.Exists(done), "Finite child did not finish.");
            Assert.IsType<TimeoutException>(observation.Run.Error);
            Assert.False(observation.ChildDoneAtReturn, "Run must time out before the finite pipe holder finishes.");
        }
        catch (Exception error)
        {
            error.Data["NativeToolchainRunDirectory"] = directory;
            output.WriteLine($"Retained fixture: {directory}");
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static string PowerShellExecutable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    private static (string Executable, string[] Arguments) PowerShell(string script)
        => (PowerShellExecutable, ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "$ProgressPreference = 'SilentlyContinue'; " + script]);

    private static string PowerShellQuote(string value) => "'" + value.Replace("'", "''") + "'";
    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private static (ProcessResult? Result, Exception? Error, long ElapsedMilliseconds) ObserveRun((string Executable, string[] Arguments) command, int timeout)
    {
        var watch = Stopwatch.StartNew();
        try { return (NativeToolchain.Run(command.Executable, command.Arguments, timeout), null, watch.ElapsedMilliseconds); }
        catch (Exception error) { return (null, error, watch.ElapsedMilliseconds); }
    }

    private static (string Executable, string[] Arguments) Shell(params string[] commands)
        => OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("ComSpec")!, ["/d", "/c", string.Join(" & ", commands)])
            : ("/bin/sh", ["-c", string.Join("\n", commands)]);
}

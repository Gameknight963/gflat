using System.Diagnostics;

namespace gflat.Tests;

public sealed class TemporaryIrCleanupTests
{
    [Fact]
    public async Task LockedTemporaryIrReportsCleanupWarningAndPreservesUserExitCode()
    {
        // Unix permits unlinking open files; the sharing violation is Windows-specific.
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "gflat-ir-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "main.gf");
            File.WriteAllText(source, """
                extern int puts(readonly(char)* text) nothrow;
                extern int getchar() nothrow;
                extern int fflush(void*? stream) nothrow;
                int main() nothrow {
                    puts("READY"); fflush(null);
                    getchar();
                    return 42;
                }
                """);
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string argument in new[] { typeof(Program).Assembly.Location, source,
                "--no-stdlib", "-o", Path.Combine(directory, "program.exe"), "--run" })
                start.ArgumentList.Add(argument);
            // Change only this child process; parallel compiler tests keep their own temp root.
            start.Environment["TEMP"] = directory;
            start.Environment["TMP"] = directory;
            start.Environment["TMPDIR"] = directory;
            using var process = Process.Start(start)!;
            Task<string> error = process.StandardError.ReadToEndAsync();
            try
            {
                string? ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(90));
                Assert.Equal("READY", ready);
                string ir = Assert.Single(Directory.GetFiles(directory, "gflat_*.ll"));
                // Reproduce an external reader that allows reads but denies file deletion.
                using (var held = new FileStream(ir, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await process.StandardInput.WriteLineAsync();
                    process.StandardInput.Close();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                    Assert.Equal(42, process.ExitCode);
                    Assert.True(File.Exists(ir));
                    string diagnostic = await error;
                    Assert.Contains("Warning: could not delete temporary IR", diagnostic);
                    Assert.Contains(ir, diagnostic);
                    Assert.DoesNotContain("Internal compiler error", diagnostic);
                }
                Assert.Empty(await process.StandardOutput.ReadToEndAsync());
            }
            finally
            {
                if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}

using System.Diagnostics;
using System.Text.Json;

namespace gflat.Tests;

public sealed class PipelineTimingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedCompilerProcessFlushesStageTimingsAndOptionalAllocations(bool allocations)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat timing " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "main.gf");
            string traces = Path.Combine(root, "traces");
            File.WriteAllText(source, "int main() => 0;");
            var start = new ProcessStartInfo("dotnet") {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string argument in new[] { typeof(Program).Assembly.Location, source, "--check", "--no-stdlib" })
                start.ArgumentList.Add(argument);
            start.Environment["GFLAT_PROFILE_DIRECTORY"] = traces;
            start.Environment["GFLAT_PROFILE_ALLOCATIONS"] = allocations ? "1" : "0";
            start.Environment["GFLAT_PROFILE_DETAIL"] = "0";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("Compiler timing probe exceeded 30 seconds");
            }
            Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
            var events = Directory.GetFiles(traces, "*.jsonl").SelectMany(File.ReadLines)
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
            Assert.Contains(events, item => item.GetProperty("stage").GetString() == "frontend.check");
            var body = Assert.Single(events, item => item.GetProperty("stage").GetString() == "check.bodies");
            Assert.True(body.GetProperty("milliseconds").GetDouble() >= 0);
            if (allocations) Assert.True(body.GetProperty("allocatedBytes").GetInt64() > 0);
            else Assert.Equal(JsonValueKind.Null, body.GetProperty("allocatedBytes").ValueKind);
            Assert.DoesNotContain(events, item => item.GetProperty("stage").GetString() == "check.method");
        }
        finally { Directory.Delete(root, true); }
    }
}

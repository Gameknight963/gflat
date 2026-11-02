using System.Diagnostics;
using System.Text.Json;

namespace gflat;

// Opt-in process-local tracing for compiler and test performance investigations.
public static class PipelineTimings
{
    private static readonly string? DirectoryPath = Environment.GetEnvironmentVariable("GFLAT_PROFILE_DIRECTORY");
    private static readonly string? Pass = Environment.GetEnvironmentVariable("GFLAT_PROFILE_PASS");
    private static readonly object Gate = new();
    private static readonly AsyncLocal<string?> Context = new();
    private static StreamWriter? writer;
    private static int pending;

    public static bool Enabled => !string.IsNullOrWhiteSpace(DirectoryPath);
    public static string? TestName
    {
        get => Context.Value ?? Environment.GetEnvironmentVariable("GFLAT_PROFILE_TEST");
        set => Context.Value = value;
    }

    public static IDisposable? Measure(string stage, string? detail = null)
        => Enabled ? new Measurement(stage, detail, TestName) : null;

    private sealed class Measurement(string stage, string? detail, string? test) : IDisposable
    {
        private readonly long start = Stopwatch.GetTimestamp();
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            string json = JsonSerializer.Serialize(new { stage, detail, test, milliseconds, pass = Pass, process = Environment.ProcessId });
            lock (Gate)
            {
                if (writer == null)
                {
                    Directory.CreateDirectory(DirectoryPath!);
                    string file = Path.Combine(DirectoryPath!, $"pipeline-{Environment.ProcessId}-{Guid.NewGuid():N}.jsonl");
                    writer = new StreamWriter(file);
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => { lock (Gate) { writer?.Dispose(); writer = null; } };
                }
                writer.WriteLine(json);
                // Bound cancellation loss without flushing every measurement.
                if (++pending >= 128) { writer.Flush(); pending = 0; }
            }
        }
    }
}

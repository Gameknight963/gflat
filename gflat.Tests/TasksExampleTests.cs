using System.Diagnostics;
using System.Text;

namespace gflat.Tests;

public sealed class TasksExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualTaskToolUsesOwningTitlesAndCountedConsoleLines(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("tasks.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/tasks.gf"))));
        string directory = Path.Combine(Path.GetTempPath(), "gflat-tasks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "tasks.ll"), executable = Path.Combine(directory, "tasks.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            const string banner = "Tasks (in memory). Commands: add <title>, list, done <number>, quit\n";
            foreach (string separator in new[] { "\n", "\r\n" })
            {
                string[] commands = ["list", "add ", "add first", "add second", "done 2", "list",
                    "done 0", "done 3", "done 2147483648", "done 1x", "listx", "", "quit", "add ignored"];
                string expected = banner + "No tasks yet.\nA task needs a title.\nAdded.\nAdded.\nDone.\n" +
                    "1. [ ] first\n2. [x] second\n" +
                    string.Concat(Enumerable.Repeat("Use done <number> with an existing task number.\n", 4)) +
                    "Unknown command. Use add <title>, list, done <number>, or quit.\n";
                await AssertSession(executable, string.Join(separator, commands) + separator, expected);
            }
            // Grow the native vector and reuse the input String repeatedly. A final
            // command without LF still runs; title bytes must outlive that buffer.
            string longTitle = new('x', 10000);
            string session = "add " + longTitle + "\n" +
                string.Concat(Enumerable.Range(0, 20).Select(index => $"add item {index}\n")) + "done 1\nlist";
            string rows = "1. [x] " + longTitle + "\n" +
                string.Concat(Enumerable.Range(0, 20).Select(index => $"{index + 2}. [ ] item {index}\n"));
            await AssertSession(executable, session, banner + string.Concat(Enumerable.Repeat("Added.\n", 21)) + "Done.\n" + rows);
            // Input/output uses counted String APIs. Embedded NUL is title data,
            // and a bare CR within a line is retained rather than discarded.
            await AssertSession(executable, "add a\0b\rc\nlist\n", banner + "Added.\n1. [ ] a\0b\rc\n");
            await AssertSession(executable, "quit\0junk\nlist\0junk\nlist\nquit\nadd ignored\n",
                banner + string.Concat(Enumerable.Repeat("Unknown command. Use add <title>, list, done <number>, or quit.\n", 2)) + "No tasks yet.\n");
            await AssertSession(executable, "", banner);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task AssertSession(string executable, string input, string expected)
    {
        using var process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        using var output = new MemoryStream();
        var read = process.StandardOutput.BaseStream.CopyToAsync(output);
        var errors = process.StandardError.ReadToEndAsync();
        var write = Task.Run(async () =>
        {
            await process.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(input));
            process.StandardInput.Close();
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Assert.Fail("Task example timed out.");
        }
        await write; await read;
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(await errors);
        if (OperatingSystem.IsWindows()) expected = expected.Replace("\n", "\r\n");
        Assert.Equal(Encoding.UTF8.GetBytes(expected), output.ToArray());
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace gflat.Tests;

public sealed class ClosestExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolRanksBorrowedArgumentsAndPreservesAllClosestTies(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("closest.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/closest.gf"))));
        string directory = Path.Combine(Path.GetTempPath(), "gflat-closest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "main.ll");
            string executable = Path.Combine(directory, "closest.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            List<(string Query, string[] Candidates, int[] Distances)> cases =
            [
                ("", [""], [0]),
                ("", ["a", "bbb", "", ""], [1, 3, 0, 0]),
                ("a", ["b", "aa", "a", "a"], [1, 1, 0, 0]),
                ("book", ["back", "boon", "books", "boon"], [2, 1, 1, 1]),
                ("ab", ["ba"], [2]),
                ("cat", ["CAT", "cats", "cut"], [3, 1, 1]),
                ("-", ["--", "x", "-"], [1, 1, 0]),
                ("a b", ["a\tb", "a  b"], [1, 1]),
                ("Case", ["case"], [1]),
                ("abc", ["zzz", "ab", "abc"], [3, 1, 0]),
                ("abcdef", ["abXdef", "abcde", "abXdef", "abcdefX"], [1, 1, 1, 1]),
                (new string('x', 256), ["x", "xx", "xxx"], [255, 254, 253]),
                ("x", [new string('x', 256), "xx", ""], [255, 1, 1]),
                (new string('a', 128), [new string('a', 127) + "b", new string('a', 64), new string('a', 128)], [1, 64, 0]),
                ("kitten", ["sitting"], [3])
            ];
            var random = new Random(901837);
            string Word() => new(Enumerable.Range(0, random.Next(0, 4))
                .Select(_ => random.Next(2) == 0 ? 'a' : 'b').ToArray());
            for (int sample = 0; sample < 6; sample++)
            {
                string query = Word();
                string[] candidates = Enumerable.Range(0, 5).Select(_ => Word()).ToArray();
                cases.Add((query, candidates, candidates.Select(candidate => BreadthFirstDistance(query, candidate)).ToArray()));
            }
            foreach (var row in cases)
            {
                var result = await Run(executable, [row.Query, .. row.Candidates]);
                Assert.True(result.ExitCode == 0, result.Error);
                Assert.Empty(result.Error);
                int minimum = row.Distances.Min();
                string expected = string.Concat(row.Candidates.Select((candidate, index) =>
                    row.Distances[index] == minimum
                        ? minimum.ToString(CultureInfo.InvariantCulture) + "\t" + candidate + Environment.NewLine
                        : ""));
                Assert.Equal(Encoding.ASCII.GetBytes(expected), result.Output);
            }
            foreach (string[] arguments in new[] { Array.Empty<string>(), new[] { "query-only" } })
            {
                var result = await Run(executable, arguments);
                Assert.Equal(2, result.ExitCode);
                Assert.Empty(result.Output);
                Assert.Equal("Usage: closest <query> <candidate> [more candidates...]" + Environment.NewLine, result.Error);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolReportsImmediateAndBufferedOutputFailures(int optimization)
        => ExampleOutputFailure.AssertReportedWithArguments("closest", optimization,
            "closest: Unable to compare candidates or write output",
            "closest: Unable to compare candidates or write output", "book", "boon", "books");

    private static async Task<(int ExitCode, byte[] Output, string Error)> Run(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var output = new MemoryStream();
        Task copied = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await Task.WhenAll(copied, error, process.WaitForExitAsync(timeout.Token)); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        return (process.ExitCode, output.ToArray(), await error);
    }

    // Only short {a,b} words use this graph search; the larger cases have fixed oracles.
    private static int BreadthFirstDistance(string first, string second)
    {
        Assert.InRange(first.Length, 0, 3);
        Assert.InRange(second.Length, 0, 3);
        Assert.All(first.Concat(second), value => Assert.True(value is 'a' or 'b'));
        if (first == second) return 0;
        int bound = Math.Max(first.Length, second.Length);
        var pending = new Queue<(string Value, int Depth)>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { first };
        pending.Enqueue((first, 0));
        while (pending.TryDequeue(out var current))
        {
            if (current.Depth == bound) continue;
            foreach (string next in Neighbors(current.Value))
            {
                if (!seen.Add(next)) continue;
                int depth = current.Depth + 1;
                if (next == second) return depth;
                pending.Enqueue((next, depth));
            }
        }
        throw new InvalidOperationException("Constructive edit bound did not reach target");
    }

    private static IEnumerable<string> Neighbors(string value)
    {
        for (int index = 0; index <= value.Length; index++)
        foreach (char symbol in new[] { 'a', 'b' })
            yield return value.Insert(index, symbol.ToString());
        for (int index = 0; index < value.Length; index++)
        {
            yield return value.Remove(index, 1);
            char replacement = value[index] == 'a' ? 'b' : 'a';
            yield return value[..index] + replacement + value[(index + 1)..];
        }
    }
}

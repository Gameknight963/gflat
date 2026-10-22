using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardLibraryGuideTests
{
    public static IEnumerable<object[]> CompletePrograms()
    {
        int count = 0;
        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Guides"), "*.md").Order(StringComparer.Ordinal))
        {
            int index = 0;
            foreach (var example in Programs(File.ReadAllText(path)))
            {
                yield return [Path.GetFileName(path), index++];
                count++;
            }
        }
        if (count == 0) throw new InvalidOperationException("No complete standard-library guide programs were copied into the test output.");
    }

    [Theory]
    [MemberData(nameof(CompletePrograms))]
    public void CompleteGuideProgramCompiles(string guide, int index)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Guides", guide);
        var example = Programs(File.ReadAllText(path)).ElementAt(index);
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(file => new SourceFile(file, File.ReadAllText(file)));
        // Retain the guide's line numbers in compiler errors. Only complete
        // programs with an entry point are checked; contextual fragments remain
        // separate, and the language specification has its own test harness.
        string code = new string('\n', example.Line - 1) + example.Code;
        _ = Compiler.Emit(sources.Append(new SourceFile(path, code)), options: new() { NoStdLib = true });
    }

    private static IEnumerable<(string Code, int Line)> Programs(string document)
    {
        foreach (Match match in Regex.Matches(document, @"(?ms)^```csharp[^\r\n]*\r?\n(?<code>.*?)^```"))
        {
            Group code = match.Groups["code"];
            if (!Regex.IsMatch(code.Value, @"\bint\s+main\s*\(")) continue;
            int line = 1 + document[..code.Index].Count(character => character == '\n');
            yield return (code.Value, line);
        }
    }
}

using System.Text.RegularExpressions;

namespace gflat.Tests;

[Trait("SeparateShards", "Documentation")]
public sealed class StandardLibraryGuideTests
{
    private static readonly Lazy<SourceFile[]> Library = new(() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(file => new SourceFile(file, File.ReadAllText(file))).ToArray());

    public static IEnumerable<object[]> CompletePrograms(int partition)
    {
        if (partition is < 0 or >= 4) throw new ArgumentOutOfRangeException(nameof(partition));
        int count = 0;
        string directory = Path.Combine(AppContext.BaseDirectory, "Guides");
        foreach (string path in Directory.GetFiles(directory, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            int index = 0;
            foreach (var example in Programs(File.ReadAllText(path)))
            {
                if (count++ % 4 == partition) yield return [Path.GetRelativePath(directory, path), index];
                index++;
            }
        }
        if (count == 0) throw new InvalidOperationException("No complete standard-library guide programs were copied into the test output.");
    }

    [Theory]
    [MemberData(nameof(CompletePrograms), 0)]
    [Trait("Optimization", "Independent")]
    [Trait("EstimatedCases", "50")]
    public void CompleteGuideProgramCompiles(string guide, int index)
        => CheckProgram(guide, index);

    internal static void CheckProgram(string guide, int index)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Guides", guide);
        string document = File.ReadAllText(path);
        var example = Programs(document).ElementAt(index);
        var headers = new Dictionary<string, SourceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(document,
            @"(?ms)^`(?<name>[^`\r\n]+\.gfh)`:\s*\r?\n```(?:cpp|csharp)\r?\n(?<code>.*?)^```"))
        {
            string header = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, match.Groups["name"].Value));
            headers.Add(header, new SourceFile(header, match.Groups["code"].Value));
        }
        // Retain the guide's line numbers in compiler errors. Only complete
        // programs with an entry point are checked; contextual fragments remain
        // separate, and the language specification has its own test harness.
        string code = new string('\n', example.Line - 1) + example.Code;
        _ = Compiler.Emit(Library.Value.Append(new SourceFile(path, code)), options: new()
        {
            NoStdLib = true,
            IncludeLoader = header => headers.GetValueOrDefault(header)
        });
    }

    private static IEnumerable<(string Code, int Line)> Programs(string document)
    {
        foreach (Match match in Regex.Matches(document, @"(?ms)^```(?:csharp|cpp)[^\r\n]*\r?\n(?<code>.*?)^```"))
        {
            Group code = match.Groups["code"];
            if (!Regex.IsMatch(code.Value, @"\bint\s+main\s*\(")) continue;
            int line = 1 + document[..code.Index].Count(character => character == '\n');
            yield return (code.Value, line);
        }
    }
}

[Trait("SeparateShards", "Documentation")]
public sealed class StandardLibraryGuidePartition1Tests
{
    [Theory]
    [MemberData(nameof(StandardLibraryGuideTests.CompletePrograms), 1, MemberType = typeof(StandardLibraryGuideTests))]
    [Trait("Optimization", "Independent")]
    [Trait("EstimatedCases", "50")]
    public void CompleteGuideProgramCompiles(string guide, int index)
        => StandardLibraryGuideTests.CheckProgram(guide, index);
}

[Trait("SeparateShards", "Documentation")]
public sealed class StandardLibraryGuidePartition2Tests
{
    [Theory]
    [MemberData(nameof(StandardLibraryGuideTests.CompletePrograms), 2, MemberType = typeof(StandardLibraryGuideTests))]
    [Trait("Optimization", "Independent")]
    [Trait("EstimatedCases", "50")]
    public void CompleteGuideProgramCompiles(string guide, int index)
        => StandardLibraryGuideTests.CheckProgram(guide, index);
}

[Trait("SeparateShards", "Documentation")]
public sealed class StandardLibraryGuidePartition3Tests
{
    [Theory]
    [MemberData(nameof(StandardLibraryGuideTests.CompletePrograms), 3, MemberType = typeof(StandardLibraryGuideTests))]
    [Trait("Optimization", "Independent")]
    [Trait("EstimatedCases", "50")]
    public void CompleteGuideProgramCompiles(string guide, int index)
        => StandardLibraryGuideTests.CheckProgram(guide, index);
}

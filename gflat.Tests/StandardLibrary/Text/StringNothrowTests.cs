using System.Text.RegularExpressions;

namespace gflat.Tests.Std.Text;

public class StringNothrowTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedStringMetadataComparisonAndTryParsingUseDirectCallsWithoutCleanupBoundaries(int optimization)
    {
        var types = new[]
        {
            ("Byte", "byte"), ("SByte", "sbyte"), ("Short", "short"), ("UShort", "ushort"),
            ("Int", "int"), ("UInt", "uint"), ("Long", "long"), ("ULong", "ulong"),
            ("NInt", "nint"), ("NUInt", "nuint")
        };
        string checks = string.Join("\n", types.Select((type, index) => $$"""
            {{type.Item2}} value{{index}} = ({{type.Item2}})0;
            if (!text.TryParse{{type.Item1}}(&value{{index}}) || value{{index}} != ({{type.Item2}})42) return 10;
            """));
        string failureChecks = string.Join("\n", types.Select((type, index) => $"if (text.TryParse{type.Item1}(&value{index}) || value{index} != ({type.Item2})0) return 12;"));
        string code = $$"""
            using Standard; using Standard::Text;
            int Probe(ref String text, immutable(String)* other) {
                if (text.Length != 2 || text.Capacity < 2 || text.IsEmpty || text.Data[0] != '4') return 1;
                if (text.ToCString() != text.Data || !text.Equals(other) || text.CompareTo(other) != 0) return 2;
                if (text.GetHashCode() != other.GetHashCode()) return 3;
                if (!text.StartsWith("4") || !text.StartsWith("42", 2) || !text.EndsWith("2") || !text.EndsWith("42", 2)) return 4;
                {{checks}}
                text.Replace('4', '5');
                if (text.Data[0] != '5') return 5;
                text.Clear();
                if (!text.IsEmpty || text.Length != 0 || text.Data[0] != '\0') return 6;
                {{failureChecks}}
                return 0;
            }
            int main() {
                String text = s"42"; String other = s"42";
                int result = Probe(ref text, &other);
                if (result != 0) return result;
                String empty = new();
                empty.Clear(); empty.Replace('a','b');
                if (!empty.IsEmpty || empty.Length != 0 || empty.Capacity != 0 || empty.Data[0] != '\0') return 11;
                return 0;
            }
            """;
        var sources = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Standard/core"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("main.gf", code));
        string ir = Compiler.Emit(sources);
        string body = Regex.Match(ir, @"(?ms)^define [^\r\n]*@gflat\$Probe\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);
        Assert.DoesNotContain("invoke", body);
        Assert.DoesNotContain("__gflat_eh_", body);
        Assert.Contains("ToCString", body);
        Match parserWrapper = Assert.Single(Regex.Matches(ir, @"(?ms)^(?<definition>define [^\r\n]*@(?<name>[^\s(]+)\([^\r\n]*\{)\r?\n(?<body>.*?)^\}").Cast<Match>(),
            match => match.Groups["name"].Value.Contains("$extension$", StringComparison.Ordinal)
                && body.Contains("@" + match.Groups["name"].Value + "(", StringComparison.Ordinal)
                && match.Groups["body"].Value.Contains("$TryParseInt", StringComparison.Ordinal));
        Assert.Contains(" nounwind", parserWrapper.Groups["definition"].Value);
        Assert.DoesNotContain("invoke", parserWrapper.Groups["body"].Value);
        Assert.Contains("Length", body);

        string[] definitions = Regex.Matches(ir, @"(?m)^define [^\r\n]+").Select(match => match.Value).ToArray();
        foreach (string member in new[] { "ToCString", "GetHashCode", "CompareTo", "Equals", "Clear", "StartsWith", "EndsWith" })
        {
            string[] methods = definitions.Where(line => line.Contains("$String$") && line.Contains("$" + member)).ToArray();
            Assert.NotEmpty(methods);
            Assert.All(methods, line => Assert.Contains("nounwind", line));
        }

        string directory = Path.Combine(Path.GetTempPath(), "gflat-string-nothrow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "strings.ll"), output = Path.Combine(directory, "strings.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(0, NativeToolchain.Run(output, []).ExitCode);
        }
        finally { Directory.Delete(directory, true); }
    }
}

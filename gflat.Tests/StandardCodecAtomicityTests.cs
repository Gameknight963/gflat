using System.Text;

namespace gflat.Tests;

public sealed class StandardCodecAtomicityTests
{
    private static readonly byte[] Payload = Enumerable.Range(0, 255).Select(value => (byte)value).ToArray();

    private static (string Name, string Encoded, string[] Suffixes)[] Cases() =>
    [
        ("Base64", Convert.ToBase64String(Payload), ["AA=A", "AB==", "AAB=", "AAA!", "A"]),
        ("Hex", Convert.ToHexString(Payload), ["GG", "0G", "G0", "A"]),
        ("PercentEncoding", string.Concat(Payload.Select(value =>
            value is >= 65 and <= 90 or >= 97 and <= 122 or >= 48 and <= 57 or 45 or 46 or 95 or 126
                ? ((char)value).ToString() : "%" + value.ToString("X2"))), ["%", "%0", "%GG", "%0G", "%G0", "%FF%"])
    ];

    private static void Run(string code, int optimization)
    {
        var sources = new[] { "Encoding/Base64.gf", "Encoding/Hex.gf", "Encoding/PercentEncoding.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private static string Buffer(string encoded) => $$"""
        char[{{encoded.Length + 2}}] buffer;
        buffer[0] = '!'; buffer[{{encoded.Length + 1}}] = '?';
        for (nuint i = 0; i < {{encoded.Length}}; i++) buffer[i + 1] = "{{encoded}}"[i];
        nuint written = 99;
        """;

    private static string CheckUnchanged(string encoded) => $$"""
        if (written != 0 || buffer[0] != '!' || buffer[{{encoded.Length + 1}}] != '?') return 2;
        for (nuint i = 0; i < {{encoded.Length}}; i++) if (buffer[i + 1] != "{{encoded}}"[i]) return 3;
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryInsufficientInPlaceCapacityPreservesEncodedBytesAndGuards(int optimization)
    {
        var code = new StringBuilder("using std; int main() {\n");
        foreach (var item in Cases())
        {
            code.AppendLine("{").AppendLine(Buffer(item.Encoded));
            code.AppendLine($$"""
                for (nuint capacity = 0; capacity < {{Payload.Length}}; capacity++) {
                    written = 99;
                    if ({{item.Name}}::TryDecode(&buffer[1], {{item.Encoded.Length}}, &buffer[1], capacity, ref written)) return 1;
                    {{CheckUnchanged(item.Encoded)}}
                }
                """);
            code.AppendLine("}");
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MalformedSuffixAfterLongValidPrefixPreservesInPlaceInputAndGuards(int optimization)
    {
        var code = new StringBuilder("using std; int main() {\n");
        foreach (var item in Cases())
        foreach (string suffix in item.Suffixes)
        {
            string encoded = item.Encoded + suffix;
            code.AppendLine("{").AppendLine(Buffer(encoded));
            code.AppendLine($$"""
                if ({{item.Name}}::TryGetDecodedLength(&buffer[1], {{encoded.Length}}, ref written)) return 1;
                {{CheckUnchanged(encoded)}}
                written = 99;
                if ({{item.Name}}::TryDecode(&buffer[1], {{encoded.Length}}, &buffer[1], {{encoded.Length}}, ref written)) return 1;
                {{CheckUnchanged(encoded)}}
                """);
            code.AppendLine("}");
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }
}

using System.Text.RegularExpressions;

namespace gflat.Tests;

public class StandardNumericNothrowTests
{
    private static readonly (string Suffix, string Type)[] IntegerTypes =
    [
        ("Byte", "byte"), ("SByte", "sbyte"), ("Short", "short"), ("UShort", "ushort"),
        ("Int", "int"), ("UInt", "uint"), ("Long", "long"), ("ULong", "ulong"),
        ("NInt", "nint"), ("NUInt", "nuint"), ("ExtraLong", "extralong")
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NonthrowingNumericOperationsUseDirectCallsWithoutBoundaryHandlers(int optimization)
    {
        string checks = string.Join("\n", IntegerTypes.Select((type, index) => $$"""
            {{type.Type}} value{{index}} = ({{type.Type}})99;
            if (!std::TryParse{{type.Suffix}}("42", &value{{index}}) || value{{index}} != ({{type.Type}})42) return 1;
            if (!std::TryParse{{type.Suffix}}("+42suffix", 3, &value{{index}}) || value{{index}} != ({{type.Type}})42) return 2;
            if (std::TryParse{{type.Suffix}}("bad", &value{{index}}) || value{{index}} != ({{type.Type}})0) return 3;
            if (std::TryParse{{type.Suffix}}("", 0, &value{{index}}) || value{{index}} != ({{type.Type}})0) return 4;
            if (std::Numerics::Math::Min(({{type.Type}})7, ({{type.Type}})4) != ({{type.Type}})4) return 5;
            if (std::Numerics::Math::Max(({{type.Type}})7, ({{type.Type}})4) != ({{type.Type}})7) return 6;
            """));
        string program = $$"""
            int main() {
                {{checks}}
                if (!std::Numerics::Math::IsFinite(1.0f) || !std::Numerics::Math::IsFinite(1.0)) return 7;
                if (std::Numerics::Math::IsNaN(1.0f) || std::Numerics::Math::IsNaN(1.0)) return 8;
                if (std::Numerics::Math::Abs(-2.0f) != 2.0f || std::Numerics::Math::Abs(-2.0) != 2.0) return 9;
                if (std::Numerics::Math::Min(1.0f, 2.0f) != 1.0f || std::Numerics::Math::Max(1.0f, 2.0f) != 2.0f) return 10;
                if (std::Numerics::Math::Min(1.0, 2.0) != 1.0 || std::Numerics::Math::Max(1.0, 2.0) != 2.0) return 11;
                extralong wide = (extralong)1 << 100;
                if (std::Numerics::Math::Min(wide, wide + (extralong)1) != wide) return 12;
                if (std::Numerics::Math::Max(wide, wide + (extralong)1) != wide + (extralong)1) return 13;
                return 0;
            }
            """;
        var sources = new[] { "Parsing.gf", "IntegerParsing.gf", "Numerics/Math.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", program));
        string ir = Compiler.Emit(sources);
        string main = Regex.Match(ir, @"(?ms)^define [^\r\n]*@main\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(main);
        Assert.DoesNotContain("invoke", main);
        Assert.DoesNotContain("__gflat_eh_", main);
        Assert.Contains("call zeroext i1 @gflat$std$TryParseInt", main);
        Assert.Contains("call zeroext i1 @gflat$std$TryParseExtraLong", main);
        Assert.Matches(@"call i32 @[^\r\n ]*\$Math\$Min", main);

        string[] definitions = Regex.Matches(ir, @"(?m)^define [^\r\n]+").Select(match => match.Value).ToArray();
        string[] attempts = definitions.Where(line => line.Contains("$TryParse")).ToArray();
        Assert.Equal(22, attempts.Length);
        Assert.All(attempts, line => Assert.Contains("nounwind", line));
        string[] helpers = definitions.Where(line => line.Contains("$integer_parsing_detail$")).ToArray();
        Assert.Equal(2, helpers.Length);
        Assert.All(helpers, line => Assert.Contains("nounwind", line));
        string[] pureMath = definitions.Where(line => Regex.IsMatch(line, @"\$Math\$(Min|Max|IsNaN|IsFinite)\$") || line.Contains("$Math$Abs$") && Regex.IsMatch(line, @"\((float|double) ")).ToArray();
        Assert.Equal(32, pureMath.Length);
        Assert.All(pureMath, line => Assert.Contains("nounwind", line));
        string[] throwing = definitions.Where(line => Regex.IsMatch(line, @"\$(Parse\w+|Clamp)\$") || line.Contains("$Math$Abs$") && Regex.IsMatch(line, @"\(i(8|16|32|64|128) ")).ToArray();
        Assert.NotEmpty(throwing);
        Assert.All(throwing, line => Assert.DoesNotContain("nounwind", line));

        string directory = Path.Combine(Path.GetTempPath(), "gflat-numeric-nothrow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "numeric.ll"), output = Path.Combine(directory, "numeric.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(0, NativeToolchain.Run(output, []).ExitCode);
        }
        finally { Directory.Delete(directory, true); }
    }
}

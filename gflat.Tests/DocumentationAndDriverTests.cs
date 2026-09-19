using System.Text.RegularExpressions;

namespace gflat.Tests;

public class DocumentationAndDriverTests
{
    [Fact]
    public void EveryNormativeExampleCompiles()
    {
        string document = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LANGUAGE_SPEC.md"));
        var examples = Regex.Matches(document, @"```gflat\r?\n(.*?)```", RegexOptions.Singleline);
        Assert.NotEmpty(examples);
        foreach (Match example in examples) Compiler.Emit(example.Groups[1].Value);
    }

    [Fact]
    public void CliUsesSourceOutputAndExplicitRun()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gflat_cli_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string source = Path.Combine(dir, "source with spaces.gf");
            string ir = Path.Combine(dir, "result.ll");
            string executable = Path.Combine(dir, "result.exe");
            File.WriteAllText(source, "int main() { return 7; }");
            Assert.Equal(0, Program.Main([source, "--emit-ir", "-o", ir]));
            Assert.Contains("define i32 @main", File.ReadAllText(ir));
            Assert.Equal(0, Program.Main([source, "-o", executable]));
            Assert.Equal(7, Program.Main([source, "-o", executable, "--run"]));
            Assert.Equal(2, Program.Main([source, "--target", "i386-pc-windows-msvc"]));
            Assert.Equal(2, Program.Main([source, "-o", source]));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("/*", "Unterminated")]
    [InlineData("1__2", "separator")]
    [InlineData("0x", "hexadecimal")]
    [InlineData("1.0u", "Integer suffix")]
    [InlineData("\"a\nb\"", "Newline")]
    public void MalformedLexemesHaveSourceErrors(string source, string message)
    {
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Lexer.Tokenize(source));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NumericSeparatorsAndColumns()
    {
        Assert.Equal("1000", Lexer.Tokenize("1_000")[0].Text);
        var token = Lexer.Tokenize("/* comment\n */\n  int")[0];
        Assert.Equal(3, token.Line);
        Assert.Equal(3, token.Column);
        Assert.Equal(3, token.Span.Length);
    }
}

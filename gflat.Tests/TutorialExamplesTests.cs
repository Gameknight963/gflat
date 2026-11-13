using System.Text.RegularExpressions;
using gflat.CompileExceptions;

namespace gflat.Tests;

[Trait("SeparateShards", "Documentation")]
public sealed class TutorialExamplesTests
{
    [Fact]
    [Trait("Optimization", "Independent")]
    [Trait("EstimatedCases", "40")]
    public void CompleteProgramsCompileAndDeliberateErrorsRemainErrors()
    {
        string tutorial = Path.Combine(AppContext.BaseDirectory, "Tutorial");
        string standard = Path.Combine(AppContext.BaseDirectory, "Standard");
        var library = Directory.EnumerateFiles(standard, "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToArray();
        var failures = new List<string>();
        int checkedPrograms = 0;
        foreach (string path in Directory.EnumerateFiles(tutorial, "*.md", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path);
            int example = 0;
            foreach (Match block in Regex.Matches(text, @"```(?:cpp|csharp|cs|c|gflat)\r?\n(?<code>.*?)```", RegexOptions.Singleline))
            {
                example++;
                string code = block.Groups["code"].Value;
                if (!Regex.IsMatch(code, @"\bint\s+main\s*\(")) continue;
                checkedPrograms++;
                bool deliberateError = Regex.IsMatch(code, @"//[^\r\n]*(?:compile error|not allowed|\berror\b)", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(code, @"DoSomething\(move input\);\s*DoSomething\(move input\);");
                // These short examples continue declarations introduced earlier in their chapter.
                if (code.Contains("String") && !code.Contains("using Standard::Text;")) code = "using Standard::Text;\n\n" + code;
                if (code.Contains("GetSomething()") && !code.Contains("int GetSomething()")) code += "\nint GetSomething() => 7;";
                if (code.Contains("DoSomething(") && !code.Contains("void DoSomething(")) code += "\nvoid DoSomething(String value) { Console::WriteLine(ref value); }";
                string label = Path.GetRelativePath(tutorial, path) + " example " + example;
                try
                {
                    Compiler.Check(library.Append(new SourceFile(label + ".gf", code)), options: new() { NoStdLib = true });
                    if (deliberateError) failures.Add(label + ": expected a diagnostic");
                }
                catch (TypeCheckException error)
                {
                    if (!deliberateError) failures.Add(label + ": " + error.Message);
                }
            }
        }
        Assert.True(checkedPrograms >= 40, "The complete tutorial examples must remain covered.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}

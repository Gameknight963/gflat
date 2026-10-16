using gflat.diagnostics;

namespace gflat.Tests;

public sealed class ConstantCallDiagnosticLabelTests
{
    [Theory]
    [InlineData("namespace Lib{extern int Read();} const int value=Lib::Read(); int main()=>0;", "Cannot call 'Lib::Read' at compile time", "const int value=Lib::Read();")]
    [InlineData("namespace Lib{namespace Nested{extern int Read();}} const int value=Lib::Nested::Read(); int main()=>0;", "Cannot call 'Lib::Nested::Read' at compile time", "const int value=Lib::Nested::Read();")]
    [InlineData("using Lib; namespace Lib{extern int Read();} const int value=Read(); int main()=>0;", "Cannot call 'Lib::Read' at compile time", "const int value=Read();")]
    [InlineData("extern int Read(); const int value=Read(); int main()=>0;", "Cannot call 'Read' at compile time", "const int value=Read();")]
    [InlineData("namespace Lib{int Read()=>1;} const int value=Lib::Read(); int main()=>0;", "Function 'Read' is not marked 'const' and cannot be evaluated at compile time", "const int value=Lib::Read();")]
    [InlineData("namespace Lib{int Read<T>()=>1;} const int value=Lib::Read<int>(); int main()=>0;", "Function 'Lib::Read<int>' is not marked 'const' and cannot be evaluated at compile time", "const int value=Lib::Read<int>();")]
    public void ConstantCallFailuresNameTheSelectedDeclaration(string source, string detail, string offending)
    {
        var diagnostics = Compiler.Analyze([new SourceFile("constant-call-label.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("Const variable 'value' initializer must be a compile-time constant: " + detail, error.Message);
        Assert.Equal("constant-call-label.gf", error.FilePath);
        Assert.Equal(offending, source.Substring(error.Span.Start, error.Span.Length));
    }
}

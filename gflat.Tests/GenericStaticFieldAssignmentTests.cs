using gflat.diagnostics;

namespace gflat.Tests;

public sealed class GenericStaticFieldAssignmentTests
{
    [Theory]
    [InlineData("class", false)]
    [InlineData("struct", false)]
    [InlineData("class", true)]
    [InlineData("struct", true)]
    public void DirectWritesPreserveSpecializationIdentityAndEvaluateTheValueOnce(string kind, bool compound)
    {
        string operation = compound ? "+=" : "=";
        string source = kind + " Box<T>{public static T value;}alias IntBox=Box<int>;" +
            "int calls;int Input(){calls++;return 42;}" +
            "int main(){Box<int>::value" + operation + "Input();if(calls!=1)return 1;" +
            "Box<long>::value=17;if(Box<int>::value!=42 || Box<long>::value!=17)return 2;" +
            "Box<int>::value++;IntBox::value+=1;return Box<int>::value-2;}";
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void QualifiedWritesAcceptConstantExpressions(string kind)
        => Assert.Equal(42, CompilerTestHelper.Run(kind + " Box<T>{public static T value;}const int Answer()=>40;" +
            "int main(){Box<int>::value=Answer();Box<int>::value+=2;return Box<int>::value;}").ExitCode);

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void InstanceFieldsAreStillRejected(string kind)
        => AssertError(kind + " Box<T>{public T value;}int main(){Box<int>::value=42;return 0;}", "Instance field 'value' must be accessed through an object");

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void PrivateStaticFieldsKeepTheirAccessibility(string kind)
        => AssertError(kind + " Box<T>{private static T value;}int main(){Box<int>::value=42;return 0;}", "Cannot access private field 'value'");

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void ReadonlyStaticFieldsRemainProtected(string kind)
        => AssertError(kind + " Box<T>{public static readonly(T) value;}int main(){Box<int>::value=42;return 0;}", "readonly");

    [Fact]
    public void UnknownGenericTypeQualifiersProduceSourceErrors()
        => AssertError("int main(){Missing<int>::value=42;return 0;}", "Unknown generic type 'Missing'");

    private static void AssertError(string source, string expected)
    {
        var errors = Compiler.Analyze([new SourceFile("generic-static-write.gf", source)], new() { NoStdLib = true });
        Assert.Contains(errors, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }
}

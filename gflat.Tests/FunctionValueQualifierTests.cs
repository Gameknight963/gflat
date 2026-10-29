using gflat.diagnostics;

namespace gflat.Tests;

public sealed class FunctionValueQualifierTests
{
    public static IEnumerable<object[]> ParameterMismatches()
    {
        foreach (string shape in new[] { "readonly-scalar", "readonly-pointer", "immutable-root",
            "immutable-pointee", "reference", "readonly-pointee", "mutable-implementation" })
        foreach (bool overloaded in new[] { false, true })
        foreach (bool address in new[] { false, true })
            yield return [shape, overloaded, address];
    }

    [Theory]
    [MemberData(nameof(ParameterMismatches))]
    public void ParameterQualifiersMustMatchWithOrWithoutAnUnrelatedOverload(string shape, bool overloaded, bool address)
    {
        var (actual, expected, argument, body) = shape switch
        {
            "readonly-scalar" => ("readonly(int)", "int", "value", ""),
            "readonly-pointer" => ("readonly(int*)", "int*", "&value", "*item=42;"),
            "immutable-root" => ("immutable(int*)", "readonly(int)*", "&value", ""),
            "immutable-pointee" => ("immutable(int*)", "int*", "&value", ""),
            "reference" => ("ref readonly(int)", "ref int", "ref value", ""),
            "readonly-pointee" => ("readonly(int)*", "int*", "&value", ""),
            "mutable-implementation" => ("int*", "immutable(int*)", "&value", "*item=42;"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        AssertAssignmentRejected(ParameterSource(actual, expected, argument, body, overloaded, address));
    }

    public static IEnumerable<object[]> ExactParameters()
    {
        foreach (string type in new[] { "readonly(int)", "readonly(int*)", "immutable(int*)",
            "ref readonly(int)", "readonly(int)*" })
        foreach (bool overloaded in new[] { false, true })
        foreach (bool address in new[] { false, true })
            yield return [type, overloaded, address];
    }

    [Theory]
    [MemberData(nameof(ExactParameters))]
    public void ExactProtectedParameterSignaturesRemainCallable(string type, bool overloaded, bool address)
    {
        string argument = type == "readonly(int)" ? "value" : type.StartsWith("ref ") ? "ref value" : "&value";
        string body = type == "readonly(int*)" ? "*item=42;" : "";
        Compiler.Check(ParameterSource(type, type, argument, body, overloaded, address), options: new() { NoStdLib = true });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReturnQualifiersMustMatchWithOrWithoutAnUnrelatedOverload(bool overloaded, bool address)
        => AssertAssignmentRejected(ReturnSource("int", overloaded, address));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExactProtectedReturnSignaturesRemainCallable(bool overloaded, bool address)
        => Compiler.Check(ReturnSource("readonly(int)", overloaded, address), options: new() { NoStdLib = true });

    private static string ParameterSource(string actual, string expected, string argument, string body,
        bool overloaded, bool address) => $$"""
        void F({{actual}} item){ {{body}} }
        {{(overloaded ? "void F(long unrelated){}" : "")}}
        int main(){
            int value=41;
            void({{expected}})* callback={{(address ? "&F" : "F")}};
            callback({{argument}});
            return value;
        }
        """;

    private static string ReturnSource(string expected, bool overloaded, bool address) => $$"""
        readonly(int) F()=>41;
        {{(overloaded ? "int F(long unrelated)=>0;" : "")}}
        int main(){
            {{expected}}()* callback={{(address ? "&F" : "F")}};
            return callback();
        }
        """;

    private static void AssertAssignmentRejected(string source)
        => Assert.Contains(Compiler.Analyze([new SourceFile("function-value-qualifiers.gf", source)], new() { NoStdLib = true }),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Descriptor.Id == "GF1001" &&
                diagnostic.Message.StartsWith("Cannot assign ", StringComparison.Ordinal));
}

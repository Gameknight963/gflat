using gflat.CompileExceptions;

namespace gflat.Tests.Namespaces;

public sealed class NamespaceFunctionValueSignatureTests
{
    public static IEnumerable<object[]> ValidCases()
    {
        foreach (string form in new[] { "implicit", "address", "nullable-alias" })
        foreach (bool early in new[] { false, true })
        foreach (bool reverse in new[] { false, true })
        foreach (int optimization in new[] { 0, 2 })
            yield return [form, early, reverse, optimization];
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void FunctionValuesUseTheirDeclaringNamespaceBeforeAndAfterSpecialization(
        string form, bool early, bool reverse, int optimization)
    {
        string declaration = form == "nullable-alias" ? "Callback*? callback=Library::Identity;" :
            "int(int)* callback=" + (form == "address" ? "&" : "") + "Library::Identity;";
        string invocation = form == "nullable-alias" ? "callback!(42)" : "callback(42)";
        var result = CompilerTestHelper.Run(Sources(declaration, "return " + invocation + ";", early, reverse), optimization);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CallerAliasesCannotMakeAnIncompatibleFunctionPointerAssignable(bool early, bool reverse)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            Sources("long(long)* callback=Library::Identity;", "return 0;", early, reverse)));
        Assert.Contains("Cannot assign", error.Message);
        Assert.Contains("int(int)*", error.Message);
        Assert.Contains("long(long)*", error.Message);
    }

    private static SourceFile[] Sources(string declaration, string body, bool early, bool reverse)
    {
        const string library = "namespace Library { alias Word=int; Word Identity(Word value)=>value; }";
        string helper = $$"""
            namespace Helpers {
                alias Word=long;
                alias Callback=int(int);
                struct Probe<T> {
                    public static int Check() {
                        {{declaration}}
                        {{body}}
                    }
                }
            }
            """;
        string client = (early ? "int Trigger(Helpers::Probe<int>* probe)=>0;\n" : "") +
            "int main()=>Helpers::Probe<int>::Check();";
        return [
            new SourceFile(reverse ? "b-library.gf" : "a-library.gf", library),
            new SourceFile(reverse ? "a-helper.gf" : "b-helper.gf", helper),
            new SourceFile("z-client.gf", client)
        ];
    }
}

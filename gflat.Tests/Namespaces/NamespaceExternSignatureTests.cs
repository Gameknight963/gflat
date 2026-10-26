using gflat.CompileExceptions;

namespace gflat.Tests.Namespaces;

public sealed class NamespaceExternSignatureTests
{
    public static IEnumerable<object[]> ValidCases()
    {
        foreach (string form in new[] { "implicit", "address", "nullable-alias", "direct" })
        foreach (bool early in new[] { false, true })
        foreach (bool reverse in new[] { false, true })
        foreach (int optimization in new[] { 0, 2 })
            yield return [form, early, reverse, optimization];
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void NativeSignaturesUseTheirDeclaringNamespaceDuringEarlySpecialization(
        string form, bool early, bool reverse, int optimization)
    {
        (string declaration, string invocation) = form switch
        {
            "direct" => ("", "Library::abs(-42)"),
            "nullable-alias" => ("Callback*? callback=Library::abs;", "callback!(-42)"),
            "address" => ("int(int)* callback=&Library::abs;", "callback(-42)"),
            _ => ("int(int)* callback=Library::abs;", "callback(-42)")
        };
        var result = CompilerTestHelper.Run(Sources(declaration, "return " + invocation + ";", early, reverse), optimization);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CallerAliasesCannotChangeTheNativeCallbackSignature(bool early, bool reverse)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            Sources("long(long)* callback=Library::abs;", "return 0;", early, reverse)));
        Assert.Contains("Cannot assign", error.Message);
        Assert.Contains("int(int)*", error.Message);
        Assert.Contains("long(long)*", error.Message);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void DeclarationBindingPreservesPrivateExternAccess(bool early, bool reverse, bool direct)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(
            direct ? "" : "int(int)* callback=Library::abs;",
            direct ? "return Library::abs(-42);" : "return callback(-42);",
            early, reverse, privateExtern: true)));
        Assert.Contains("private to its source file", error.Message);
    }

    private static SourceFile[] Sources(string declaration, string body, bool early, bool reverse, bool privateExtern = false)
    {
        string library = "namespace Library { alias Word=int; " + (privateExtern ? "private " : "") +
            "extern Word abs(Word value); }";
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

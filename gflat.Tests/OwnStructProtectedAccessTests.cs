using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class OwnStructProtectedAccessTests
{
    public static IEnumerable<object[]> OwnAccessCases()
    {
        foreach (string member in new[] { "method", "field", "static-property", "property" })
        foreach (bool qualified in new[] { false, true })
        foreach (string visibility in new[] { "protected", "private", "public" })
            yield return new object[] { member, qualified, visibility };
    }

    [Theory]
    [MemberData(nameof(OwnAccessCases))]
    public void DeclaringStructCanUseItsProtectedStaticMembersAndAccessors(string member, bool qualified, string visibility)
    {
        string expression = Access(member, qualified ? member == "property" ? "this." : "Entry::" : "");
        string source = "struct Entry{" + Declaration(member, visibility) +
            "public int Check()=>" + expression + ";}int main(){Entry entry=new Entry();return entry.Check();}";
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void GenericDeclaringStructRetainsItsConcreteOwnerForProtectedAccess()
        => Assert.Equal(42, CompilerTestHelper.Run("namespace Lib{struct Entry<T>{protected static int seed=40;" +
            "protected static int Read()=>seed+2;protected int Value=>Read();public int Check()=>Value;}}" +
            "int main(){Lib::Entry<int> entry=new Lib::Entry<int>();return entry.Check();}").ExitCode);

    [Theory]
    [InlineData("method", false)]
    [InlineData("field", false)]
    [InlineData("static-property", false)]
    [InlineData("property", false)]
    [InlineData("method", true)]
    [InlineData("field", true)]
    [InlineData("static-property", true)]
    [InlineData("property", true)]
    public void GlobalAndUnrelatedStructCallersCannotUseProtectedMembers(string member, bool unrelatedStruct)
    {
        string use = Access(member, member == "property" ? "entry." : "Entry::");
        string body = "Entry entry=new Entry();return " + use + ";";
        string declaration = "struct Entry{" + Declaration(member, "protected") + "}";
        string source = unrelatedStruct
            ? declaration + "struct Other{public static int Check(){" + body + "}}int main()=>Other::Check();"
            : declaration + "int main(){" + body + "}";
        AssertDenied(source, member);
    }

    [Theory]
    [InlineData("method")]
    [InlineData("field")]
    [InlineData("static-property")]
    [InlineData("property")]
    public void NestedTypesDoNotGainAdditionalProtectedPrivileges(string member)
    {
        string expression = Access(member, member == "property" ? "entry." : "Entry::");
        string source = "struct Entry{" + Declaration(member, "protected") +
            "public struct Nested{public static int Check(){Entry entry=new Entry();return " + expression + ";}}}int main()=>0;";
        AssertDenied(source, member);
    }

    [Fact]
    public void ClassAndDerivedClassProtectedAccessStillWorks()
        => Assert.Equal(42, CompilerTestHelper.Run("class Base{protected static int seed=40;protected static int Read()=>Base::seed+2;" +
            "protected int Value=>Base::Read();}class Derived:Base{public int Check()=>Value;}int main(){Derived entry=new Derived();return entry.Check();}").ExitCode);

    private static string Declaration(string member, string visibility) => member switch {
        "method" => visibility + " static int Read()=>42;",
        "field" => visibility + " static int value=42;",
        "static-property" => visibility + " static int Value{get=>42;}",
        _ => visibility + " int Value{get=>42;}"
    };
    private static string Access(string member, string prefix) => prefix + (member switch { "method" => "Read()", "field" => "value", _ => "Value" });
    private static void AssertDenied(string source, string member)
    {
        var file = new SourceFile("own-struct-protected.gf", source);
        var diagnostic = Assert.Single(Compiler.Analyze([file], new() { NoStdLib=true }), error => error.Severity == DiagnosticSeverity.Error);
        string message = member switch { "method" => "Cannot access protected method 'Read'", "field" => "Cannot access protected field 'value'", _ => "Cannot access protected accessor of property 'Value'" };
        Assert.Equal(message, diagnostic.Message);
        var strict = Assert.Throws<TypeCheckException>(() => Compiler.Check(file, options: new() { NoStdLib=true }));
        Assert.Equal(message, strict.Description);
    }
}

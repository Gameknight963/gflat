using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class StructMemberAccessTests
{
    [Theory]
    [InlineData("private", "read")]
    [InlineData("protected", "read")]
    [InlineData("private", "write")]
    [InlineData("protected", "write")]
    [InlineData("private", "address")]
    [InlineData("protected", "address")]
    [InlineData("private", "callback")]
    [InlineData("protected", "callback")]
    [InlineData("private", "method")]
    [InlineData("protected", "method")]
    [InlineData("private", "method-read")]
    [InlineData("protected", "method-read")]
    public void ExternalAccessUsesTheDeclaredVisibility(string visibility, string operation)
    {
        string declaration = operation switch
        {
            "callback" => visibility + " int()* value=Answer;",
            "method" or "method-read" => visibility + " int Read()=>42;",
            _ => visibility + " int value;"
        };
        string access = operation switch
        {
            "read" => "entry.value",
            "write" => "entry.value=42",
            "address" => "&entry.value",
            "callback" => "entry.value()",
            "method" => "entry.Read()",
            _ => "entry.Read"
        };
        string source = "int Answer()=>42;namespace Lib{public struct Entry<T>{" + declaration + "}}" +
            "int main(){Lib::Entry<int> entry=new Lib::Entry<int>();" + access + ";return 0;}";
        string member = operation is "method" or "method-read" ? "method 'Read'" : "field 'value'";
        var file = new SourceFile("struct-member-access.gf", source);
        var diagnostic = Assert.Single(Compiler.Analyze([file], new() { NoStdLib = true }), error => error.Severity == DiagnosticSeverity.Error);
        Assert.Equal("Cannot access " + visibility + " " + member + " of struct 'Lib::Entry<int>'", diagnostic.Message);
        Assert.Equal("struct-member-access.gf", diagnostic.FilePath);
        Assert.Equal(operation is "method" or "method-read" ? "entry.Read" : operation is "write" or "address" or "callback" ? "entry.value" : access, source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        var strict = Assert.Throws<TypeCheckException>(() => Compiler.Check(file, options: new() { NoStdLib = true }));
        Assert.Equal(diagnostic.Message, strict.Description);
        Assert.Equal(diagnostic.Span.Start, strict.Span.Start);
        Assert.Equal(diagnostic.Span.Length, strict.Span.Length);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("protected")]
    [InlineData("internal")]
    [InlineData("public")]
    public void DeclaringTypeCanAccessAnotherInstance(string visibility)
        => Assert.Equal(42, CompilerTestHelper.Run("struct Entry{" + visibility + " int value;public Entry(int item){value=item;}" +
            "public int Read(Entry* other)=>other.value;}int main(){Entry first=new Entry(1);Entry other=new Entry(42);return first.Read(&other);}").ExitCode);

    [Theory]
    [InlineData("internal")]
    [InlineData("public")]
    public void ExternallyVisibleFieldsRemainUsable(string visibility)
        => Assert.Equal(42, CompilerTestHelper.Run("struct Entry{" + visibility + " int value;}int main(){Entry entry=new Entry();entry.value=42;int* value=&entry.value;return *value;}").ExitCode);

    [Fact]
    public void NestedGenericOwnerKeepsPrivateAccess()
        => Assert.Equal(42, CompilerTestHelper.Run("struct Outer<T>{private T value;public Outer(T item){value=item;}" +
            "public struct Inner{public static T Read(Outer<T>* other)=>other.value;}}int main(){Outer<int> value=new Outer<int>(42);return Outer<int>::Inner::Read(&value);}").ExitCode);

    [Fact]
    public void PublicPropertiesCanUsePrivateFields()
        => Assert.Equal(42, CompilerTestHelper.Run("struct Entry{private int value;public Entry(int item){value=item;}public int Value=>value;}int main(){Entry entry=new Entry(42);return entry.Value;}").ExitCode);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicOverloadIsSelectedBeforeAccessIsChecked(bool privateFirst)
    {
        const string restricted = "private int Read(long value)=>0;";
        const string available = "public int Read(int value)=>42;";
        string members = privateFirst ? restricted + available : available + restricted;
        Assert.Equal(42, CompilerTestHelper.Run("struct Entry{" + members + "}int main(){Entry entry=new Entry();return entry.Read(1);}").ExitCode);
        var errors = Compiler.Analyze([new SourceFile("private-overload.gf", "struct Entry{" + members + "}int main(){Entry entry=new Entry();return entry.Read(1l);}")], new() { NoStdLib = true });
        Assert.Contains(errors, error => error.Message == "Cannot access private method 'Read' of struct 'Entry'");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ClassOverloadAccessUsesTheSelectedDeclaringMember(bool privateFirst, bool inherited)
    {
        const string restricted = "private int Read(long value)=>0;";
        const string available = "public int Read(int value)=>42;";
        string members = privateFirst ? restricted + available : available + restricted;
        string declarations = "class Entry{" + members + "}" + (inherited ? "class Derived:Entry{}" : "");
        string owner = inherited ? "Derived" : "Entry";
        Assert.Equal(42, CompilerTestHelper.Run(declarations + "int main(){" + owner + " entry=new " + owner + "();return entry.Read(1);}").ExitCode);
        var errors = Compiler.Analyze([new SourceFile("class-overload.gf", declarations + "int main(){" + owner + " entry=new " + owner + "();return entry.Read(1l);}")], new() { NoStdLib = true });
        Assert.Contains(errors, error => error.Message == "Cannot access private method 'Read' of class 'Entry'");
    }

    [Fact]
    public void UnrelatedStructCannotReadPrivateStorage()
    {
        var errors = Compiler.Analyze([new SourceFile("unrelated.gf", "struct Entry{private int value;}struct Other{public int Read(Entry* item)=>item.value;}int main()=>0;")], new() { NoStdLib = true });
        Assert.Contains(errors, error => error.Message == "Cannot access private field 'value' of struct 'Entry'");
    }

    [Fact]
    public void OwnPrivatePropertiesAndStaticMembersRemainUsable()
        => Assert.Equal(42, CompilerTestHelper.Run("struct Entry{private static int seed=40;private int Value=>seed+2;public int Read()=>Value;}int main(){Entry entry=new Entry();return entry.Read();}").ExitCode);
}

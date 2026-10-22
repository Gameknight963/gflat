using gflat.diagnostics;

namespace gflat.Tests;

public sealed class AutoPropertyReadonlyStorageTests
{
    [Theory]
    [InlineData("class", false)]
    [InlineData("struct", false)]
    [InlineData("class", true)]
    [InlineData("struct", true)]
    public void PublicSettersCanReplaceReadonlyResults(string kind, bool isStatic)
    {
        string modifier = isStatic ? "static " : "";
        string access = isStatic ? "Entry::Value" : "entry.Value";
        string local = isStatic ? "" : "Entry entry=new Entry();";
        Assert.Equal(42, CompilerTestHelper.Run(kind + " Entry{public " + modifier + "readonly(int) Value{get;set;}=1;}" +
            "int main(){" + local + access + "=42;return " + access + ";}").ExitCode);
    }

    [Theory]
    [InlineData("class", false)]
    [InlineData("struct", false)]
    [InlineData("class", true)]
    [InlineData("struct", true)]
    public void DeclaringConstructorsCanInitializeGetOnlyProperties(string kind, bool isStatic)
    {
        string modifier = isStatic ? "static " : "";
        string constructor = isStatic ? "static Entry(){Value=42;}" : "public Entry(){Value=42;}";
        string read = isStatic ? "Entry::Value" : "entry.Value";
        string local = isStatic ? "" : "Entry entry=new Entry();";
        Assert.Equal(42, CompilerTestHelper.Run(kind + " Entry{public " + modifier + "readonly(int) Value{get;}" + constructor +
            "}int main(){" + local + "return " + read + ";}").ExitCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamespacePropertiesUseTheirOwnBackingStorage(bool setter)
    {
        string accessors = setter ? "get;set;" : "get;";
        string write = setter ? "Lib::Value=42;" : "";
        Assert.Equal(42, CompilerTestHelper.Run("namespace Lib{public readonly(int) Value{" + accessors + "}=42;}int main(){" + write + "return Lib::Value;}").ExitCode);
    }

    [Theory]
    [InlineData("readonly(int)")]
    [InlineData("Frozen")]
    public void GenericPropertiesPreserveAliasedAndSubstitutedQualifiers(string type)
        => Assert.Equal(42, CompilerTestHelper.Run("alias Frozen=readonly(int);struct Holder<T>{public T Value{get;set;}public Holder(T value){Value=value;}}" +
            "int main(){Holder<" + type + "> holder=new Holder<" + type + ">(1);holder.Value=42;return holder.Value;}").ExitCode);

    [Fact]
    public void ReadonlyPointerResultsAllowReplacementOfThePointer()
        => Assert.Equal(42, CompilerTestHelper.Run("struct Holder{public immutable(char*) Value{get;set;}=\"initial\";}int main(){Holder holder=new Holder();holder.Value=\"*\";return (int)holder.Value[0];}").ExitCode);

    [Fact]
    public void ReadonlyAggregateResultsKeepTheirPointerViews()
        => Assert.Equal(42, CompilerTestHelper.Run("int number=42;struct Cell{public int* data;public Cell(int* value){data=value;}}" +
            "struct Holder{public immutable(Cell) Value{get;set;}=new Cell(&number);}" +
            "int main(){Holder holder=new Holder();Cell value=new Cell(&number);holder.Value=value;return *holder.Value.data;}").ExitCode);

    [Theory]
    [InlineData("class Entry{private readonly(int) field;public int Value{get{return field;}set{field=value;}}}", "readonly")]
    [InlineData("class Entry{public readonly(int) Value{get;}public Entry(){Value=1;}public void Reset(){Value=42;}}", "declaring constructor")]
    [InlineData("class Entry{public readonly(int) Value{get;}public Entry(Entry* other){other.Value=42;Value=1;}}", "declaring constructor")]
    [InlineData("class Entry{public readonly(int) Value{get;}public Entry(){Value=1;}}class Other{public Other(Entry* entry){entry.Value=42;}}", "declaring constructor")]
    [InlineData("class Entry{public readonly(int) Value{get;}public Entry(){int()* initialize=()=>{Value=42;return 0;};}}", "Capturing")]
    [InlineData("struct Entry{public readonly(int) Value{get;set;}}void Write(immutable(Entry)* entry){entry.Value=42;}", "non-immutable")]
    [InlineData("struct Entry{public int Value{get;immutable set;}}", "readonly")]
    [InlineData("struct Entry{public immutable(char*) Value{get;set;}=\"text\";}void Write(Entry* entry){entry.Value[0]='x';}", "readonly")]
    [InlineData("namespace Lib{public readonly(int) Value{get;}=1;}void Write(){Lib::Value=42;}", "cannot be assigned")]
    [InlineData("int number=1;struct Cell{public int* data;public Cell(int* value){data=value;}}struct Holder{public immutable(Cell) Value{get;set;}=new Cell(&number);}void Write(Holder* holder){*holder.Value.data=42;}", "readonly")]
    public void GeneratedStoragePermissionDoesNotWeakenSourceRestrictions(string source, string expected)
    {
        var errors = Compiler.Analyze([new SourceFile("readonly-property-negative.gf", source)], new() { NoStdLib = true });
        Assert.Contains(errors, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }
}

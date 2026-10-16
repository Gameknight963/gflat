using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class GenericStaticCallableTests
{
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
    private static (EditorModel Model, AnalysisSnapshot Snapshot, SourceFile Main, SourceFile Library) Analyze(string main, string library)
    {
        var source = new SourceFile(Path.GetFullPath("generic-call-main.gf"), main);
        var extra = new SourceFile(Path.GetFullPath("generic-call-library.gf"), library);
        var snapshot = new AnalysisSnapshot([source, extra], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return (new(snapshot, [source, extra]), snapshot, source, extra);
    }
    private static string Hover(EditorModel model, SourceFile file, int offset)
        => Json(model.Hover(file.Path, offset)).GetProperty("contents").GetProperty("value").GetString()!;

    [Theory]
    [InlineData("class", "Library::Box<int>::Identity(3, 4)")]
    [InlineData("struct", "Library::Box<int>::Identity(3, 4)")]
    [InlineData("class", "(&Library::Box<int>::Identity)(3, 4)")]
    public void GenericStaticCallsAndAddressesKeepConcreteTypesAndDeclaredAliases(string kind, string expression)
    {
        string library = $$"""
            namespace Library { alias Word=int;
                {{kind}} Box<T> { public static T Identity(T value, Word tag)=>value; }
            }
            """;
        var (model, _, main, extra) = Analyze("alias Word=long; int main()=>" + expression + ";", library);
        int member = main.Text.IndexOf("Identity", StringComparison.Ordinal);
        string hover = Hover(model, main, member);
        Assert.Contains("int value", hover);
        Assert.Contains("Word tag", hover);
        Assert.DoesNotContain("T value", hover);
        var definition = Json(model.Definition(main.Path, member));
        Assert.Contains("generic-call-library.gf", definition.GetProperty("uri").GetString());
        Assert.Equal(extra.Text[..extra.Text.IndexOf("Identity", StringComparison.Ordinal)].Split('\n').Last().Length,
            definition.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Fact]
    public void SelectedOverloadNavigatesItsOriginalDeclarationAndProvidesConcreteSignatures()
    {
        const string library = "class Box<T> { public static int Pick(int value)=>value; public static long Pick(long value)=>value; }";
        var (model, _, main, extra) = Analyze("long Test()=>Box<int>::Pick(3l);", library);
        int member = main.Text.IndexOf("Pick", StringComparison.Ordinal);
        var target = Json(model.Definition(main.Path, member)).GetProperty("range");
        Assert.Equal(extra.Text.LastIndexOf("Pick", StringComparison.Ordinal),
            target.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Contains("long value", Hover(model, main, member));
        var help = Json(model.SignatureHelp(main.Path, main.Text.IndexOf("3l", StringComparison.Ordinal) + 1));
        Assert.Equal(2, help.GetProperty("signatures").GetArrayLength());
        Assert.Contains("long value", help.GetProperty("signatures")[help.GetProperty("activeSignature").GetInt32()].GetProperty("label").GetString());
    }

    [Fact]
    public void NestedGenericTypeShapesPreserveSourceAliases()
    {
        const string library = "namespace Library { alias Word=int; struct Pair<A,B>{public A first;public B second;} class Box<T>{public static Pair<T,Word> Shape(Pair<T,Word> value)=>value;} }";
        var (model, _, main, _) = Analyze("alias Word=long; int main(){Library::Pair<int,int> pair=default; Library::Pair<int,int> result=Library::Box<int>::Shape(pair);return 0;}", library);
        Assert.Contains("Pair<int, Word>", Hover(model, main, main.Text.IndexOf("Shape", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("int(int,int)* fn=&Library::Box<int>::Identity; return fn(3,4);")]
    [InlineData("int(int,int)* fn=Library::Box<int>::Identity; return fn(3,4);")]
    public void AssignedFunctionValuesNavigateTheSourceCallable(string body)
    {
        var (model, _, main, _) = Analyze("int main(){" + body + "}", "namespace Library { class Box<T>{public static T Identity(T value,int tag)=>value;} }");
        int name = main.Text.IndexOf("Identity", StringComparison.Ordinal);
        Assert.Contains("int value", Hover(model, main, name));
        Assert.Contains("generic-call-library.gf", Json(model.Definition(main.Path, name)).ToString());
    }

    [Fact]
    public void NestedNongenericOwnerUsesSignatureBindingsAndAliasOwnersWork()
    {
        const string library = "namespace Library {class Outer<T>{public class Inner{public static T Read(T value)=>value;}}}";
        var (model, _, main, _) = Analyze("alias Inner=Library::Outer<int>::Inner; int main()=>Inner::Read(3);", library);
        Assert.Contains("int value", Hover(model, main, main.Text.IndexOf("Read", StringComparison.Ordinal)));
    }

    [Fact]
    public void PointerReadonlyNullableAndReferenceShapesAreSubstituted()
    {
        const string library = "class Box<T>{public static readonly(T)*? Pointer(readonly(T)*? value)=>value;public static T Read(ref T value)=>value;}";
        var (model, _, main, _) = Analyze("int main(){readonly(int)*? p=Box<int>::Pointer(null);int n=3;return Box<int>::Read(ref n);}", library);
        string pointer = Hover(model, main, main.Text.IndexOf("Pointer", StringComparison.Ordinal));
        Assert.Contains("readonly(int)*?", pointer);
        Assert.DoesNotContain("readonly(T)", pointer);
        Assert.Contains("ref int value", Hover(model, main, main.Text.IndexOf("Read", StringComparison.Ordinal)));
    }

    [Fact]
    public void GenericMethodCallsGetBoundSignatureHelpAndUnusedArgumentsRemainExplicitlyUnbound()
    {
        const string library = "class Box<T>{public static U Echo<U>(U value)=>value;public static int Ignore<U>(int value)=>value;}";
        var (model, _, main, _) = Analyze("int main(){int a=Box<int>::Echo<int>(3);return Box<int>::Ignore<long>(a);}", library);
        int echo = main.Text.IndexOf("Echo", StringComparison.Ordinal);
        Assert.Contains("Echo<int>(int value)", Hover(model, main, echo));
        var help = Json(model.SignatureHelp(main.Path, main.Text.IndexOf("(3)", StringComparison.Ordinal) + 1));
        Assert.Contains("Echo<int>(int value)", help.GetProperty("signatures")[0].GetProperty("label").GetString());
        Assert.Contains("Ignore<U>", Hover(model, main, main.Text.IndexOf("Ignore", StringComparison.Ordinal)));
    }

    [Fact]
    public void RichDeclaredAliasTargetsItsLibraryAndRepeatedQueriesDoNotChangeCompilerState()
    {
        const string library = "namespace Library {alias Word=int;class Box<T>{public static T Read(T value,Word tag)=>value;}}";
        var (model, snapshot, main, extra) = Analyze("alias Word=long;int main()=>Library::Box<int>::Read(3,4);", library);
        int member = main.Text.IndexOf("Read", StringComparison.Ordinal);
        var presentation = new VisualStudioHover(Json(new { }));
        var rich = Json(model.Hover(main.Path, member, presentation));
        var runs = rich.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")[0].GetProperty("Runs");
        var alias = runs.EnumerateArray().Single(run => run.GetProperty("Text").GetString() == "Word");
        Assert.Contains("generic-call-library.gf", alias.GetProperty("_gflat_target").GetProperty("uri").GetString());
        Assert.Equal(extra.Text.IndexOf("Word", StringComparison.Ordinal), alias.GetProperty("_gflat_target").GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        var parameter = runs.EnumerateArray().Single(run => run.GetProperty("Text").GetString() == "value");
        Assert.Contains("generic-call-library.gf", parameter.GetProperty("_gflat_target").GetProperty("uri").GetString());
        int types = snapshot.Checker!.GetAllClasses().Count();
        for (int i = 0; i < 5; i++)
        {
            model.Hover(main.Path, member); model.Definition(main.Path, member); model.SemanticTokens(main.Path);
            model.SignatureHelp(main.Path, main.Text.IndexOf("(3,4)", StringComparison.Ordinal) + 1);
        }
        Assert.Equal(types, snapshot.Checker.GetAllClasses().Count());
    }

    [Fact]
    public void MethodAndOuterTypeParametersRemainDistinctInPresentation()
    {
        var (model, _, main, _) = Analyze("long Test()=>Box<int>::Read<long>(3l);", "class Box<T>{public static U Read<U>(U value)=>value;}");
        Assert.Contains("Read<long>(long value)", Hover(model, main, main.Text.IndexOf("Read", StringComparison.Ordinal)));
    }

    [Fact]
    public void EarlierErrorsDoNotHideHealthySelectedMethodMetadata()
    {
        var source = new SourceFile(Path.GetFullPath("generic-recovery-main.gf"), "int Broken()=>Missing(); int Healthy()=>Box<int>::Read(3);");
        var library = new SourceFile(Path.GetFullPath("generic-recovery-lib.gf"), "class Box<T>{public static T Read(T value)=>value;}");
        var snapshot = new AnalysisSnapshot([source, library], new() { NoStdLib = true });
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source, library]);
        int member = source.Text.IndexOf("Read", StringComparison.Ordinal);
        Assert.Contains("int value", Hover(model, source, member));
        Assert.NotNull(model.Definition(source.Path, member));
    }

    [Fact]
    public void SourceTemplateHoverKeepsItsGenericParameters()
    {
        var (model, _, _, library) = Analyze("int main()=>Box<int>::Read(3);", "class Box<T>{public static T Read(T value)=>value;}");
        Assert.Contains("T value", Hover(model, library, library.Text.IndexOf("Read", StringComparison.Ordinal)));
    }

    [Fact]
    public void NestedExplicitGenericArgumentsSupportSignatureHelpAcrossCombinedClosingTokens()
    {
        const string library = "struct Pair<A,B>{public A first;public B second;} class Box<T>{public static U Echo<U>(U value)=>value;}";
        var (model, _, main, _) = Analyze("int main(){Pair<int,byte> pair=default;Pair<int,byte> result=Box<int>::Echo<Pair<int,byte>>(pair);return 0;}", library);
        var help = Json(model.SignatureHelp(main.Path, main.Text.IndexOf(">>(pair)", StringComparison.Ordinal) + 3));
        Assert.Contains("Pair<int, byte> value", help.GetProperty("signatures")[0].GetProperty("label").GetString());
    }
}

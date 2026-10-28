using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class SourceGenericStaticMethodGroupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UninstantiatedSourceGroupsUseWrittenOwnerArguments(bool namespaced, bool address)
    {
        string library = "class Bridge<T>{public static int Compare(T left,T right)=>0;private static int Secret(T left,T right)=>0;public int Instance(T left,T right)=>0;}";
        if (namespaced) library = "namespace Lib{" + library + "}";
        string prefix = namespaced ? "Lib::" : "";
        string group = (address ? "&" : "") + prefix + "Bridge<U>::Compare";
        string text = "void Consume<T>(int(T,T)* callback){}void Template<U>()=>Consume<U>(" + group + ");" +
            "void Concrete()=>Consume<int>(" + prefix + "Bridge<int>::Compare);int main(){Concrete();return 0;}";
        var fixture = Analyze(text, library);
        int use = text.IndexOf("Compare", StringComparison.Ordinal);
        var expected = fixture.Model.Definition(fixture.Declaration.Path, library.IndexOf("Compare", StringComparison.Ordinal));
        Assert.NotNull(expected);
        for (int iteration = 0; iteration < 3; iteration++)
        {
            string hover = Hover(fixture.Model, fixture.Client, use);
            Assert.Contains("U left", hover); Assert.Contains("U right", hover);
            Assert.Contains(prefix + "Bridge<U>", hover);
            Assert.DoesNotContain("T left", hover); Assert.DoesNotContain("int left", hover);
            Assert.Equal(expected, fixture.Model.Definition(fixture.Client.Path, use));
            Assert.Equal("method", Classification(fixture.Model, fixture.Client, use));
            var candidates = Json(fixture.Model.Completion(fixture.Client.Path, use)).GetProperty("items").EnumerateArray().ToArray();
            var compare = Assert.Single(candidates, entry => entry.GetProperty("label").GetString() == "Compare");
            Assert.Contains("U left", compare.GetProperty("detail").GetString());
            Assert.Contains(prefix + "Bridge<U>", compare.GetProperty("detail").GetString());
            Assert.DoesNotContain(candidates, entry => entry.GetProperty("label").GetString() is "Secret" or "Instance");
        }
        AssertUnchanged(fixture);
    }

    [Fact]
    public void CheckedConcreteGroupRetainsItsSelectedCallable()
    {
        const string library = "namespace Lib{class Bridge<T>{public static int Compare(T left,T right)=>0;}}";
        const string text = "void Consume<T>(int(T,T)* callback){}void Concrete()=>Consume<int>(Lib::Bridge<int>::Compare);int main(){Concrete();return 0;}";
        var fixture = Analyze(text, library);
        int use = text.IndexOf("Compare", StringComparison.Ordinal);
        Assert.Contains("int left", Hover(fixture.Model, fixture.Client, use));
        Assert.Equal(fixture.Model.Definition(fixture.Declaration.Path, library.IndexOf("Compare", StringComparison.Ordinal)),
            fixture.Model.Definition(fixture.Client.Path, use));
        Assert.Equal("method", Classification(fixture.Model, fixture.Client, use));
        AssertUnchanged(fixture);
    }

    [Theory]
    [InlineData("Secret")]
    [InlineData("Instance")]
    public void SourceFallbackDoesNotExposePrivateOrInstanceMembers(string member)
    {
        const string library = "class Bridge<T>{public static int Compare(T a,T b)=>0;private static int Secret(T a,T b)=>0;public int Instance(T a,T b)=>0;}";
        string text = "void Consume<T>(int(T,T)* callback){}void Template<U>()=>Consume<U>(Bridge<U>::" + member + ");int main()=>0;";
        var fixture = Analyze(text, library);
        int use = text.IndexOf("::" + member, StringComparison.Ordinal) + 2;
        Assert.Null(fixture.Model.Definition(fixture.Client.Path, use));
        Assert.Null(fixture.Model.Hover(fixture.Client.Path, use));
        AssertUnchanged(fixture);
    }

    [Fact]
    public void UncheckedOverloadedGroupDoesNotInventASelectedDeclaration()
    {
        const string library = "class Bridge<T>{public static int Compare(T a,T b)=>0;public static int Compare(T a)=>0;}";
        const string text = "void Consume<T>(int(T,T)* callback){}void Template<U>()=>Consume<U>(Bridge<U>::Compare);int main()=>0;";
        var fixture = Analyze(text, library);
        Assert.Null(fixture.Model.Definition(fixture.Client.Path, text.IndexOf("::Compare", StringComparison.Ordinal) + 2));
        AssertUnchanged(fixture);
    }

    [Fact]
    public void MethodGenericShadowingRetainsDeclaredParametersAndTheWrittenOwner()
    {
        const string library = "class Bridge<T>{public static int Compare<T>(T left,T right)=>0;}";
        const string text = "void Consume<T>(int(T,T)* callback){}void Template<U>()=>Consume<U>(Bridge<U>::Compare);" +
            "void Concrete()=>Consume<int>(Bridge<int>::Compare);int main(){Concrete();return 0;}";
        var fixture = Analyze(text, library);
        int use = text.IndexOf("Compare", StringComparison.Ordinal);
        string hover = Hover(fixture.Model, fixture.Client, use);
        Assert.Contains("Compare<T>", hover); Assert.Contains("T left", hover); Assert.Contains("T right", hover);
        Assert.Contains("Bridge<U>", hover);
        Assert.DoesNotContain("Bridge<int>", hover); Assert.DoesNotContain("Bridge<T>", hover);
        Assert.DoesNotContain("U left", hover); Assert.DoesNotContain("int left", hover);
        Assert.Equal(fixture.Model.Definition(fixture.Declaration.Path, library.IndexOf("Compare", StringComparison.Ordinal)),
            fixture.Model.Definition(fixture.Client.Path, use));
        var candidates = Json(fixture.Model.Completion(fixture.Client.Path, use)).GetProperty("items").EnumerateArray();
        var compare = Assert.Single(candidates, entry => entry.GetProperty("label").GetString() == "Compare");
        Assert.Contains("Bridge<U>", compare.GetProperty("detail").GetString());
        Assert.Contains("T left", compare.GetProperty("detail").GetString());
        AssertUnchanged(fixture);
    }

    [Fact]
    public void UnboundMethodReturnDoesNotAcquireAnUnrelatedGlobalTypesMembers()
    {
        const string library = "class T{public int Bogus;}class Bridge<A>{public static T Identity<T>(T value)=>value;}";
        const string text = "void Template<U>(U value){Bridge<U>::Identity(value).Bogus;}int main()=>0;";
        var fixture = Analyze(text, library);
        int use = text.IndexOf("Bogus", StringComparison.Ordinal);
        Assert.Null(fixture.Model.Hover(fixture.Client.Path, use));
        Assert.Null(fixture.Model.Definition(fixture.Client.Path, use));
        Assert.Null(Classification(fixture.Model, fixture.Client, use));
        var items = Json(fixture.Model.Completion(fixture.Client.Path, use)).GetProperty("items").EnumerateArray();
        Assert.DoesNotContain(items, item => item.GetProperty("label").GetString() == "Bogus");
        AssertUnchanged(fixture);
    }

    [Fact]
    public void NestedOwnerArgumentsUseTheExistingSourceProjection()
    {
        const string library = "namespace Lib{class Outer<T>{public class Bridge<V>{public static int Compare(T left,V right)=>0;}}}";
        const string text = "void Consume<T>(int(T,long)* callback){}void Template<U>()=>Consume<U>(Lib::Outer<U>::Bridge<long>::Compare);int main()=>0;";
        var fixture = Analyze(text, library);
        int use = text.IndexOf("Compare", StringComparison.Ordinal);
        string hover = Hover(fixture.Model, fixture.Client, use);
        Assert.Contains("U left", hover); Assert.Contains("long right", hover);
        Assert.Contains("Lib::Outer<U>::Bridge<long>", hover);
        Assert.Equal(fixture.Model.Definition(fixture.Declaration.Path, library.IndexOf("Compare", StringComparison.Ordinal)),
            fixture.Model.Definition(fixture.Client.Path, use));
        Assert.Equal("method", Classification(fixture.Model, fixture.Client, use));
        var candidates = Json(fixture.Model.Completion(fixture.Client.Path, use)).GetProperty("items").EnumerateArray();
        var compare = Assert.Single(candidates, entry => entry.GetProperty("label").GetString() == "Compare");
        Assert.Contains("U left", compare.GetProperty("detail").GetString());
        Assert.Contains("long right", compare.GetProperty("detail").GetString());
        Assert.Contains("Lib::Outer<U>::Bridge<long>", compare.GetProperty("detail").GetString());
        AssertUnchanged(fixture);
    }

    [Fact]
    public void PlainAndConstrainedAliasesKeepTheirExistingExposureRules()
    {
        const string library = """
            interface IProvider{static int Read();}
            static class Provider:IProvider{public static int Read()=>1;public static int Extra()=>2;}
            weak alias Restricted:IProvider=Provider;
            alias Plain=Provider;
            """;
        const string text = "void Template<U>(){Plain::Read();Restricted::Read();}int main()=>0;";
        var fixture = Analyze(text, library);
        foreach (string qualifier in new[] { "Plain", "Restricted" })
        {
            int use = text.IndexOf(qualifier + "::Read", StringComparison.Ordinal) + qualifier.Length + 2;
            Assert.NotNull(fixture.Model.Hover(fixture.Client.Path, use)); Assert.NotNull(fixture.Model.Definition(fixture.Client.Path, use));
            var items = Json(fixture.Model.Completion(fixture.Client.Path, use)).GetProperty("items").EnumerateArray().ToArray();
            Assert.Contains(items, item => item.GetProperty("label").GetString() == "Read");
            if (qualifier == "Restricted") Assert.DoesNotContain(items, item => item.GetProperty("label").GetString() == "Extra");
        }
        AssertUnchanged(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UncheckedGenericAliasApplicationsDoNotExposeProviderImplementation(bool namespaced)
    {
        string library = "interface IProvider{static int Read();}static class Provider:IProvider{public static int Read()=>1;public static int Extra()=>2;}weak alias Restricted:IProvider=Provider;";
        if (namespaced) library = "namespace Lib{" + library + "}";
        string qualifier = namespaced ? "Lib::Restricted<U>" : "Restricted<U>";
        // The parser accepts the type application in this deferred source body.
        // Without a checked binding, it must not unwrap the alias implementation.
        string text = "void Template<U>(){" + qualifier + "::Extra();}int main()=>0;";
        var fixture = Analyze(text, library);
        int use = text.IndexOf("::Extra", StringComparison.Ordinal) + 2;
        Assert.Null(fixture.Model.Hover(fixture.Client.Path, use));
        Assert.Null(fixture.Model.Definition(fixture.Client.Path, use));
        var items = Json(fixture.Model.Completion(fixture.Client.Path, use)).GetProperty("items").EnumerateArray();
        Assert.DoesNotContain(items, item => item.GetProperty("label").GetString() == "Extra");
        AssertUnchanged(fixture);
    }

    [Fact]
    public void UncheckedGenericParameterApplicationDoesNotInventImplementationMembers()
    {
        const string library = "interface IProvider{static int Read();}static class Provider:IProvider{public static int Read()=>1;public static int Extra()=>2;}";
        const string text = "void Template<P:IProvider,U>(){P<U>::Extra();}int main()=>0;";
        var fixture = Analyze(text, library);
        int use = text.IndexOf("::Extra", StringComparison.Ordinal) + 2;
        Assert.Null(fixture.Model.Hover(fixture.Client.Path, use));
        Assert.Null(fixture.Model.Definition(fixture.Client.Path, use));
        var items = Json(fixture.Model.Completion(fixture.Client.Path, use)).GetProperty("items").EnumerateArray();
        Assert.DoesNotContain(items, item => item.GetProperty("label").GetString() == "Extra");
        AssertUnchanged(fixture);
    }

    private static (EditorModel Model, AnalysisSnapshot Snapshot, SourceFile Client, SourceFile Declaration,
        TypeExpression[] Types, SourceSpan[] Spans, int Classes) Analyze(string text, string library)
    {
        var client = new SourceFile(Path.GetFullPath("generic-static-group/client.gf"), text);
        var declaration = new SourceFile(Path.GetFullPath("generic-static-group/library.gf"), library);
        var snapshot = new AnalysisSnapshot([client, declaration], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var types = snapshot.Nodes.Select(node => node.Node).OfType<TypeExpression>().ToArray();
        var spans = types.Select(type => type.Span).ToArray();
        int classes = snapshot.Checker!.GetAllClasses().Count();
        var model = new EditorModel(snapshot, [client, declaration]);
        var fixture = (Model: model, Snapshot: snapshot, Client: client, Declaration: declaration, Types: types, Spans: spans, Classes: classes);
        AssertUnchanged(fixture);
        return fixture;
    }
    private static void AssertUnchanged((EditorModel Model, AnalysisSnapshot Snapshot, SourceFile Client, SourceFile Declaration,
        TypeExpression[] Types, SourceSpan[] Spans, int Classes) fixture)
    {
        Assert.Equal(fixture.Classes, fixture.Snapshot.Checker!.GetAllClasses().Count());
        Assert.Equal(fixture.Spans, fixture.Types.Select(type => type.Span).ToArray());
    }
    private static string Hover(EditorModel model, SourceFile source, int use)
    {
        var hover = Json(model.Hover(source.Path, use));
        Assert.NotEqual(JsonValueKind.Null, hover.ValueKind);
        return hover.GetProperty("contents").GetProperty("value").GetString()!;
    }
    private static string? Classification(EditorModel model, SourceFile source, int offset)
    {
        int[] data = Json(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int line = 0, column = 0;
        var expected = source.Span(offset, 1);
        for (int index = 0; index < data.Length; index += 5)
        {
            line += data[index]; column = data[index] == 0 ? column + data[index + 1] : data[index + 1];
            if (line == expected.Line - 1 && column == expected.Column - 1) return EditorModel.TokenTypes[data[index + 3]];
        }
        return null;
    }
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}

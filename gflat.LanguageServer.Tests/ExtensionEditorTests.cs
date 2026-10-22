using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class ExtensionEditorTests
{
    private static (EditorModel Model, string Path, int Offset, SourceFile Source) Analyze(string marked, string library)
    {
        int offset = marked.IndexOf('|');
        var source = new SourceFile(Path.GetFullPath("extension-main.gf"), marked.Remove(offset, 1));
        var extra = new SourceFile(Path.GetFullPath("extension-library.gf"), library);
        var snapshot = new AnalysisSnapshot([source, extra]);
        return (new EditorModel(snapshot, [source, extra]), source.Path, offset, source);
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
    private static string[] Labels(object value) => Json(value).GetProperty("items").EnumerateArray()
        .Select(item => item.GetProperty("label").GetString()!).ToArray();

    [Fact]
    public void DotCompletionFiltersExtensionsByUsingAndReceiverType()
    {
        const string library = "namespace Extras; int Double(this int value) => value * 2; int Wrong(this char* text) => 0;";
        var (model, path, offset, _) = Analyze("using Extras; int main() { int n = 2; n.|; return 0; }", library);
        Assert.Contains("Double", Labels(model.Completion(path, offset)));
        Assert.DoesNotContain("Wrong", Labels(model.Completion(path, offset)));
        var (hidden, hp, ho, _) = Analyze("int main() { int n = 2; n.|; return 0; }", library);
        Assert.DoesNotContain("Double", Labels(hidden.Completion(hp, ho)));
    }

    [Fact]
    public void DotCallsHaveBoundHoverNavigationAndSignatureHelp()
    {
        const string library = "namespace Extras; int Add(this int value, int amount = 2) => value + amount;";
        var (model, path, offset, source) = Analyze("using Extras; int main() { int n = 40; return n.Ad|d(); }", library);
        var hover = Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("int Extras::Add(int amount = 2)", hover);
        Assert.DoesNotContain("this", hover);
        Assert.Contains("extension-library.gf", Json(model.Definition(path, offset)).ToString());
        var signature = Json(model.SignatureHelp(path, source.Text.LastIndexOf("Add(", StringComparison.Ordinal) + 4, visualStudio: true));
        Assert.Single(signature.GetProperty("signatures")[0].GetProperty("parameters").EnumerateArray());
        Assert.Equal(0, signature.GetProperty("activeParameter").GetInt32());
        Assert.Contains("amount", signature.GetProperty("signatures")[0].GetProperty("label").GetString());
        Assert.DoesNotContain("value", signature.GetProperty("signatures")[0].GetProperty("label").GetString());
        Assert.NotEqual(JsonValueKind.Null, signature.GetProperty("signatures")[0].GetProperty("_vs_colorizedLabel").ValueKind);
    }

    [Fact]
    public void FreeCallsKeepTheExplicitReceiverInTheirSignature()
    {
        var (model, path, offset, source) = Analyze("using Extras; int main() { return Extras::Ad|d(40, 2); }",
            "namespace Extras; int Add(this int value, int amount) => value + amount;");
        Assert.Contains("this int value", Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString());
        var signature = Json(model.SignatureHelp(path, source.Text.LastIndexOf("Add(", StringComparison.Ordinal) + 7));
        Assert.Equal(2, signature.GetProperty("signatures")[0].GetProperty("parameters").GetArrayLength());
    }

    [Fact]
    public void RefExtensionsRequireStorageAndHonorReadonly()
    {
        const string library = "namespace Extras; void Bump(this ref int value) { value++; } int Read(this ref readonly(int) value) => value;";
        var (mutable, mp, mo, _) = Analyze("using Extras; void F() { int n = 1; n.|; }", library);
        Assert.Contains("Bump", Labels(mutable.Completion(mp, mo)));
        Assert.Contains("Read", Labels(mutable.Completion(mp, mo)));
        var (readOnly, rp, ro, _) = Analyze("using Extras; void F(readonly(int) n) { n.|; }", library);
        Assert.DoesNotContain("Bump", Labels(readOnly.Completion(rp, ro)));
        Assert.Contains("Read", Labels(readOnly.Completion(rp, ro)));
        var (temporary, tp, to, _) = Analyze("using Extras; int Value() => 1; void F() { Value().|; }", library);
        Assert.DoesNotContain("Bump", Labels(temporary.Completion(tp, to)));
        Assert.DoesNotContain("Read", Labels(temporary.Completion(tp, to)));
    }

    [Fact]
    public void GenericExtensionsDisplayInferredTypesAndNavigateToTheirDefinition()
    {
        const string library = "namespace Extras; T Identity<T>(this T value) => value;";
        var (model, path, offset, _) = Analyze("using Extras; int main() { int n = 42; return n.Iden|tity(); }", library);
        Assert.Contains("int Extras::Identity<int>()", Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("extension-library.gf", Json(model.Definition(path, offset)).ToString());
        var (completion, cp, co, _) = Analyze("using Extras; int main() { int n = 42; n.|; return 0; }", library);
        var item = Json(completion.Completion(cp, co)).GetProperty("items").EnumerateArray().Single(item => item.GetProperty("label").GetString() == "Identity");
        Assert.Contains("int Extras::Identity<int>()", item.GetProperty("detail").GetString());
    }

    [Fact]
    public void NonCallablePropertiesAndExtensionMethodsBothAppear()
    {
        const string library = "namespace Extras; int Count(this ref immutable(Box) box) => box.Count;";
        var (model, path, offset, _) = Analyze("using Extras; struct Box { public int Count => 42; } int main() { Box b = new Box(); b.|; return 0; }", library);
        var items = Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("label").GetString() == "Count").ToArray();
        Assert.Equal(2, items.Length);
        Assert.Contains(items, item => item.GetProperty("kind").GetInt32() == 10);
        Assert.Contains(items, item => item.GetProperty("kind").GetInt32() == 2);
    }

    [Fact]
    public void LiteralReceiversHaveCompletionAndNavigation()
    {
        const string library = "namespace Extras; int Bytes(this immutable(char*) text) => 3;";
        var (model, path, offset, _) = Analyze("using Extras; int main() => \"abc\".By|tes();", library);
        Assert.NotNull(model.Hover(path, offset));
        Assert.Contains("extension-library.gf", Json(model.Definition(path, offset)).ToString());
        var (completion, cp, co, _) = Analyze("using Extras; void F() { \"abc\".|; }", library);
        Assert.Contains("Bytes", Labels(completion.Completion(cp, co)));
    }

    [Fact]
    public void GuardedNullableRefReceiversKeepTheirDeclaredSlotType()
    {
        const string library = "namespace Extras; void Clear(this ref int*? slot) { slot = null; }";
        var (model, path, offset, _) = Analyze("using Extras; void F(int*? p) { if (p == null) return; p.|; }", library);
        Assert.Contains("Clear", Labels(model.Completion(path, offset)));
    }

    [Fact]
    public void PointerPointeeCompletionsPreserveReadonly()
    {
        const string library = "namespace Extras; void Bump(this ref Box box) { box.value++; } int Read(this ref immutable(Box) box) => box.value;";
        const string box = "struct Box { public int value; } ";
        var (model, path, offset, _) = Analyze("using Extras; " + box + "void F(Box* p) { p.|; }", library);
        Assert.Contains("Bump", Labels(model.Completion(path, offset)));
        Assert.Contains("Read", Labels(model.Completion(path, offset)));
        var (readOnly, rp, ro, _) = Analyze("using Extras; " + box + "void F(immutable(Box)* p) { p.|; }", library);
        Assert.DoesNotContain("Bump", Labels(readOnly.Completion(rp, ro)));
        Assert.Contains("Read", Labels(readOnly.Completion(rp, ro)));
    }

    [Fact]
    public void NestedGenericReceiverTypesAreSubstitutedInHovers()
    {
        var (model, path, offset, _) = Analyze("""
            using Extras;
            struct Box<T> { public T value; public Box(T n) { value = n; } }
            int main() { Box<int> box = new Box<int>(42); return box.Ge|t(); }
            """, "namespace Extras; T Get<T>(this ref immutable(Box<T>) box) => box.value;");
        Assert.Contains("int Extras::Get<int>()", Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString());
        Assert.NotNull(model.Definition(path, offset));
    }

    [Fact]
    public void BoundSignaturesPreserveSourceAliases()
    {
        var (model, path, offset, _) = Analyze("using Extras; int main() { int n = 1; n.Po|inter(); return 0; }",
            "namespace Extras { alias Handle = void*?; Handle Pointer(this int value) => null; }");
        string hover = Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Handle Extras::Pointer()", hover);
        Assert.DoesNotContain("void*", hover);
    }

    [Fact]
    public void InvalidReceiverDeclarationDoesNotHideLaterExtensions()
    {
        var (model, path, offset, _) = Analyze("using Extras; void F() { int n = 1; n.|; }", """
            namespace Extras;
            int Broken(int n, this int value) => value;
            int Double(this int value) => value * 2;
            """);
        Assert.Contains("Double", Labels(model.Completion(path, offset)));
    }

    [Fact]
    public void SignatureHelpCountsVisibleArgumentsAndIgnoresNestedCommas()
    {
        var (model, path, offset, _) = Analyze("using Extras; int Pair(int a, int b) => a + b; int main() { int n = 1; return n.Sum(Pair(1, 2), |3); }",
            "namespace Extras; int Sum(this int receiver, int first, int second) => receiver + first + second;");
        var signature = Json(model.SignatureHelp(path, offset));
        Assert.Equal(1, signature.GetProperty("activeParameter").GetInt32());
        Assert.Equal(2, signature.GetProperty("signatures")[0].GetProperty("parameters").GetArrayLength());
        Assert.DoesNotContain("receiver", signature.GetProperty("signatures")[0].GetProperty("label").GetString());
    }

    [Fact]
    public void ExtensionCallsAreClassifiedAsMethods()
    {
        var (model, path, _, source) = Analyze("using Extras; int main() { int n = 1; return n.Do|uble(); }",
            "namespace Extras; int Double(this int value) => value * 2;");
        int column = 0;
        var classifications = new Dictionary<int, string>();
        var data = Json(model.SemanticTokens(path)).GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        for (int i = 0; i < data.Length; i += 5)
        {
            Assert.Equal(0, data[i]);
            column += data[i + 1];
            classifications[column] = EditorModel.TokenTypes[data[i + 3]];
        }
        Assert.Equal("method", classifications[source.Text.LastIndexOf("Double", StringComparison.Ordinal)]);
    }

    [Fact]
    public void ConstrainedGenericCompletionsRequireACompatibleReceiver()
    {
        const string declarations = "interface I { int Get(); } struct Box : I { public int Get() => 1; } ";
        const string library = "namespace Extras; T Identity<T : I>(this T value) => Missing();";
        var (allowed, ap, ao, _) = Analyze("using Extras; " + declarations + "void F() { Box b = new Box(); b.|; }", library);
        Assert.Contains("Identity", Labels(allowed.Completion(ap, ao)));
        var (rejected, rp, ro, _) = Analyze("using Extras; " + declarations + "void F() { int n = 1; n.|; }", library);
        Assert.DoesNotContain("Identity", Labels(rejected.Completion(rp, ro)));
    }

    [Fact]
    public void CallableInstanceMembersKeepCompletionAndNavigationPriority()
    {
        const string declaration = "struct Box { public int Read() => 42; } ";
        const string library = "namespace Extras; int Read(this ref immutable(Box) value) => 1;";
        var (model, path, offset, _) = Analyze("using Extras; " + declaration + "int main() { Box box = new Box(); return box.Re|ad(); }", library);
        Assert.Contains("extension-main.gf", Json(model.Definition(path, offset)).ToString());
        Assert.Contains("Box.Read", Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString());
        var (completion, cp, co, _) = Analyze("using Extras; " + declaration + "void F() { Box box = new Box(); box.|; }", library);
        var items = Json(completion.Completion(cp, co)).GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("label").GetString() == "Read").ToArray();
        Assert.Single(items);
        Assert.DoesNotContain("Extras", items[0].GetProperty("detail").GetString());
    }
}

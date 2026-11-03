using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ReflectionEditorTests
{
    private static (EditorModel Model, SourceFile Source, int Offset) Analyze(string marked)
    {
        int offset = marked.IndexOf('|');
        var source = new SourceFile(Path.GetFullPath("reflection-editor.gf"), marked.Remove(offset, 1));
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return (new(snapshot, [source]), source, offset);
    }

    private static string HoverText(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions)
        .GetProperty("contents").GetProperty("value").GetString()!;

    private static string[] Labels(object value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions)
        .GetProperty("items").EnumerateArray().Select(item => item.GetProperty("label").GetString()!).ToArray();

    [Fact]
    public void TypeofExpressionShowsItsCheckedType()
    {
        var (model, source, offset) = Analyze("int main(){int value=42;type|of(value);return 0;}");
        string hover = HoverText(model.Hover(source.Path, offset));
        Assert.Contains("typeof(value)", hover);
        Assert.Contains("**Name:** int", hover);
        Assert.Contains("**Kind:** Primitive", hover);
    }

    [Fact]
    public void EnclosingTypeQueryHasHoverInAStaticMethod()
    {
        var (model, source, offset) = Analyze("struct Box { public static nuint Size() => type|of(*this).Size; } int main()=>0;");
        string hover = HoverText(model.Hover(source.Path, offset));
        Assert.Contains("typeof(*this)", hover);
        Assert.Contains("**Name:** Box", hover);
    }

    [Fact]
    public void SelfTypedMethodHoverShowsTheBoundSignature()
    {
        var (model, source, offset) = Analyze("struct Box { public int Value; public [:typeof(*this):] Copy()=>*this; } int main(){ Box value=new(); return value.Co|py().Value; }");
        string hover = HoverText(model.Hover(source.Path, offset));
        Assert.Contains("Box", hover);
        Assert.Contains("Copy()", hover);
        Assert.DoesNotContain("typeof", hover);
    }

    [Fact]
    public void ReflectionTypeIsAKeywordAndHasHover()
    {
        var (model, source, offset) = Analyze("const refle|ction info=reflect(int);int main()=>0;");
        Assert.Contains("reflection", HoverText(model.Hover(source.Path, offset)));
        var tokens = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        int column = 0;
        var kinds = new Dictionary<int, string>();
        for (int i = 0; i < tokens.Length; i += 5)
        {
            column += tokens[i + 1];
            kinds[column] = EditorModel.TokenTypes[tokens[i + 3]];
        }
        Assert.Equal("modifier", kinds[source.Text.IndexOf("reflection", StringComparison.Ordinal)]);
        Assert.Equal("modifier", kinds[source.Text.IndexOf("reflect(", StringComparison.Ordinal)]);
    }

    [Theory]
    [InlineData("1 + 2", "3")]
    [InlineData("reflect(int)", "reflect(int)")]
    public void CapturedValueHoverShowsTheValue(string expression, string expected)
    {
        var (model, source, offset) = Analyze("int main(){const reflection value=ref|lect(" + expression + ");return 0;}");
        string hover = HoverText(model.Hover(source.Path, offset));
        Assert.Contains("**Value:** " + expected, hover);
        Assert.DoesNotContain("**Name:**", hover);
    }

    [Fact]
    public void TypeReflectionCompletionHasApplicableProperties()
    {
        var (model, source, offset) = Analyze("int main()=> (int)reflect(int*).|Size;");
        var labels = Labels(model.Completion(source.Path, offset));
        Assert.Contains("Size", labels);
        Assert.Contains("Fields", labels);
        Assert.Contains("ElementType", labels);
        Assert.DoesNotContain("Offset", labels);
        Assert.DoesNotContain("Length", labels);
    }

    [Fact]
    public void ReflectedMemberCompletionUsesItsEntityKind()
    {
        var (model, source, offset) = Analyze("struct Box{public int Value;}const reflection field=reflect(Box).Fields[0];int main()=>(int)field.|Offset;");
        var labels = Labels(model.Completion(source.Path, offset));
        Assert.Contains("Offset", labels);
        Assert.Contains("DeclaringType", labels);
        Assert.Contains("Accessibility", labels);
        Assert.DoesNotContain("Fields", labels);
        Assert.DoesNotContain("ElementType", labels);
    }

    [Fact]
    public void MethodAndParameterReflectionHaveApplicableCompletion()
    {
        var (methodModel, methodSource, methodOffset) = Analyze("int Read(int value)=>value;const reflection method=reflect(Read);int main()=>(int)method.|ParameterCount;");
        var methodLabels = Labels(methodModel.Completion(methodSource.Path, methodOffset));
        Assert.Contains("Parameters", methodLabels);
        Assert.Contains("ReturnType", methodLabels);
        Assert.DoesNotContain("Offset", methodLabels);
        var (parameterModel, parameterSource, parameterOffset) = Analyze("int Read(int value)=>value;const reflection parameter=reflect(Read).Parameters[0];int main()=>(int)parameter.|Position;");
        var parameterLabels = Labels(parameterModel.Completion(parameterSource.Path, parameterOffset));
        Assert.Contains("Type", parameterLabels);
        Assert.Contains("Position", parameterLabels);
        Assert.Contains("IsConst", parameterLabels);
        Assert.DoesNotContain("Parameters", parameterLabels);
    }

    [Fact]
    public void ReflectionPropertyHoverShowsItsConstantValue()
    {
        var (model, source, offset) = Analyze("int main()=>(int)reflect(int).Si|ze;");
        Assert.Contains("(nuint)4", HoverText(model.Hover(source.Path, offset)));
        Assert.Null(model.Definition(source.Path, offset));
        var tokens = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        int column = 0;
        string? kind = null;
        for (int i = 0; i < tokens.Length; i += 5)
        {
            column += tokens[i + 1];
            if (column == source.Text.IndexOf("Size", StringComparison.Ordinal)) kind = EditorModel.TokenTypes[tokens[i + 3]];
        }
        Assert.Equal("property", kind);
    }

    [Fact]
    public void SplicedFieldNavigatesToItsOriginalDeclaration()
    {
        var (model, source, offset) = Analyze("struct Box{public int Value;public Box(int value){Value=value;}}const reflection field=reflect(Box).Fields[0];int main(){Box box=new(42);return box.[:fi|eld:];}");
        int declaration = source.Text.IndexOf("Value;", StringComparison.Ordinal);
        Assert.Equal(new Location(Workspace.FileUri(source.Path), Workspace.ToRange(source.Span(declaration, "Value".Length))),
            model.Definition(source.Path, offset));
        Assert.Contains("Box.Value", HoverText(model.Hover(source.Path, offset)));
    }

    [Fact]
    public void SplicedTypeNavigatesToItsOriginalDeclaration()
    {
        var (model, source, offset) = Analyze("struct Box{}const reflection info=reflect(Box);int main(){[:in|fo:] value=new();return 0;}");
        int declaration = source.Text.IndexOf("Box{", StringComparison.Ordinal);
        Assert.Equal(new Location(Workspace.FileUri(source.Path), Workspace.ToRange(source.Span(declaration, "Box".Length))),
            model.Definition(source.Path, offset));
        Assert.Contains("struct Box", HoverText(model.Hover(source.Path, offset)));
    }
}

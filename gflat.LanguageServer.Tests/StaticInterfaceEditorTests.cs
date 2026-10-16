using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class StaticInterfaceEditorTests
{
    [Fact]
    public void StaticInterfaceAndItsImplicitStaticMembersHaveAccurateHovers()
    {
        string path = Path.GetFullPath("static-interface.gf");
        string code = "public static interface IContract { int Read(); int Value { get; } }";
        SourceFile[] files = [new(path, code)];
        var snapshot = new AnalysisSnapshot(files, new CompilationOptions { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, files);
        string Hover(string name)
        {
            var hover = JsonSerializer.SerializeToElement(model.Hover(path, code.IndexOf(name, StringComparison.Ordinal)), Protocol.JsonOptions);
            return hover.GetProperty("contents").GetProperty("value").GetString()!;
        }
        Assert.Contains("static interface IContract", Hover("IContract"));
        Assert.Contains("static int IContract::Read", Hover("Read"));
        Assert.Contains("static int IContract::Value", Hover("Value"));
    }

    [Fact]
    public void TypeParameterCompletionIncludesImplicitStaticContractMembers()
    {
        string path = Path.GetFullPath("static-interface-completion.gf");
        string code = "static interface IContract { int Read(); } int Use<P : IContract>() => P::Read();";
        SourceFile[] files = [new(path, code)];
        var snapshot = new AnalysisSnapshot(files, new CompilationOptions { NoStdLib = true });
        Assert.Empty(snapshot.Diagnostics);
        var model = new EditorModel(snapshot, files);
        int offset = code.LastIndexOf("Read", StringComparison.Ordinal);
        var completion = JsonSerializer.SerializeToElement(model.Completion(path, offset), Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "Read");
    }
}

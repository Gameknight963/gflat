using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class IpNetworkLastAddressEditorTests
{
    [Fact]
    public void ImmutableNumericEndpointUsesItsIpAddressPropertyTypeAndActualSource()
    {
        const string text="using std;IpAddress Observe(immutable(IpNetwork)* network)nothrow=>network.LastAddress;";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories).Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var library=files.Single(file=>Path.GetFileName(file.Path)=="IpNetwork.gf");var source=new SourceFile(Path.GetFullPath("ip-network-endpoint-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var access=Assert.Single(snapshot.Nodes.Where(node=>node.Span.SourceId==source.Id).Select(node=>node.Node).OfType<MemberAccessExpression>());
        Assert.Equal("std::IpAddress",snapshot.Checker!.DisplayTypeName(snapshot.Checker.GetType(access)));
        var model=new EditorModel(snapshot,files);int use=text.IndexOf("LastAddress",StringComparison.Ordinal);
        Assert.Contains(Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="LastAddress");
        string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;Assert.Contains("IpAddress",hover);Assert.Contains("LastAddress",hover);Assert.Contains("immutable get nothrow",hover);Assert.DoesNotContain('$',hover);
        const string anchor="public IpAddress LastAddress";int declaration=library.Text.IndexOf(anchor,StringComparison.Ordinal);Assert.True(declaration>=0);
        var expected=model.Definition(library.Path,declaration+anchor.IndexOf("LastAddress",StringComparison.Ordinal));Assert.NotNull(expected);Assert.Equal(expected,model.Definition(source.Path,use));
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}

using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Uses the actual network relation methods and their overload family.
public sealed class IpNetworkRelationsEditorTests
{
    [Theory]
    [InlineData("Contains","IpAddress","address",2)]
    [InlineData("Contains","IpNetwork","other",2)]
    [InlineData("Overlaps","IpNetwork","other",1)]
    public void ImmutableRelationsPresentTheSelectedSourceSignatureAndCompleteContainsFamily(string member,string targetType,string parameter,int count)
    {
        string text="using std;bool Observe(immutable(IpNetwork)* receiver,immutable("+targetType+")* target) nothrow=>receiver."+member+"(target);";
        string root=Path.Combine(AppContext.BaseDirectory,"StandardLibrary","core");
        string[] names=["IpNetwork.gf","IpAddress.gf","IEquatable.gf","IComparable.gf","IHashable.gf","Hashing/primitive_hashing.gf"];
        var sources=names.Select(name=>Path.Combine(root,name)).Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var source=new SourceFile(Path.GetFullPath("ip-network-relations-editor.gf"),text);sources.Add(source);
        var snapshot=new AnalysisSnapshot(sources);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var call=Assert.Single(snapshot.Nodes.Where(node=>node.Span.SourceId==source.Id).Select(node=>node.Node).OfType<CallExpression>());
        Assert.Equal("bool",TypeChecker.TypeName(snapshot.Checker!.GetType(call)));
        var model=new EditorModel(snapshot,sources);int use=text.IndexOf(member+"(",StringComparison.Ordinal);
        Assert.Contains(Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()==member);
        string expectedParameter="immutable("+targetType+")* "+parameter;
        string hover=Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("bool",hover);Assert.Contains(member+"(",hover);Assert.Contains(expectedParameter,hover);Assert.Contains("nothrow",hover);
        Assert.DoesNotContain('$',hover);
        SourceFile library=sources.Single(file=>Path.GetFileName(file.Path)=="IpNetwork.gf");
        string marker=member+"(immutable("+targetType+")*";
        int declaration=library.Text.IndexOf(marker,StringComparison.Ordinal);Assert.True(declaration>=0,"Missing actual source "+marker);
        var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(source.Path,use));
        Assert.Contains("IpNetwork.gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));
        var help=Json(model.SignatureHelp(source.Path,text.LastIndexOf("target",StringComparison.Ordinal)+1));
        var signatures=help.GetProperty("signatures");Assert.Equal(count,signatures.GetArrayLength());
        string selected=signatures[help.GetProperty("activeSignature").GetInt32()].GetProperty("label").GetString()!;
        // Check overload identity independently of signature-label qualification.
        Assert.Contains(expectedParameter,selected.Replace("std::","",StringComparison.Ordinal));Assert.Contains("bool",selected);Assert.Equal(0,help.GetProperty("activeParameter").GetInt32());
        foreach(var signature in signatures.EnumerateArray())Assert.DoesNotContain('$',signature.GetProperty("label").GetString()!);
        if(member=="Contains")
        {
            Assert.Contains(signatures.EnumerateArray(),signature=>signature.GetProperty("label").GetString()!.Replace("std::","",StringComparison.Ordinal).Contains("immutable(IpAddress)* address",StringComparison.Ordinal));
            Assert.Contains(signatures.EnumerateArray(),signature=>signature.GetProperty("label").GetString()!.Replace("std::","",StringComparison.Ordinal).Contains("immutable(IpNetwork)* other",StringComparison.Ordinal));
        }
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}

using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class GenericOverloadEditorTests
{
    private const string Library="""
        namespace Library{
            alias Word=int;
            public Word Free(Word value)=>value;
            public T Free<T>(T value)=>value;
            class Box<O>{
                public static Word Pick(Word value)=>value;
                public static T Pick<T>(T value)=>value;
            }
            public Word Touch(this ref Word value,bool tag)=>value;
            public T Touch<T>(this ref T value,bool tag)=>value;
        }
        """;

    [Theory]
    [InlineData("free",false)]
    [InlineData("free",true)]
    [InlineData("static",false)]
    [InlineData("static",true)]
    [InlineData("extension",false)]
    [InlineData("extension",true)]
    public void MixedFamiliesPresentTheSelectedSourceDeclarationAndAllSignatures(string context,bool generic)
    {
        string name=context switch{"free"=>"Free","static"=>"Pick",_=>"Touch"};
        string type=generic?"long":"int";
        string argument=generic?"3l":"3";
        string expression=context switch{
            "free"=>$"Library::Free({argument})",
            "static"=>$"Library::Box<byte>::Pick({argument})",
            _=>"number.Touch(true)"
        };
        string program=$"using Library;alias Word=long;{type} Test(){{{type} number={argument};return {expression};}}";
        var (model,_,caller,library)=Analyze(program,Library);
        int use=program.IndexOf(name,StringComparison.Ordinal);
        string hover=Hover(model,caller,use);
        Assert.Contains(generic?$"{name}<long>":name+"(",hover);
        Assert.Contains(generic?"long ":"Word ",hover);
        Assert.DoesNotContain("T value",hover);
        if(context!="extension")Assert.Contains(generic?"long value":"Word value",hover);
        else{
            Assert.Contains("bool tag",hover);
            Assert.DoesNotContain("this ref",hover);
        }
        int declaration=Library.IndexOf(generic?name+"<T>":name+"(",StringComparison.Ordinal);
        AssertDefinition(model,caller,use,library,declaration);

        int argumentOffset=program.IndexOf('(',use)+1;
        var help=Json(model.SignatureHelp(caller.Path,argumentOffset));
        var signatures=help.GetProperty("signatures");
        // The non-generic extension requires ref int and cannot borrow long storage.
        bool onlyGenericReceiver=context=="extension"&&generic;
        Assert.Equal(onlyGenericReceiver?1:2,signatures.GetArrayLength());
        Assert.Equal(0,help.GetProperty("activeParameter").GetInt32());
        string selected=signatures[help.GetProperty("activeSignature").GetInt32()].GetProperty("label").GetString()!;
        Assert.Contains(generic?$"{name}<long>":name+"(",selected);
        Assert.Contains(generic?"long ":"Word ",selected);
        Assert.Contains(signatures.EnumerateArray(),item=>item.GetProperty("label").GetString()!.Contains(name+"<",StringComparison.Ordinal));
        if(!onlyGenericReceiver)
            Assert.Contains(signatures.EnumerateArray(),item=>item.GetProperty("label").GetString()!.Contains(name+"(",StringComparison.Ordinal));

        var rich=Json(model.Hover(caller.Path,use,new VisualStudioHover(Json(new{}))));
        var runs=rich.GetProperty("_vs_rawContent").GetProperty("Elements")[0].GetProperty("Elements")[0].GetProperty("Runs");
        if(generic)
            Assert.Contains(runs.EnumerateArray(),run=>run.GetProperty("Text").GetString()=="long"&&run.GetProperty("ClassificationTypeName").GetString()=="keyword");
        else{
            var alias=runs.EnumerateArray().First(run=>run.GetProperty("Text").GetString()=="Word");
            AssertTarget(alias.GetProperty("_gflat_target"),library,Library.IndexOf("Word",StringComparison.Ordinal));
        }
    }

    [Fact]
    public void KnownCallbackSignatureSelectsTheGenericMemberOfAStaticOwnerFamily()
    {
        const string libraryText="namespace Library{class Factory<O>{public static int Make(bool value)=>0;public static T Make<T>(T value)=>value;}}";
        const string program="long Test(){long(long)* callback=&Library::Factory<byte>::Make;return callback(3l);}";
        var (model,_,caller,library)=Analyze(program,libraryText);
        int use=program.IndexOf("Make",StringComparison.Ordinal);
        Assert.Contains("Make<long>(long value)",Hover(model,caller,use));
        AssertDefinition(model,caller,use,library,libraryText.IndexOf("Make<T>",StringComparison.Ordinal));
        string template=Hover(model,library,libraryText.IndexOf("Make<T>",StringComparison.Ordinal));
        Assert.Contains("T value",template);
        Assert.DoesNotContain("long value",template);
    }

    [Fact]
    public void AmbiguousGenericCallsDoNotEraseLaterSelectedMetadataOrNamespaceColors()
    {
        const string program="int Cross<T>(T first,int second)=>1;int Cross<T>(int first,T second)=>2;int Broken()=>Cross(0,0);long Healthy()=>Library::Free(3l);";
        var (model,snapshot,caller,library)=Analyze(program,Library,expectErrors:true);
        Assert.Contains(snapshot.Diagnostics,item=>item.Severity==DiagnosticSeverity.Error&&item.Message.Contains("ambiguous",StringComparison.OrdinalIgnoreCase));
        int use=program.IndexOf("Free",StringComparison.Ordinal);
        Assert.Contains("Free<long>(long value)",Hover(model,caller,use));
        AssertDefinition(model,caller,use,library,Library.IndexOf("Free<T>",StringComparison.Ordinal));
        var help=Json(model.SignatureHelp(caller.Path,program.IndexOf("(3l)",StringComparison.Ordinal)+1));
        Assert.Equal(2,help.GetProperty("signatures").GetArrayLength());
        Assert.Contains("Free<long>",help.GetProperty("signatures")[help.GetProperty("activeSignature").GetInt32()].GetProperty("label").GetString());
        Assert.Equal("namespace",Classification(model,caller,program.IndexOf("Library",StringComparison.Ordinal)));
    }

    private static (EditorModel Model,AnalysisSnapshot Snapshot,SourceFile Caller,SourceFile Library) Analyze(string program,string library,bool expectErrors=false)
    {
        var caller=new SourceFile(Path.GetFullPath("generic-overload-editor-main.gf"),program);
        var extra=new SourceFile(Path.GetFullPath("generic-overload-editor-library.gf"),library);
        var snapshot=new AnalysisSnapshot([extra,caller],new(){NoStdLib=true});
        if(!expectErrors)Assert.DoesNotContain(snapshot.Diagnostics,item=>item.Severity==DiagnosticSeverity.Error);
        return(new(snapshot,[extra,caller]),snapshot,caller,extra);
    }

    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
    private static string Hover(EditorModel model,SourceFile source,int offset)
        =>Json(model.Hover(source.Path,offset)).GetProperty("contents").GetProperty("value").GetString()!;
    private static void AssertDefinition(EditorModel model,SourceFile source,int use,SourceFile target,int declaration)
        =>AssertTarget(Json(model.Definition(source.Path,use)),target,declaration);
    private static void AssertTarget(JsonElement definition,SourceFile target,int offset)
    {
        Assert.Contains(Path.GetFileName(target.Path),definition.GetProperty("uri").GetString());
        string preceding=target.Text[..offset];
        var start=definition.GetProperty("range").GetProperty("start");
        Assert.Equal(preceding.Count(character=>character=='\n'),start.GetProperty("line").GetInt32());
        Assert.Equal(offset-(preceding.LastIndexOf('\n')+1),start.GetProperty("character").GetInt32());
    }
    private static string? Classification(EditorModel model,SourceFile source,int offset)
    {
        var data=Json(model.SemanticTokens(source.Path)).GetProperty("data").EnumerateArray().Select(item=>item.GetInt32()).ToArray();
        int[] starts=[0,..source.Text.Select((character,index)=>(character,index)).Where(item=>item.character=='\n').Select(item=>item.index+1)];
        int line=0,column=0;
        for(int i=0;i<data.Length;i+=5){
            line+=data[i];column=data[i]==0?column+data[i+1]:data[i+1];
            if(starts[line]+column==offset)return EditorModel.TokenTypes[data[i+3]];
        }
        return null;
    }
}

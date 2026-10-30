using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Exercise the actual standard value, including its optional constructor arguments.
public sealed class TimeOfDayEditorTests
{
    [Fact]
    public void TimeOfDayTypeOptionalConstructorAndMetadataRetainTheirConcreteSourceContracts()
    {
        const string text="using Standard; using Standard::Time;int Probe(){TimeOfDay value=new(12,34);TimeOfDay midnight=new();int hour=value.Hour;long ticks=value.Ticks;return hour;}";
        var (model,source,library,_)=Analyze(text);
        int type=text.IndexOf("TimeOfDay",StringComparison.Ordinal);
        string typeHover=Hover(model,source,type);Assert.Contains("struct",typeHover);Assert.Contains("TimeOfDay",typeHover);Assert.DoesNotContain('$',typeHover);
        Assert.Equal("struct",Color(model,source,type));
        int typeDeclaration=library.Text.IndexOf("struct TimeOfDay",StringComparison.Ordinal);Assert.True(typeDeclaration>=0);
        Definition(model,source,type,library,typeDeclaration+"struct ".Length);
        int creation=text.IndexOf("new(12,34)",StringComparison.Ordinal);
        var help=Json(model.SignatureHelp(source.Path,text.IndexOf("34",creation,StringComparison.Ordinal)));
        Assert.Equal(1,help.GetProperty("activeParameter").GetInt32());
        var signatures=help.GetProperty("signatures");
        var selected=signatures[help.GetProperty("activeSignature").GetInt32()];
        Assert.Equal(4,selected.GetProperty("parameters").GetArrayLength());
        string label=selected.GetProperty("label").GetString()!;
        Assert.Contains("TimeOfDay",label);Assert.Contains("int hour",label);Assert.Contains("int minute",label);
        Assert.Matches(@"\bint\s+second\s*=\s*0\b",label);Assert.Matches(@"\bint\s+millisecond\s*=\s*0\b",label);Assert.DoesNotContain('$',label);
        foreach(var member in new[]{(Name:"Hour",Type:"int"),(Name:"Ticks",Type:"long")})
        {
            int use=text.IndexOf("value."+member.Name,StringComparison.Ordinal)+"value.".Length;
            Assert.Single(Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()==member.Name);
            string hover=Hover(model,source,use);
            Assert.Contains(member.Type,hover);Assert.Contains(member.Name,hover);Assert.Contains("immutable",hover);Assert.Contains("nothrow",hover);Assert.DoesNotContain('$',hover);
            int declaration=library.Text.IndexOf(member.Type+" "+member.Name+" ",StringComparison.Ordinal);Assert.True(declaration>=0);
            Definition(model,source,use,library,declaration+member.Type.Length+1);
        }
        Assert.Contains("int hour",Hover(model,source,text.LastIndexOf("hour",StringComparison.Ordinal)));
    }

    [Fact]
    public void CountedAndCStringParsingSelectTheirDistinctNoThrowOverloads()
    {
        const string text="using Standard; using Standard::Time;void Probe(){TimeOfDay output=new();bool counted=TimeOfDay::TryParse(\"12:34:56\",8,&output);bool terminated=TimeOfDay::TryParse(\"12:34:56\",&output);}";
        var (model,source,library,snapshot)=Analyze(text);
        int counted=text.IndexOf("TryParse",StringComparison.Ordinal),terminated=text.LastIndexOf("TryParse",StringComparison.Ordinal);
        Assert.Single(Json(model.Completion(source.Path,counted)).GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="TryParse");
        foreach(var row in new[]{(Use:counted,Arity:3),(Use:terminated,Arity:2)})
        {
            string hover=Hover(model,source,row.Use);Assert.Contains("bool",hover);Assert.Contains("TryParse",hover);Assert.Contains("nothrow",hover);Assert.DoesNotContain('$',hover);
            var method=Assert.Single(snapshot.Nodes.Where(node=>node.Span.SourceId==library.Id).Select(node=>node.Node).OfType<MethodDeclaration>(),
                declaration=>declaration.Name=="TryParse"&&declaration.Parameters.Count==row.Arity);
            int declaration=library.Text.IndexOf("TryParse",method.Span.Start,StringComparison.Ordinal);
            Definition(model,source,row.Use,library,declaration);
            int output=text.IndexOf("&output",row.Use,StringComparison.Ordinal)+1;
            var help=Json(model.SignatureHelp(source.Path,output));var signatures=help.GetProperty("signatures");Assert.Equal(2,signatures.GetArrayLength());
            Assert.Equal(row.Arity-1,help.GetProperty("activeParameter").GetInt32());
            var selected=signatures[help.GetProperty("activeSignature").GetInt32()];Assert.Equal(row.Arity,selected.GetProperty("parameters").GetArrayLength());
            string label=selected.GetProperty("label").GetString()!;
            Assert.Contains("bool",label);Assert.Contains("TryParse",label);Assert.Contains("TimeOfDay",label);Assert.Contains("nothrow",label);Assert.DoesNotContain('$',label);
        }
        Assert.NotEqual(model.Definition(source.Path,counted),model.Definition(source.Path,terminated));
    }

    [Fact]
    public void ImmutableArithmeticSelectsBothAddOverloadsAndNavigatesToTheirDeclarations()
    {
        const string text="using Standard; using Standard::Time;void Probe(){TimeOfDay value=new(23,30);immutable(TimeOfDay)* view=&value;TimeSpan duration=TimeSpan::FromHours(2);int days=99;TimeOfDay first=view.Add(duration);TimeOfDay second=view.Add(duration,ref days);bool inside=view.IsBetween(first,second);}";
        var (model,source,library,snapshot)=Analyze(text);
        int first=text.IndexOf("Add(duration)",StringComparison.Ordinal),second=text.IndexOf("Add(duration,",StringComparison.Ordinal);
        foreach(var row in new[]{(Use:first,Arity:1),(Use:second,Arity:2)})
        {
            string hover=Hover(model,source,row.Use);
            Assert.Contains("TimeOfDay",hover);Assert.Contains("Add",hover);Assert.Contains("immutable",hover);Assert.Contains("nothrow",hover);Assert.DoesNotContain('$',hover);
            var method=Assert.Single(snapshot.Nodes.Where(node=>node.Span.SourceId==library.Id).Select(node=>node.Node).OfType<MethodDeclaration>(),declaration=>declaration.Name=="Add"&&declaration.Parameters.Count==row.Arity);
            int declaration=library.Text.IndexOf("Add(",method.Span.Start,StringComparison.Ordinal);
            Definition(model,source,row.Use,library,declaration);
            int argument=row.Arity==1?text.IndexOf("duration",row.Use,StringComparison.Ordinal):text.IndexOf("days",row.Use,StringComparison.Ordinal);
            var help=Json(model.SignatureHelp(source.Path,argument));var signatures=help.GetProperty("signatures");
            Assert.Equal(2,signatures.GetArrayLength());Assert.Equal(row.Arity-1,help.GetProperty("activeParameter").GetInt32());
            var selected=signatures[help.GetProperty("activeSignature").GetInt32()];Assert.Equal(row.Arity,selected.GetProperty("parameters").GetArrayLength());
            string label=selected.GetProperty("label").GetString()!;Assert.Contains("TimeSpan value",label);Assert.Contains("nothrow",label);Assert.DoesNotContain('$',label);
            if(row.Arity==2)Assert.Contains("ref int wrappedDays",label);
        }
        Assert.NotEqual(model.Definition(source.Path,first),model.Definition(source.Path,second));
        int between=text.IndexOf("IsBetween",StringComparison.Ordinal);
        string betweenHover=Hover(model,source,between);Assert.Contains("bool",betweenHover);Assert.Contains("immutable",betweenHover);Assert.Contains("nothrow",betweenHover);
        Definition(model,source,between,library,library.Text.IndexOf("IsBetween(",StringComparison.Ordinal));
        Assert.Single(Json(model.Completion(source.Path,between)).GetProperty("items").EnumerateArray(),item=>item.GetProperty("label").GetString()=="IsBetween");
    }


    private static (EditorModel Model,SourceFile Source,SourceFile Library,AnalysisSnapshot Snapshot) Analyze(string text)
    {
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        SourceFile library=files.Single(file=>Path.GetFileName(file.Path)=="TimeOfDay.gf");
        var source=new SourceFile(Path.GetFullPath("time-of-day-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        return(new EditorModel(snapshot,files),source,library,snapshot);
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
    private static string Hover(EditorModel model,SourceFile file,int offset)
        =>Json(model.Hover(file.Path,offset)).GetProperty("contents").GetProperty("value").GetString()!;
    private static void Definition(EditorModel model,SourceFile file,int use,SourceFile library,int declaration)
    {
        var expected=model.Definition(library.Path,declaration);Assert.NotNull(expected);
        Assert.Equal(expected,model.Definition(file.Path,use));Assert.Contains("TimeOfDay.gf",JsonSerializer.Serialize(expected,Protocol.JsonOptions));
    }
    private static string Color(EditorModel model,SourceFile file,int offset)
    {
        int[] data=Json(model.SemanticTokens(file.Path)).GetProperty("data").EnumerateArray().Select(value=>value.GetInt32()).ToArray();int column=0;
        for(int i=0;i<data.Length;i+=5){column=data[i]==0?column+data[i+1]:data[i+1];if(column==offset)return EditorModel.TokenTypes[data[i+3]];}
        Assert.Fail("Missing TimeOfDay semantic token");return "";
    }
}

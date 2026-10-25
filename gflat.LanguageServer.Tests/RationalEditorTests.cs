using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class RationalEditorTests
{
    [Fact]
    public void OwningFractionMembersAndBorrowedTextConversionHaveTheirActualDefinitions()
    {
        const string text="using std; using std::Numerics;void Probe(){Rational first=new Rational(1l,2l);Rational second=new(1l,3l);BigInteger top=first.Numerator;BigInteger bottom=first.Denominator;Rational sum=first.Add(&second);String rendered=sum.ToString();}";
        var files=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        var rational=files.Single(file=>Path.GetFileName(file.Path)=="Rational.gf");
        var conversion=files.Single(file=>Path.GetFileName(file.Path)=="FormattableString.gf");
        var source=new SourceFile(Path.GetFullPath("rational-editor.gf"),text);files.Add(source);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,files);
        foreach(var row in new[]{(Name:"Numerator",Type:"BigInteger",Library:rational),
            (Name:"Denominator",Type:"BigInteger",Library:rational),
            (Name:"Add",Type:"Rational",Library:rational),
            (Name:"ToString",Type:"String",Library:conversion)}){
            int member=text.IndexOf("."+row.Name,StringComparison.Ordinal)+1;Assert.True(member>0);
            var items=Json(model.Completion(source.Path,member)).GetProperty("items");
            Assert.Contains(items.EnumerateArray(),item=>item.GetProperty("label").GetString()==row.Name);
            string hover=Json(model.Hover(source.Path,member)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(row.Type,hover);Assert.Contains(row.Name,hover);Assert.DoesNotContain('$',hover);
            int declaration=row.Library.Text.IndexOf(row.Name,StringComparison.Ordinal);Assert.True(declaration>=0);
            var expected=model.Definition(row.Library.Path,declaration);Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,member));
        }
        int constructor=text.IndexOf("new Rational(",StringComparison.Ordinal)+"new Rational(1l,".Length;
        var help=Json(model.SignatureHelp(source.Path,constructor));
        Assert.Equal(1,help.GetProperty("activeParameter").GetInt32());
        var selected=help.GetProperty("signatures")[help.GetProperty("activeSignature").GetInt32()];
        Assert.Equal(2,selected.GetProperty("parameters").GetArrayLength());
        string label=selected.GetProperty("label").GetString()!;
        Assert.Contains("long numerator",label);Assert.Contains("long denominator",label);
    }
    private static JsonElement Json(object? value)=>JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}

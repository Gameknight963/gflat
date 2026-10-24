using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

// Valid source should not offer another file's private implementation details.
public sealed class PrivateFunctionCompletionTests
{
    [Theory]
    [InlineData("private int Hidden()=>1;")]
    [InlineData("private T Hidden<T>(T value)=>value;")]
    public void PrivateNamespaceFunctionsAreOfferedOnlyWithinTheirDeclaringFile(string declaration)
    {
        var library=new SourceFile(Path.GetFullPath("private-completion-library.gf"),
            "namespace Lib;"+declaration+" public long Healthy()=>42;"
            +"long Inside(){long bare=Healthy();long qualified=Lib::Healthy();return bare+qualified;}");
        var consumer=new SourceFile(Path.GetFullPath("private-completion-consumer.gf"),
            "using Lib;long Read(){long bare=Healthy();long qualified=Lib::Healthy();return bare+qualified;}");
        var snapshot=new AnalysisSnapshot([library,consumer],new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,[library,consumer]);
        int healthyDeclaration=library.Text.IndexOf("Healthy",StringComparison.Ordinal);
        var healthyTarget=model.Definition(library.Path,healthyDeclaration);Assert.NotNull(healthyTarget);
        foreach(var file in new[]{library,consumer})
        foreach(string call in new[]{"bare=Healthy","qualified=Lib::Healthy"})
        {
            int start=file.Text.IndexOf(call,StringComparison.Ordinal);Assert.True(start>=0);
            int use=file.Text.IndexOf("Healthy",start,StringComparison.Ordinal);
            var labels=JsonSerializer.SerializeToElement(model.Completion(file.Path,use),Protocol.JsonOptions)
                .GetProperty("items").EnumerateArray().Select(item=>item.GetProperty("label").GetString()).ToArray();
            Assert.Single(labels,label=>label=="Healthy");
            if(file==library)Assert.Single(labels,label=>label=="Hidden");
            else Assert.DoesNotContain("Hidden",labels);
            Assert.Equal(healthyTarget,model.Definition(file.Path,use));
        }
    }
}

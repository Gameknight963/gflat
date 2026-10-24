using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class QualifiedDeclarationPrefixEditorTests
{
    [Fact]
    public void RelativeReplacementNamespacePrefixNavigatesToItsLexicalOwnerAcrossFiles()
    {
        var global=new SourceFile(Path.GetFullPath("a-global-owner.gf"),"namespace Api { public int Other()=>0; }");
        var owner=new SourceFile(Path.GetFullPath("b-lexical-owner.gf"),"namespace Outer { namespace Api { public expect int Read(); } }");
        var source=new SourceFile(Path.GetFullPath("c-replacement.gf"),"namespace Outer { replace int Api::Read()=>42; }");
        SourceFile[] sources=[global,owner,source];
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        int prefix=source.Text.IndexOf("Api::Read",StringComparison.Ordinal);
        string hover=JsonSerializer.Serialize(model.Hover(source.Path,prefix+1),Protocol.JsonOptions);
        Assert.Contains("Outer::Api",hover);
        string definition=JsonSerializer.Serialize(model.Definition(source.Path,prefix+1),Protocol.JsonOptions);
        Assert.Contains("b-lexical-owner.gf",definition);
        Assert.DoesNotContain("a-global-owner.gf",definition);
    }

    [Fact]
    public void OrdinaryCallPrefixesUseLexicalNamespaceAndExplicitGlobalControl()
    {
        var global=new SourceFile(Path.GetFullPath("a-global-call.gf"),"namespace Api { public int Read()=>7; }");
        var owner=new SourceFile(Path.GetFullPath("b-lexical-call.gf"),"namespace Outer { namespace Api { public int Read()=>42; } }");
        var source=new SourceFile(Path.GetFullPath("c-call.gf"),"namespace Outer { int Run()=>Api::Read()+global::Api::Read(); }");
        SourceFile[] sources=[global,owner,source];
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        int relative=source.Text.IndexOf("Api::Read",StringComparison.Ordinal);
        int absolute=source.Text.LastIndexOf("Api::Read",StringComparison.Ordinal);
        Assert.Contains("b-lexical-call.gf",JsonSerializer.Serialize(model.Definition(source.Path,relative+1),Protocol.JsonOptions));
        Assert.Contains("a-global-call.gf",JsonSerializer.Serialize(model.Definition(source.Path,absolute+1),Protocol.JsonOptions));
    }

    [Fact]
    public void TypeAliasAndGenericParameterPrefixesKeepPriorityOverGlobalNamespace()
    {
        var global=new SourceFile(Path.GetFullPath("a-global-prefix.gf"),"namespace Api { public int Read()=>7; }");
        var source=new SourceFile(Path.GetFullPath("b-type-prefix.gf"),"""
            namespace Models { static class Api { public static int Read()=>42; } int Run()=>Api::Read(); }
            namespace Aliases { alias Api=Models::Api; int Run()=>Api::Read(); }
            int Forward<Api>()=>Api::Read();
            int Local(){alias Api=Models::Api;return Api::Read()+global::Api::Read();}
            int main()=>Forward<Models::Api>()+Local();
            """);
        SourceFile[] sources=[global,source];
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        int[] declarations=[
            source.Text.IndexOf("class Api",StringComparison.Ordinal)+6,
            source.Text.IndexOf("alias Api",StringComparison.Ordinal)+6,
            source.Text.IndexOf("Forward<Api>",StringComparison.Ordinal)+8,
            source.Text.LastIndexOf("alias Api",StringComparison.Ordinal)+6
        ];
        int at=0;
        for(int i=0;i<4;i++){
            at=source.Text.IndexOf("Api::Read",at,StringComparison.Ordinal);
            Assert.True(at>=0);
            var definition=Assert.IsType<Location>(model.Definition(source.Path,at+1));
            Assert.Equal(new Location(Workspace.FileUri(source.Path),Workspace.ToRange(source.Span(declarations[i],3))),definition);
            at+=3;
        }
        int absolute=source.Text.LastIndexOf("Api::Read",StringComparison.Ordinal);
        var globalDefinition=Assert.IsType<Location>(model.Definition(source.Path,absolute+1));
        Assert.Equal(new Location(Workspace.FileUri(global.Path),Workspace.ToRange(global.Span(global.Text.IndexOf("Api",StringComparison.Ordinal),3))),globalDefinition);
    }
}

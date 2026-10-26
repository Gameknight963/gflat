using System.Text.Json;
using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DeclarationPhaseIsolationTests
{
    public static IEnumerable<object[]> InvalidDeclarations()
    {
        yield return ["missing-body-brace", "Duplicate declaration", """
            namespace Mutations {
                alias Word=int;
                class Base {public int Value;public Base(Word* value){Value=*value;}}
                class Derived<T>:Base {public Derived(int* value):base(value){}}
                int Run()int value=42;Derived<int> item=new(&value);return item.Value;}
            }
            """];
        yield return ["namespace-open-brace", "Implementation of alias", """
            namespace Mutations
                interface IProvider {static int Read();}
                static class Provider:IProvider {public static int Read()=>42;}
                weak alias Selected:IProvider=Provider;
                int Run()=>Selected::Read();
            }
            """];
        yield return ["interface-method-name", "can only contain method declarations", """
            namespace Mutations {
                interface IProvider {static int ();}
                static class Provider:IProvider {public static int Read()=>42;}
                weak alias Selected:IProvider=Provider;
                int Run()=>Selected::Read();
            }
            """];
        yield return ["interface-method-paren", "can only contain method declarations", """
            namespace Mutations {
                interface IProvider {static int Read);}
                static class Provider:IProvider {public static int Read()=>42;}
                weak alias Selected:IProvider=Provider;
                int Run()=>Selected::Read();
            }
            """];
        yield return ["provider-name", "Provider alias declarations must be declared in a namespace", """
            namespace Mutations {
                interface IProvider {static int Read();}
                static class :IProvider {public static int Read()=>42;}
                weak alias Selected:IProvider=Provider;
                int Run()=>Selected::Read();
            }
            """];
        yield return ["provider-base", "Implementation of alias", """
            namespace Mutations {
                interface IProvider {static int Read();}
                static class Provider: {public static int Read()=>42;}
                weak alias Selected:IProvider=Provider;
                int Run()=>Selected::Read();
            }
            """];
        yield return ["provider-close-brace", "Provider alias declarations must be declared in a namespace", """
            namespace Mutations {
                interface IProvider {static int Read();}
                static class Provider:IProvider {public static int Read()=>42;
                weak alias Selected:IProvider=Provider;
                int Run()=>Selected::Read();
            }
            """];
        yield return ["alias-target", "Implementation of alias", """
            namespace Mutations {
                interface IProvider {static int Read();}
                static class Provider:IProvider {public static int Read()=>42;}
                weak alias Selected:IProvider=;
                int Run()=>Selected::Read();
            }
            """];
    }

    [Theory]
    [MemberData(nameof(InvalidDeclarations))]
    public void InvalidDeclarationPhasesPreserveUnrelatedBindingsTokensAndNavigation(string name,string diagnostic,string text)
    {
        var broken=new SourceFile(Path.GetFullPath("a-recovery-phase.gf"),text);
        var healthy=new SourceFile(Path.GetFullPath("z-recovery-healthy.gf"),"""
            namespace Stable {
                public struct Item {
                    public int Value;
                    public int Read(int input){int inside=input;return inside;}
                }
                public int Echo(int argument)=>argument;
            }
            int Healthy(){Stable::Item item=new Stable::Item();int answer=Stable::Echo(item.Read(42));return answer;}
            """);
        SourceFile[] files=[broken,healthy];
        Assert.Throws<TypeCheckException>(()=>Compiler.Check(files,options:new(){NoStdLib=true}));
        var baseline=new AnalysisSnapshot([healthy],new(){NoStdLib=true});
        Assert.DoesNotContain(baseline.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error);
        var snapshot=new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error&&error.FilePath==healthy.Path);
        var local=Assert.Single(snapshot.Nodes.Where(n=>n.Span.SourceId==healthy.Id).Select(n=>n.Node).OfType<VariableDeclaration>(),n=>n.Name=="inside");
        Assert.NotNull(snapshot.Checker);
        Assert.Equal("int",TypeChecker.TypeName(snapshot.Checker.GetType(local)));
        var call=Assert.Single(snapshot.Nodes.Where(n=>n.Span.SourceId==healthy.Id).Select(n=>n.Node).OfType<CallExpression>(),n=>n.Callee is MemberAccessExpression{Member:"Read"});
        Assert.NotNull(snapshot.Checker.GetResolvedCall(call));
        var originalModel=new EditorModel(baseline,[healthy]);
        var model=new EditorModel(snapshot,files);
        Assert.Equal(Json(originalModel.SemanticTokens(healthy.Path)),Json(model.SemanticTokens(healthy.Path)));
        int read=healthy.Text.LastIndexOf("Read",StringComparison.Ordinal);
        var expectedDefinition=originalModel.Definition(healthy.Path,read);
        Assert.NotNull(expectedDefinition);
        Assert.Equal(Json(expectedDefinition),Json(model.Definition(healthy.Path,read)));
        var options=new HoverOptions(ShowTypeLayout:false);
        var expectedHover=originalModel.Hover(healthy.Path,read,options:options);
        Assert.NotNull(expectedHover);
        Assert.Equal(Json(expectedHover),Json(model.Hover(healthy.Path,read,options:options)));
        Assert.Contains(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error&&error.FilePath==broken.Path&&error.Message.Contains(diagnostic,StringComparison.Ordinal));
        if(name=="missing-body-brace") {
            var conflict=Assert.Single(snapshot.Diagnostics,error=>error.Message.Contains("Duplicate declaration of 'item'",StringComparison.Ordinal));
            Assert.Single(conflict.RelatedLocations);
            Assert.Equal(broken.Id,conflict.RelatedLocations[0].SourceId);
        }
    }

    private static string Json(object? value)=>JsonSerializer.Serialize(value,Protocol.JsonOptions);
}

using gflat.ast;
using gflat.comptime;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class NameofGenericMethodTests
{
    private const string Box = "struct Box<T>{public T value;}";

    [Theory]
    [InlineData("class", false, false)]
    [InlineData("class", false, true)]
    [InlineData("class", true, false)]
    [InlineData("class", true, true)]
    [InlineData("struct", false, false)]
    [InlineData("struct", false, true)]
    [InlineData("struct", true, false)]
    [InlineData("struct", true, true)]
    public void GenericMemberNamesDoNotBindReturnTypes(string kind, bool staticMethod, bool globalT)
    {
        string modifier = staticMethod ? "static " : "";
        string target = staticMethod ? "Helpers::Make" : "helper.Make";
        string source = (globalT ? "class T{public int Bogus;}" : "") + Box + kind + " Helpers{" +
            "public " + modifier + "Box<T> Make<T>(Box<T> value)=>value;}" +
            "int main(){Helpers*? helper;nameof(" + target + ");return 0;}";
        AssertNames(source, "Make");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifiedAndImportedFreeMethodGroupsAreOnlyNamed(bool globalT)
        => AssertNames("using N;" + (globalT ? "class T{public int Bogus;}" : "") + Box +
            "namespace N{public Box<T> Make<T>(Box<T> value)=>value;}" +
            "int main(){nameof(N::Make);nameof(Make);return 0;}", "Make", "Make");

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void PrivateAccessIsCheckedBeforeTheOpenReturnType(string kind)
    {
        string source = "class T{public int Bogus;}" + Box + kind +
            " Helpers{private Box<T> Make<T>(Box<T> value)=>value;}" +
            "int main(){Helpers*? helper;nameof(helper.Make);return 0;}";
        var error = Assert.Single(Compiler.Analyze([new SourceFile("private-nameof.gf", source)], new() { NoStdLib = true }),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("Cannot access private member 'Make'", error.Message);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void MethodGroupsCannotBecomeNameofReceivers(string kind)
    {
        string source = Box + kind + " Helpers{public Box<T> Make<T>(Box<T> value)=>value;}" +
            "int main(){Helpers*? helper;nameof(helper.Make.value);return 0;}";
        var error = Assert.Single(Compiler.Analyze([new SourceFile("nameof-group-chain.gf", source)], new() { NoStdLib = true }),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("A method group cannot be the receiver of a nameof member", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NamesProduceCStringValuesWithoutEvaluatingReceiversOrGetters(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            using N;
            extern int printf(readonly(char)* format,...);
            struct Box<T>{public T value;}
            class Helpers{
                public Box<T> Make<T>(Box<T> value)=>value;
                public static Box<T> Static<T>(Box<T> value)=>value;
            }
            struct Worker{public Box<T> Make<T>(Box<T> value)=>value;}
            namespace N{public Box<T> Free<T>(Box<T> value)=>value;}
            struct Payload{public int value;}
            struct Container{
                public Payload Field;
                public Payload Property{get{throw new Exception();}}
            }
            int main(){
                Helpers*? helper;Worker*? worker;Container*? container;
                printf("%s %s %s %s %s %s %s",nameof(helper.Make),nameof(worker.Make),
                    nameof(Helpers::Static),nameof(N::Free),nameof(Free),
                    nameof(container.Field.value),nameof(container.Property.value));
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Make Make Static Free Free value value", result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    private static void AssertNames(string text, params string[] expected)
    {
        var source = new SourceFile("nameof-generic-method.gf", text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var names = snapshot.Nodes.Select(entry => entry.Node).OfType<NameofExpression>().ToArray();
        Assert.Equal(expected.Length, names.Length);
        for (int index = 0; index < names.Length; index++)
        {
            Assert.True(snapshot.Checker!.TryGetConstValue(names[index], out var value));
            Assert.Equal(expected[index], Assert.IsType<ConstValue.String>(value).Value);
            var type = Assert.IsType<PointerTypeExpression>(snapshot.Checker.GetType(names[index]));
            Assert.True(type.IsReadOnly);
            Assert.False(type.IsNullable);
            Assert.Equal("char", Assert.IsType<NamedTypeExpression>(type.Inner).Name);
        }
        Assert.Contains(snapshot.Checker!.Symbols.GenericStructs.Values, declaration => declaration.SourceName == "Box");
        Assert.DoesNotContain(snapshot.Checker.Symbols.Structs.Values, info => info.Declaration?.SourceName == "Box");
    }
}

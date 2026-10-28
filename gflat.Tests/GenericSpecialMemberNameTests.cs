using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class GenericSpecialMemberNameTests
{
    public static IEnumerable<object[]> InvalidDeclarations()
    {
        foreach (string kind in new[] { "struct", "class" })
            foreach (bool destructor in new[] { false, true })
                foreach (int context in new[] { 0, 1, 2 })
                    yield return [kind, destructor, context];
    }

    [Theory]
    [MemberData(nameof(InvalidDeclarations))]
    public void TypeArgumentNamesCannotNameConstructorsOrDestructors(string kind, bool destructor, int context)
    {
        var (source, owner) = Program(kind, context, "public " + (destructor ? "~" : "") + "Payload(){}");
        var diagnostics = Compiler.Analyze([new SourceFile("special-member-name.gf", source)], new() { NoStdLib = true });
        string expected = destructor
            ? $"Destructor name '~Payload' does not match {kind} name '{owner}'"
            : $"Constructor name 'Payload' does not match {kind} name '{owner}'";
        var errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Contains(errors, error => error.Message == expected);
        Assert.All(errors, error => {
            Assert.DoesNotContain('$', error.Message);
            Assert.Equal("special-member-name.gf", error.FilePath);
        });
    }

    [Theory]
    [InlineData("struct", 0)]
    [InlineData("class", 0)]
    [InlineData("struct", 1)]
    [InlineData("class", 1)]
    [InlineData("struct", 2)]
    [InlineData("class", 2)]
    public void TypeArgumentNamesCannotNameStaticConstructors(string kind, int context)
    {
        var (source, _) = Program(kind, context, "static Payload(){}");
        var diagnostics = Compiler.Analyze([new SourceFile("static-constructor-name.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("Static constructor name must match its containing type", error.Message);
        Assert.Equal("static-constructor-name.gf", error.FilePath);
        Assert.Equal("static Payload(){}", source.Substring(error.Span.Start, error.Span.Length));
    }

    [Theory]
    [InlineData("struct", 0)]
    [InlineData("class", 0)]
    [InlineData("struct", 1)]
    [InlineData("class", 1)]
    [InlineData("struct", 2)]
    [InlineData("class", 2)]
    public void SourceOwnerNamesSurviveSpecializationAndNestedRecreation(string kind, int context)
    {
        var (source, _) = Program(kind, context, "static Box(){} public Box(){} public ~Box(){}");
        var file = new SourceFile("valid-special-member.gf", source);
        var (ast, checker) = Compiler.Check(file, options: new() { NoStdLib = true });
        var identities = ast.Members.Select(node => node switch {
            ClassDeclaration { IsGeneric: false } declaration => declaration.Name,
            StructDeclaration { IsGeneric: false } declaration => declaration.Name,
            _ => ""
        }).Where(name => name.Contains("Box"));
        var declarations = identities.Select(checker.GetTypeDeclaration).Select(node => node switch {
            ClassDeclaration declaration => declaration.SourceName,
            StructDeclaration declaration => declaration.SourceName,
            _ => null
        }).ToArray();
        Assert.NotEmpty(declarations);
        Assert.All(declarations, name => Assert.Equal("Box", name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateAndVirtualChecksKeepTheirPriorityAndLineSpans(bool duplicate)
    {
        string first = duplicate ? "public ~Box(){}\n" : "";
        string source = "namespace Lib{\nstruct Box{\n" + first + "public virtual ~Payload(){}\n}\n}\nint main()=>0;";
        var diagnostics = Compiler.Analyze([new SourceFile("priority.gf", source)], new() { NoStdLib = true });
        string expected = duplicate ? "Struct 'Lib::Box' already defines a destructor" : "Struct 'Lib::Box' destructor cannot be virtual";
        var error = Assert.Single(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(expected, error.Message);
        Assert.Equal(duplicate ? 4 : 3, error.Span.Line);
        Assert.Equal("priority.gf", error.FilePath);
        Assert.Equal("public virtual ~Payload(){}", source.Substring(error.Span.Start, error.Span.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidGenericConstructorsAndDestructorsStillRun(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            static class State{public static int total=0;}
            namespace Lib{
                class Outer{public class Payload{}}
                struct Box<T>{static Box(){State::total+=1000000;}public Box(){State::total+=1;}~Box(){State::total+=10;}}
                class Container<T>{static Container(){State::total+=2000000;}public Container(){State::total+=100;}~Container(){State::total+=1000;}}
                class Family<U>{public struct Child<T>{static Child(){State::total+=4000000;}public Child(){State::total+=10000;}~Child(){State::total+=100000;}}}
            }
            int main(){
                {
                    Lib::Box<Lib::Outer::Payload> a=new Lib::Box<Lib::Outer::Payload>();
                    Lib::Container<Lib::Outer::Payload> b=new Lib::Container<Lib::Outer::Payload>();
                    Lib::Family<int>::Child<Lib::Outer::Payload> c=new Lib::Family<int>::Child<Lib::Outer::Payload>();
                }
                if(State::total!=7111111)return 1;
                return 0;
            }
            """, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        Assert.Empty(result.StandardOutput);
    }

    private static (string Source, string Owner) Program(string kind, int context, string members)
    {
        string box = kind + " Box<T>{" + members + "}";
        return context switch {
            0 => ("class Payload{} " + box + " void Use(Box<Payload>* value){} int main()=>0;", "Box<Payload>"),
            1 => ("namespace Lib{class Payload{} " + box + "} void Use(Lib::Box<Lib::Payload>* value){} int main()=>0;", "Lib::Box<Lib::Payload>"),
            _ => ("class Outer{public class Payload{}} namespace Lib{class Family<U>{public " + box + "}} void Use(Lib::Family<int>::Box<Outer::Payload>* value){} int main()=>0;", "Lib::Family<int>::Box<Outer::Payload>")
        };
    }
}

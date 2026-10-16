using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class GenericHierarchyContextTests
{
    [Theory]
    [InlineData("", "Base", "")]
    [InlineData("Demo", "Base", "Demo::")]
    [InlineData("Demo", "Demo::Base", "Demo::")]
    public void GenericClassBasesHaveCanonicalLayoutAndDispatch(string ns, string baseName, string prefix)
    {
        string declarations = $$"""
            class Base { public long prefix; public virtual int Read()=>40; }
            class Box<T> : {{baseName}} { public T value; public override int Read()=>42; }
            """;
        if (ns.Length > 0) declarations = "namespace " + ns + " { " + declarations + " }";
        Assert.Equal(42, CompilerTestHelper.Run(declarations + $$"""
            int main(){
                {{prefix}}Box<int> value=new {{prefix}}Box<int>();
                {{prefix}}Base* view=&value;
                if(sizeof({{prefix}}Box<int>)!=24 || alignof({{prefix}}Box<int>)!=8) return 1;
                return view.Read();
            }
            """).ExitCode);
    }

    [Theory]
    [InlineData("struct", "IRead")]
    [InlineData("struct", "Demo::IRead")]
    [InlineData("class", "IRead")]
    [InlineData("class", "Demo::IRead")]
    public void GenericInstanceInterfacesSupportConversionsOutsideTheirNamespace(string kind, string contract)
        => Assert.Equal(42, CompilerTestHelper.Run($$"""
            namespace Demo {
                interface IRead { int Read(); }
                {{kind}} Box<T> : {{contract}} { public int Read()=>42; }
            }
            int main(){Demo::Box<int> value=new Demo::Box<int>();Demo::IRead* view=&value;return view.Read();}
            """).ExitCode);

    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    [InlineData("static class")]
    public void GenericStaticInterfaceImplementationsSatisfyConstraints(string kind)
        => Assert.Equal(42, CompilerTestHelper.Run($$"""
            namespace Demo {
                static interface IFactory { int Read(); }
                {{kind}} Factory<T> : IFactory { public static int Read()=>42; }
            }
            int Invoke<T : Demo::IFactory>()=>T::Read();
            int main()=>Invoke<Demo::Factory<int>>();
            """).ExitCode);

    [Fact]
    public void GenericHierarchyUsesItsDeclarationImportsAndIgnoresCallerAliases()
        => Assert.Equal(42, CompilerTestHelper.Run(new[] {
            new SourceFile("types.gf", "namespace Types {public class Base {public long prefix;} public interface IRead{int Read();}}"),
            new SourceFile("box.gf", "using Types; namespace Library {public class Box<T>:Base,IRead {public T value;public int Read()=>42;}}"),
            new SourceFile("main.gf", "int main(){alias Base=byte;alias IRead=byte;Library::Box<int> value=new Library::Box<int>();Types::Base* parent=&value;Types::IRead* view=&value;if(sizeof(Library::Box<int>)!=24)return 1;return view.Read();}")
        }).ExitCode);

    [Theory]
    [InlineData("")]
    [InlineData("class Base {public virtual int Read()=>0;}")]
    public void NestedClassesInsideAGenericOwnerResolveTheirSiblingBase(string globalDeclaration)
        => Assert.Equal(42, CompilerTestHelper.Run(globalDeclaration + """
            namespace Demo {
                class Owner<T> {
                    public class Base {public T value;public virtual int Read()=>40;}
                    public class Derived : Base {public override int Read()=>42;}
                }
            }
            int main(){Demo::Owner<int>::Derived value=new Demo::Owner<int>::Derived();Demo::Owner<int>::Base* view=&value;return view.Read();}
            """).ExitCode);

    [Theory]
    [InlineData("static class Base {} class Box<T>:Base {}", "static class")]
    [InlineData("class First {} class Second {} class Box<T>:First,Second {}", "multiple classes")]
    [InlineData("struct Box<T>:Missing {}", "unknown interface")]
    public void InvalidGenericHierarchiesKeepSourceDiagnostics(string declaration, string expected)
    {
        var error=Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "namespace Demo {"+declaration+"} int main(){Demo::Box<int> value=new Demo::Box<int>();return 0;}"));
        Assert.Contains(expected,error.Description,StringComparison.OrdinalIgnoreCase);
    }
}

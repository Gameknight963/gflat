using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class GlobalTypeQualificationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RootedGenericNestedBaseAndInterfaceEscapeLexicalNamespacesAndDispatchCorrectly(int optimization)
    {
        const string code = """
            namespace A{
                public struct Token{public int Value;}
                public interface I<T>{T Read();}
                public struct Outer<T>{
                    public class Parent{public T Item;public virtual int Tag()=>1;}
                }
            }
            namespace Client{
                namespace A{
                    public struct Token{public long Wrong;}
                    public interface I<T>{long Missing();}
                    public struct Outer<T>{public class Parent{public long Wrong;}}
                }
                class Derived:global::A::Outer<global::A::Token>::Parent,global::A::I<global::A::Token>{
                    public override int Tag()=>42;
                    public global::A::Token Read()=>Item;
                }
                struct Value:global::A::I<global::A::Token>{
                    public global::A::Token Read(){global::A::Token result=new();result.Value=7;return result;}
                }
                int Run(){
                    Derived owner=new();owner.Item.Value=13;
                    global::A::Outer<global::A::Token>::Parent* parent=&owner;
                    global::A::I<global::A::Token>* contract=&owner;
                    if(parent.Tag()!=42||contract.Read().Value!=13)return 1;
                    Value value=new();global::A::I<global::A::Token>* other=&value;
                    if(other.Read().Value!=7)return 2;return 0;
                }
            }
            int main()=>Client::Run();
            """;
        SourceFile[] sources = [
            new("Exception.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Exception.gf"))),
            new("global-types.gf", code)
        ];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Fact]
    public void RootedTypeReferencesWorkThroughAliasesSignaturesFieldsAndOrdinaryTypeOperators()
        => Compiler.Check("""
            namespace Root{public struct Item{public int Value;}}
            alias RootAlias=Root::Item;
            namespace Client{
                namespace Root{public struct Item{public long Wrong;}}
                alias RootAlias=Root::Item;
                alias Selected=global::RootAlias;
                struct Holder{public global::Root::Item Field;}
                global::Root::Item Identity(global::Root::Item input){
                    global::Root::Item converted=(global::Root::Item)input;return converted;
                }
                void Pointers(global::Root::Item*? optional,readonly(global::Root::Item)* view){}
                int Layout()=>alignof(global::Root::Item);
                int Probe(){
                    Selected value=new global::Root::Item();value.Value=17;
                    Holder holder=new();holder.Field=Identity(value);
                    Pointers(&holder.Field,&holder.Field);return holder.Field.Value;
                }
            }
            int main()=>Client::Probe()-17;
            """, options: new() { NoStdLib = true, NoBuiltins = true });

    [Fact]
    public void RootedSimpleTypeDoesNotBecomeAnEnclosingGenericParameterOrLocalType()
        => Compiler.Check("""
            struct T{public int RootValue;}
            namespace Client{
                struct T{public long Wrong;}
                struct Box<T>{public global::T Value;}
                int Probe(){Box<int> value=new();value.Value.RootValue=42;return value.Value.RootValue;}
            }
            """, options: new() { NoStdLib = true, NoBuiltins = true });

    [Fact]
    public void MissingRootedNamesDoNotFallBackToLexicalTypesNamespacesOrGenerics()
    {
        (string Code, string Name, bool RequireUnknown)[] cases = [
            ("namespace Client{struct OnlyLocal{} struct Holder{public global::OnlyLocal Value;}}", "OnlyLocal", true),
            ("namespace Client{namespace Missing{public interface I{}} struct Holder:global::Missing::I{}}", "Missing", true),
            ("namespace Client{struct OnlyLocal<T>{} struct Holder{public global::OnlyLocal<int> Value;}}", "OnlyLocal", true),
            ("namespace OnlyNamespace{} namespace Client{class OnlyNamespace{} struct Holder{public global::OnlyNamespace Value;}}", "OnlyNamespace", false),
            ("namespace OnlyNamespace{} namespace Client{class OnlyNamespace{} void Probe(){new global::OnlyNamespace();}}", "OnlyNamespace", false)
        ];
        foreach (var (code, name, requireUnknown) in cases)
        {
            var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(code,
                options: new() { NoStdLib = true, NoBuiltins = true }));
            if (requireUnknown) Assert.Contains("Unknown", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(name, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RootQualifierDoesNotCreateAnotherIdentityForTheSameGenericInterface()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            using A;
            namespace A{public interface I<T>{}}
            struct Duplicate:global::A::I<int>,I<int>{}
            """, options: new() { NoStdLib = true, NoBuiltins = true }));
        Assert.Contains("Duplicate base type", error.Description, StringComparison.Ordinal);
    }
}

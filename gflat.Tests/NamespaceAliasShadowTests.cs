using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NamespaceAliasShadowTests
{
    [Fact]
    public void NamespaceAliasShadowsImportedGenericTypeWithExtensionDeclaration()
        => Compiler.Check("""
            using Library;
            namespace Library {
                interface IProtocol {int Marker{get;}}
                struct Box<T> {public int Marker;}
                struct Other<T> {public int Marker;}
                void Second<T>(this ref Box<T> target){}
            }
            namespace Caller {
                alias Box=Library::Other<int>;
                int Inspect(Box target)=>target.Marker;
            }
            """, options: new(){NoStdLib=true});

    [Theory]
    [InlineData(true,"Box")]
    [InlineData(false,"Box")]
    [InlineData(true,"Selected")]
    public void NamespaceAliasBindsItsTargetBeforeImportedGenericTypes(bool import,string aliasName)
        => Compiler.Check($$"""
            {{(import?"using Library;":"")}}
            namespace Library {
                struct Box<T> {public byte Wrong;}
                struct Other<T> {public int OtherMarker;}
            }
            namespace Caller {
                alias {{aliasName}}=Library::Other<int>;
                int Inspect({{aliasName}} target)=>target.OtherMarker;
            }
            """, options: new(){NoStdLib=true});

    [Fact]
    public void NamespaceAliasShadowsImportedOrdinaryType()
        => Compiler.Check("""
            using Library;
            namespace Library {struct Box {public byte Wrong;}}
            struct Other {public int OtherMarker;}
            namespace Caller {
                alias Box=Other;
                int Inspect(Box target)=>target.OtherMarker;
            }
            """, options: new(){NoStdLib=true});

    [Fact]
    public void NearerNamespaceTypeShadowsOuterAlias()
        => Compiler.Check("""
            struct Other {public byte Wrong;}
            namespace Outer {
                alias Box=Other;
                namespace Inner {
                    struct Box {public int InnerMarker;}
                    int Inspect(Box target)=>target.InnerMarker;
                }
            }
            """, options: new(){NoStdLib=true});

    [Fact]
    public void NearerNamespaceAliasShadowsOuterType()
        => Compiler.Check("""
            struct Other {public int OtherMarker;}
            namespace Outer {
                struct Box {public byte Wrong;}
                namespace Inner {
                    alias Box=Other;
                    int Inspect(Box target)=>target.OtherMarker;
                }
            }
            """, options: new(){NoStdLib=true});

    [Fact]
    public void TypeArgumentsOnNamespaceAliasDoNotSelectImportedGenericType()
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check("""
            using Library;
            namespace Library {struct Box<T> {public byte Wrong;}}
            namespace Caller {
                alias Box=int;
                int Inspect(Box<int> target)=>0;
            }
            """, options: new(){NoStdLib=true}));
        Assert.Contains("Unknown generic type",error.Description);
    }
}

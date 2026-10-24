using gflat.ast;

namespace gflat.Tests;

public sealed class BoundCastTargetTests
{
    private const string Types="""
        namespace A {
            public struct Box<T> {
                public struct Item {
                    public T Value;
                    public Item(T value){Value=move value;}
                }
            }
        }
        """;
    private const string ImportedPointerProgram="""
        using A;
        int main() {
            Box<int>::Item item=new(42);
            void* storage=(void*)&item;
            Box<int>::Item* imported=(Box<int>::Item*)storage;
            A::Box<int>::Item* qualified=(A::Box<int>::Item*)storage;
            if(imported!=qualified||imported.Value!=42)return 1;
            imported.Value=73;
            if(qualified.Value!=73||item.Value!=73)return 2;
            Box<long>::Item wide=new(4294967297l);
            void* wideStorage=(void*)&wide;
            Box<long>::Item* second=(Box<long>::Item*)wideStorage;
            A::Box<long>::Item* secondQualified=(A::Box<long>::Item*)wideStorage;
            if(second!=secondQualified||second.Value!=4294967297l)return 3;
            return 0;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImportedNestedGenericRawPointerCastsMatchFullyQualifiedTargets(int optimization)
        =>Run(ImportedPointerProgram,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NamespaceAndGenericLocalAliasesShadowGlobalTypeForLiteralAndNonliteralCasts(int optimization)
        =>Run("""
            struct Word {public byte Marker;}
            namespace A {
                public alias Word=long;
                public Word Widen(int value)=>(Word)value;
                public Word Literal()=>(Word)4294967297;
                public Word Negative()=>(Word)(-4294967297);
            }
            T Identity<T>(T value) {
                alias Word=T;
                return (Word)value;
            }
            int main() {
                int input=-17;
                if(A::Widen(input)!=-17l||A::Literal()!=4294967297l||A::Negative()!=-4294967297l)return 1;
                long wide=4294967297l;
                if(Identity<long>(wide)!=wide||Identity<int>(input)!=input)return 2;
                extralong huge=(extralong)1<<100;
                if(Identity<extralong>(huge)!=huge)return 3;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ResolvedPointerAliasesRetainReadOnlyAndNullableTargetKinds(int optimization)
        =>Run("""
            using A;
            alias ReadView=readonly(Box<int>::Item)*;
            alias DeepView=immutable(Box<int>::Item)*;
            alias Optional=Box<int>::Item*?;
            alias Required=Box<int>::Item*;
            int main() {
                Box<int>::Item item=new(42);void* storage=(void*)&item;
                ReadView read=(ReadView)storage;DeepView deep=(DeepView)storage;
                readonly(Box<int>::Item)* direct=(readonly(Box<int>::Item)*)storage;
                Optional optional=(Optional)storage;
                Required required=(Required)optional;
                if(optional==null||read.Value!=42||deep.Value!=42||direct.Value!=42||required.Value!=42)return 1;
                required.Value=73;if(read.Value!=73||deep.Value!=73||direct.Value!=73||optional!.Value!=73)return 2;
                void*? empty=null;Optional missing=(Optional)empty;
                Box<int>::Item*? explicitMissing=(Box<int>::Item*?)empty;
                if(missing!=null||explicitMissing!=null)return 3;
                return 0;
            }
            """,optimization);

    [Fact]
    public void EmissionUsesCheckedIdentityWithoutReplacingTheImportedSourceTargetSyntax()
    {
        var (unit,checker)=Compiler.Check(Sources(ImportedPointerProgram));
        var main=unit.Members.OfType<MethodDeclaration>().Single(method=>method.Name=="main");
        var variable=main.Body!.Statements.OfType<VariableDeclaration>().Single(node=>node.Name=="imported");
        var cast=Assert.IsType<CastExpression>(variable.Initializer);
        var syntax=Assert.IsType<PointerTypeExpression>(cast.TargetType);
        var nested=Assert.IsType<NestedTypeExpression>(syntax.Inner);
        var parent=Assert.IsType<NamedTypeExpression>(nested.Parent);
        Assert.Equal("Box",parent.Name);Assert.Null(parent.Namespace);Assert.Equal("Item",nested.Member);
        Assert.Single(parent.TypeArguments);
        SourceSpan parentSpan=parent.NameSpan;SourceSpan memberSpan=nested.NameSpan;
        var emitter=new LlvmEmitter(checker,internalizeFunctions:true);
        unit.Accept(emitter);Assert.NotEmpty(emitter.GetOutput());
        Assert.Same(syntax,cast.TargetType);Assert.Same(parent,nested.Parent);
        Assert.Equal("Box",parent.Name);Assert.Null(parent.Namespace);
        Assert.Equal(parentSpan,parent.NameSpan);Assert.Equal(memberSpan,nested.NameSpan);
    }

    private static SourceFile[] Sources(string program)=>[
        new SourceFile("cast-types.gf",Types),new SourceFile("cast-consumer.gf",program)
    ];
    private static void Run(string program,int optimization) {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Bound cast target exit {result.ExitCode}: {result.StandardError}");
    }
}

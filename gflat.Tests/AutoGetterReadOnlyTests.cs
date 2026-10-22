using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class AutoGetterReadOnlyTests
{
    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void MutablePointerAutoGettersPreserveTheirReturnCapability(string kind)
    {
        string source=$$$"""
            {{{kind}}} Holder {public int* Value{get;} public Holder(int* value){Value=value;}}
            int main(){int value=1;Holder holder=new Holder(&value);*holder.Value=42;return value;}
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("int", "41", "holder.Value+1")]
    [InlineData("readonly(int)*", "&value", "*holder.Value+1")]
    public void SafeResultsRemainCallableOnReadOnlyReceivers(string type,string argument,string result)
    {
        string source=$$$"""
            struct Holder {public {{{type}}} Value{get;} public Holder({{{type}}} value){Value=value;}}
            int main(){int value=41;immutable(Holder) holder=new Holder({{{argument}}});return {{{result}}};}
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("int*", "&value")]
    [InlineData("readonly(int)*", "&value")]
    public void ReceiverInferenceUsesTheBoundGenericResult(string argument,string value)
    {
        string source=$$$"""
            struct Holder<T>{public T Value{get;} public Holder(T value){Value=value;}}
            int main(){int value=42;Holder<{{{argument}}}> holder=new Holder<{{{argument}}}>({{{value}}});return *holder.Value;}
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManagedResultCapabilityControlsReadOnlyReceiverAccess(bool readOnly)
    {
        string type=readOnly ? "readonly(int)^" : "int^";
        string source=$$$"""
            extern void*? malloc(nuint size);
            alias GC=TestCollector;
            static class TestCollector {
                public static void* Allocate(nuint size,nuint alignment)=>malloc(size)!;
                public static void ReferenceAdded(void* slot) nothrow {}
                public static void ReferenceRemoved(void* slot) nothrow {}
            }
            struct Holder<T>{public T Value{get;}public Holder(T value){Value=value;}}
            int main(){
                int^ value=new^ int(42);
                {{{(readOnly ? "immutable(Holder<"+type+">)" : "Holder<"+type+">")}}} holder=new Holder<{{{type}}}>(value);
                {{{(readOnly ? "" : "*holder.Value=42;")}}}
                return *holder.Value;
            }
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("int", "42", "holder.Value")]
    [InlineData("readonly(int)*", "&value", "*holder.Value")]
    public void GenericSafeResultsRemainCallableThroughReadOnlyReceivers(string type,string argument,string result)
    {
        string source=$$$"""
            struct Holder<T>{public T Value{get;}public Holder(T value){Value=value;}}
            int main(){int value=42;immutable(Holder<{{{type}}}>) holder=new Holder<{{{type}}}>({{{argument}}});return {{{result}}};}
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void GenericSpecializationsInferIndependentReceiverCapabilities()
    {
        const string source="""
            struct Holder<T>{public T Value{get;}public Holder(T value){Value=value;}}
            int main(){
                int value=1;
                Holder<int*> mutable=new Holder<int*>(&value);
                *mutable.Value=40;
                immutable(Holder<int>) scalar=new Holder<int>(1);
                immutable(Holder<readonly(int)*>) pointer=new Holder<readonly(int)*>(&value);
                return scalar.Value+*pointer.Value+1;
            }
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void WritablePointerAggregatesCanBeReturnedThroughMutableReceivers()
    {
        const string source="""
            struct Buffer {public int* data;public Buffer(int* pointer){data=pointer;}}
            struct Holder<T>{public T Value{get;}public Holder(T value){Value=value;}}
            int main(){int value=1;Holder<Buffer> holder=new Holder<Buffer>(new Buffer(&value));Buffer copy=holder.Value;*copy.data=42;return value;}
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void AReadOnlyReceiverCannotUseAWritablePointerGetter()
    {
        const string source="""
            struct Holder {public int* Value{get;}public Holder(int* value){Value=value;}}
            int main(){int value=42;Holder holder=new Holder(&value);immutable(Holder)* view=&holder;return *view.Value;}
            """;
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source));
        Assert.Contains("on immutable instance",error.Message);
    }

    [Fact]
    public void ShallowReceiverCanUsePointerAutoGetterWithoutReassigningItsStorage()
    {
        const string source="""
            struct Holder{public int* Value{get;}public Holder(int* value){Value=value;}}
            int main(){int value=1;Holder holder=new(&value);readonly(Holder)* view=&holder;*view.Value=42;return value;}
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void RecursiveAutoPropertyStorageKeepsTheNormalLayoutDiagnostic()
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check("struct Loop{public Loop Value{get;}} int main()=>0;"));
        Assert.Contains("Inline value layout is recursive",error.Message);
    }

    [Fact]
    public void RecoveryCanInspectAForwardReadOfInvalidAutoPropertyStorage()
    {
        const string source="""
            int Read(Loop* value){Loop copy=value.Value;return 0;}
            struct Loop{public Loop Value{get;}}
            int main()=>0;
            """;
        var errors=Compiler.Analyze([new SourceFile("auto-cycle.gf",source)],new(){NoStdLib=true});
        Assert.Contains(errors,error=>error.Message.Contains("Inline value layout is recursive"));
    }

    [Fact]
    public void ExplicitReadOnlyStillRejectsAMutablePointerReturn()
    {
        const string source="""
            struct Holder {public int* Value{immutable get;}public Holder(int* value){Value=value;}}
            int main(){int value=42;Holder holder=new Holder(&value);return *holder.Value;}
            """;
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source));
        Assert.Contains("Cannot return",error.Message);
    }

    [Fact]
    public void PointerAliasUsesItsDeclarationFileImports()
    {
        SourceFile[] source=[
            new("aliases.gf","using Models;namespace Library {alias Pointer=Item*;struct Holder{public Pointer Value{get;}public Holder(Pointer pointer){Value=pointer;}}}"),
            new("models.gf","namespace Models;struct Item{public int value;}"),
            new("main.gf","int main(){Models::Item item=new Models::Item();Library::Holder holder=new Library::Holder(&item);holder.Value.value=42;return item.value;}")
        ];
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void ReentrantSpecializationCanReadAForwardAggregateProperty()
    {
        const string source="""
            struct Holder {public Box<int> Value{get;}public Holder(Box<int> value){Value=value;}}
            struct Box<T> {
                public int* pointer;
                public Box(int* value){pointer=value;}
                public static int Read(Holder* holder)=>*holder.Value.pointer;
            }
            int main(){int value=42;Holder holder=new Holder(new Box<int>(&value));return Box<int>::Read(&holder);}
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void ForwardCallsBindTheGetterBeforeItsBody()
    {
        const string source="""
            int Read(Holder* holder)=>*holder.Value;
            int main(){int value=42;Holder holder=new Holder(&value);return Read(&holder);}
            struct Holder {public int* Value{get;}public Holder(int* value){Value=value;}}
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void GenericScalarGetterImplementsAReadOnlyInterfaceContract()
    {
        const string source="""
            interface IValue {int Value{immutable get;}}
            struct Holder<T>:IValue {public T Value{get;}public Holder(T value){Value=value;}}
            int main(){Holder<int> holder=new Holder<int>(42);immutable(IValue)* view=(immutable(IValue)*)&holder;return view.Value;}
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void MutablePointerGetterImplementsAMutableInterfaceContract()
    {
        const string source="""
            interface IValue {int* Value{get;}}
            struct Holder<T>:IValue {public T Value{get;}public Holder(T value){Value=value;}}
            int main(){int value=42;Holder<int*> holder=new Holder<int*>(&value);IValue* view=(IValue*)&holder;return *view.Value;}
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void MutablePointerGetterCannotImplementAReadOnlyInterfaceContract()
    {
        const string source="""
            interface IValue {int* Value{immutable get;}}
            struct Holder<T>:IValue {public T Value{get;}public Holder(T value){Value=value;}}
            int main(){int value=42;Holder<int*> holder=new Holder<int*>(&value);return 0;}
            """;
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source));
        Assert.Contains("must be marked readonly",error.Message);
    }

    [Fact]
    public void VirtualAutoGettersKeepTheirExplicitReceiverContract()
    {
        const string source="""
            class Holder {public virtual int Value{get;set;}}
            int main(){Holder holder=new Holder();immutable(Holder)* view=&holder;return view.Value;}
            """;
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source));
        Assert.Contains("on immutable instance",error.Message);
    }
}

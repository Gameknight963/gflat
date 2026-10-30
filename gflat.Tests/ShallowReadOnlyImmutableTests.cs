using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ShallowReadOnlyImmutableTests
{
    private static void Check(string source) => Compiler.Check(source, options: new() { NoStdLib = true });

    [Theory]
    [InlineData("readonly(Box)* view=&box;view.data[0]=42;")]
    [InlineData("readonly(Box)* view=&box;view.target.Change();")]
    [InlineData("readonly(Box)* view=&box;view.target.value=42;")]
    [InlineData("readonly(Box)* view=&box;int* escaped=view.data;escaped[0]=42;")]
    public void ShallowViewProtectsStorageButAllowsReferencedObjects(string action)
        => Check(Preamble + "void Test(){int item=0;Target target=new();Box box=new(&item,&target);" + action + "}");

    [Theory]
    [InlineData("readonly(Box)* view=&box;view.data=&item;")]
    [InlineData("readonly(Box)* view=&box;view.inlineValue=42;")]
    [InlineData("immutable(Box)* view=&box;view.data[0]=42;")]
    [InlineData("immutable(Box)* view=&box;view.target.Change();")]
    [InlineData("immutable(Box)* view=&box;view.target.value=42;")]
    [InlineData("immutable(Box)* view=&box;int* escaped=view.data;")]
    [InlineData("immutable(Box)* view=&box;readonly(Box)* escaped=view;")]
    public void ProtectedStorageAndDeepGraphCannotBeMutatedImplicitly(string action)
        => Assert.Throws<TypeCheckException>(() => Check(Preamble + "void Test(){int item=0;Target target=new();Box box=new(&item,&target);" + action + "}"));

    [Fact]
    public void ShallowAndDeepReceiverCapabilitiesDiffer()
    {
        Check(Preamble + "void Test(){int item=0;Target target=new();Box box=new(&item,&target);readonly(Box)* view=&box;view.TouchTarget();view.Read();immutable(Box)* deep=&box;deep.Read();}");
        Assert.Throws<TypeCheckException>(() => Check(Preamble + "void Test(){int item=0;Target target=new();Box box=new(&item,&target);immutable(Box)* deep=&box;deep.TouchTarget();}"));
        Assert.Throws<TypeCheckException>(() => Check("struct Bad{int* data;public immutable void Change(){data[0]=1;}}"));
    }

    [Fact]
    public void InlineArraysProtectSlotsAndDeepViewsAlsoProtectPointees()
    {
        const string types="struct Target{public int value;public Target(){value=0;}}struct Box{public Target*[1] slots;public Box(Target* value){slots=[value];}}";
        const string setup="void Test(){Target target=new();Box box=new(&target);";
        Check(types+setup+"readonly(Box)* view=&box;view.slots[0].value=1;}");
        Assert.Throws<TypeCheckException>(()=>Check(types+setup+"readonly(Box)* view=&box;view.slots[0]=&target;}"));
        Assert.Throws<TypeCheckException>(()=>Check(types+setup+"immutable(Box)* view=&box;view.slots[0].value=1;}"));
    }

    [Fact]
    public void PointerStorageQualificationDoesNotQualifyThePointee()
    {
        Check("void Test(){int value=0;readonly(int*) slot=&value;*slot=42;}");
        Assert.Throws<TypeCheckException>(()=>Check("void Test(){int value=0;readonly(int*) slot=&value;slot=&value;}"));
        Assert.Throws<TypeCheckException>(()=>Check("void Test(){int value=0;readonly(int)* slot=&value;*slot=42;}"));
    }

    [Fact]
    public void MutableMethodsRemainUnavailableThroughShallowReceivers()
    {
        var error=Assert.Throws<TypeCheckException>(()=>Check(Preamble+"void Test(readonly(Target)* value){value.Change();}"));
        Assert.Contains("Cannot call non-readonly",error.Description);
        Assert.Contains("on readonly instance",error.Description);
    }

    [Fact]
    public void PrimitiveQualifiersHaveEquivalentCallbackSignatures()
        => Check("bool Compare(readonly(int)* value)=>*value==0;void Use(bool(immutable(int)*)* predicate){}void Test(){Use(&Compare);}");

    [Fact]
    public void ImmutableSourceRequiresDeepCopyContract()
    {
        const string shallow = "struct Owner{int* data;public copy Owner(readonly(Owner)* source){data=source.data;}public Owner(int* p){data=p;}}";
        const string deep = "struct Owner{int owned;int* data;public copy Owner(immutable(Owner)* source){owned=*source.data;data=&owned;}public Owner(int* p){owned=0;data=p;}}";
        const string call = "void Test(){int item=0;Owner original=new(&item);immutable(Owner)* view=&original;Owner copy=*view;}";
        Assert.Throws<TypeCheckException>(()=>Check(shallow+call));
        Assert.Throws<TypeCheckException>(()=>Check(shallow+call.Replace("Owner copy=*view", "immutable(Owner) copy=*view")));
        Check(deep+call);
        Check(shallow+"void Test(){int item=0;Owner original=new(&item);readonly(Owner)* view=&original;Owner copy=*view;}");
    }

    [Fact]
    public void GenericShallowParameterPreservesInferredDeepArgument()
    {
        const string types="struct Target{public int value;}struct Box{public Target* target;}";
        Check(types+"void Borrow<T>(readonly(T)* value){}void Test(immutable(Box)* value){Borrow(value);}");
        Assert.Throws<TypeCheckException>(()=>Check(types+"void Touch<T>(readonly(T)* value){value.target.value=42;}void Test(immutable(Box)* value){Touch(value);}"));
        Assert.Throws<TypeCheckException>(()=>Check(types+"void Borrow<T>(readonly(T)* value){}void Test(immutable(Box)* value){Borrow<Box>(value);}"));
        Check(types+"void Borrow<T>(immutable(T)* value){}void Test(Box* value){Borrow(value);}");
        Assert.Throws<TypeCheckException>(()=>Check(types+"T Copy<T>(readonly(T)* value)=>*value;void Test(immutable(Box)* value){Box escaped=Copy(value);}"));
    }

    [Fact]
    public void DeepRefGetterRequiresExplicitCapabilityAndRestrictsTheReturnedStorage()
    {
        const string prefix="struct Box{int* data;public Box(int* p){data=p;}public ref int this[int index]{";
        const string suffix="get{return data[index];}}}void Test(){int value=0;Box box=new(&value);immutable(Box)* view=&box;int copy=(*view)[0];}";
        Check(prefix+"immutable "+suffix);
        Assert.Throws<TypeCheckException>(()=>Check(prefix+suffix));
        Assert.Throws<TypeCheckException>(()=>Check((prefix+"immutable "+suffix).Replace("int copy=(*view)[0]", "(*view)[0]=1")));
        // A shallow-only getter may mutate referenced storage; synthesizing an
        // implicit deep variant would incorrectly reject this whole declaration.
        Check("struct Box{int* data;public Box(int* p){data=p;}public ref int this[int index]{get{data[index]=1;return data[index];}}}");
    }

    [Fact]
    public void OverridesAndInterfaceImplementationsCannotWeakenDeepReceivers()
    {
        Check("interface I{immutable int Read();}struct Item:I{public immutable int Read()=>1;}void Test(immutable(I)* value){value.Read();}");
        Assert.Throws<TypeCheckException>(()=>Check("interface I{immutable int Read();}struct Item:I{public readonly int Read()=>1;}"));
        Assert.Throws<TypeCheckException>(()=>Check("class Base{public virtual immutable int Read()=>1;}class Child:Base{public override readonly int Read()=>2;}"));
    }

    [Fact]
    public void ManagedViewsKeepTheSameShallowAndDeepBoundaries()
    {
        const string runtime="alias GC=Collector;static class Collector{public static void* Allocate(nuint bytes,nuint alignment)=>((nuint)1).reinterpret<void*>();public static void ReferenceAdded(void* slot)nothrow{}public static void ReferenceRemoved(void* slot)nothrow{}}";
        Check(runtime+Preamble+"void Test(readonly(Box)^ value){value.target.Change();value.data[0]=42;}");
        Assert.Throws<TypeCheckException>(()=>Check(runtime+Preamble+"void Test(immutable(Box)^ value){value.target.Change();}"));
        Assert.Throws<TypeCheckException>(()=>Check(runtime+Preamble+"void Test(immutable(Box)^ value){readonly(Box)^ escaped=(readonly(Box)^)value;}"));
        Assert.Throws<TypeCheckException>(()=>Check(runtime+Preamble+"void Test(immutable(Box)^ value){Box^ escaped=value;}"));
    }

    [Fact]
    public void ImmutableObjectsCanExposeOnlyReadonlyOpaqueVoidViews()
    {
        const string type="struct Box{public int* data;}";
        Check(type+"void Test(immutable(Box)* value){readonly(void)* opaque=value;}");
        Check(type+"alias Opaque=readonly(void)*;void Test(immutable(Box)* value){Opaque opaque=value;}");
        Assert.Throws<TypeCheckException>(()=>Check(type+"void Test(immutable(Box)* value){void* writable=value;}"));
        Assert.Throws<TypeCheckException>(()=>Check(type+"void Test(immutable(Box)* value){readonly(void*)* slot=value;}"));
        // This is the existing explicit raw-pointer escape, not an implicit downgrade.
        Check(type+"void Test(immutable(Box)* value){void* writable=(void*)value;}");
    }

    [Fact]
    public void EnumLeavesCanonicalizeAfterAliasAndGenericBinding()
    {
        const string declarations="namespace Model{enum Color{Red=1}}alias Shade=Model::Color;";
        Check(declarations+"bool Probe(readonly(Shade)* value)=>*value==Model::Color::Red;void Use<T>(bool(immutable(T)*)* predicate){}void Test(){Use<Shade>(&Probe);}");
        Check(declarations+"bool Probe(immutable(Shade)* value)=>*value==Model::Color::Red;void Use<T>(bool(readonly(T)*)* predicate){}void Test(){Use<Shade>(&Probe);}");
        Assert.Throws<TypeCheckException>(()=>Check(declarations+"void Test(immutable(Shade)* value){*value=Model::Color::Red;}"));
        Assert.Throws<TypeCheckException>(()=>Check(declarations+"enum Other{Red=1}bool Probe(readonly(Other)* value)=>true;void Use<T>(bool(immutable(T)*)* predicate){}void Test(){Use<Shade>(&Probe);}"));
        // A pointer-free aggregate deliberately keeps nominal deep identity.
        Assert.Throws<TypeCheckException>(()=>Check("struct Token{public int value;}void Probe(readonly(Token)* value){}void Use(void(immutable(Token)*)* action){}void Test(){Use(&Probe);}"));
    }

    [Theory]
    [InlineData("struct Box{}void Use(readonly(Box)* value){}void Use(immutable(Box)* value){}")]
    [InlineData("struct Box{public readonly int Read()=>1;public immutable int Read()=>2;}")]
    public void QualifiersDoNotCreateOverloadFamilies(string source)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Check(source));
        Assert.Contains("cannot differ only by readonly, immutable,",error.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShallowMutationAndDeepReadProduceExpectedNativeResult(int optimization)
    {
        var result=CompilerTestHelper.Run(Preamble+"int main(){int item=0;Target target=new();Box box=new(&item,&target);readonly(Box)* shallow=&box;shallow.data[0]=20;shallow.TouchTarget();immutable(Box)* deep=&box;return deep.Read()+deep.target.Read()-42;}", optimization);
        Assert.Equal(0,result.ExitCode);
    }

    private const string Preamble="""
        struct Target{public int value;public Target(){value=21;}public void Change(){value=22;}public immutable int Read()=>value;}
        struct Box{
            public int* data;public Target* target;public int inlineValue;
            public Box(int* p,Target* t){data=p;target=t;inlineValue=0;}
            public readonly void TouchTarget(){target.Change();}
            public immutable int Read()=>data[0];
        }
        """;
}

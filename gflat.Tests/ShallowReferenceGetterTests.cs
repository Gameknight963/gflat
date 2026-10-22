using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ShallowReferenceGetterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitShallowGettersBorrowMutablePointedStorageThroughReadonlyOwners(int optimization)
    {
        var result=CompilerTestHelper.Run("""
            interface ICell{ref int Value{readonly get;}}
            struct Cell:ICell{
                private int* data;
                public Cell(int* storage){data=storage;}
                public ref int Value{readonly get=>*data;}
                public ref int this[int index]{readonly get=>data[index];}
            }
            void Increment(ref int value){value++;}
            int main(){
                int value=0;Cell cell=new(&value);readonly(Cell)* view=&cell;
                view.Value=20;Increment(ref view.Value);(*view)[0]+=20;
                readonly(ICell)* interfaceView=&cell;Increment(ref interfaceView.Value);
                return value-42;
            }
            """,optimization);
        Assert.Equal(0,result.ExitCode);
    }

    [Theory]
    [InlineData("value")]
    [InlineData("this.value")]
    [InlineData("items[0]")]
    public void ShallowGettersCannotReturnMutableReferencesToReadonlyInlineStorage(string expression)
    {
        string source="struct Cell{int value;int[1] items;public Cell(){value=0;items=[0];}public ref int Value{readonly get=>"+expression+";}}";
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source,options:new(){NoStdLib=true}));
        Assert.Contains("Cannot return",error.Description);
    }

    [Fact]
    public void ExplicitShallowGetterCannotReassignTheOwnersPointerField()
    {
        const string source="struct Cell{int* data;public Cell(int* p){data=p;}public ref int Value{readonly get{data=(int*)1;return *data;}}}";
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source,options:new(){NoStdLib=true}));
        Assert.Contains("Cannot assign",error.Description);
    }

    [Fact]
    public void DeclaredReadonlyReferenceStillRejectsAssignmentThroughShallowGetter()
    {
        const string source="struct Cell{int value;public Cell(){value=0;}public ref readonly(int) Value{readonly get=>value;}}void Write(readonly(Cell)* value){value.Value=42;}";
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source,options:new(){NoStdLib=true}));
        Assert.Contains("Cannot assign to readonly storage",error.Description);
    }

    [Fact]
    public void DeclaredReadonlyReferenceCanBorrowReadonlyInlineStorage()
        => Compiler.Check("struct Cell{int value;public Cell(){value=42;}public ref readonly(int) Value{readonly get=>value;}}void Read(readonly(Cell)* value){int copy=value.Value;}",options:new(){NoStdLib=true});

    [Fact]
    public void ImmutableOwnerCannotInvokeAnExplicitShallowGetter()
    {
        const string source="struct Cell{int* data;public Cell(int* p){data=p;}public ref int Value{readonly get=>*data;}}void Read(immutable(Cell)* value){int copy=value.Value;}";
        Assert.Throws<TypeCheckException>(()=>Compiler.Check(source,options:new(){NoStdLib=true}));
    }
}

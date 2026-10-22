using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class OrdinaryOverloadSelectionTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void OrdinaryTemplateOverloadsUseTheirConcreteNestedOwner(string kind)
        => Assert.Equal(23, CompilerTestHelper.Run($$"""
            namespace Library {
                class Owner {
                    public struct Word {}
                    public {{kind}} Box<T> {
                        public int Read(Word value)=>23;
                        public int Read(long value)=>24;
                    }
                }
            }
            alias Word=long;
            int main(){
                Library::Owner::Box<int> item=new Library::Owner::Box<int>();
                return item.Read(new Library::Owner::Word());
            }
            """).ExitCode);

    private const string DuplicateTemplate = "class Box<T>{public int Read(int value)=>1;public int Read(int value)=>2;}";

    [Fact]
    public void OrdinaryTemplateDuplicateChecksAreDeferredUntilInstantiation()
    {
        // Generic template bodies and overload sets are checked in their
        // concrete owner context when instantiated, rather than by replacement selection.
        Compiler.Check(DuplicateTemplate + "int main()=>0;");
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(DuplicateTemplate +
            "int main(){Box<int> value=new Box<int>();return 0;}"));
        Assert.Contains("cannot differ only", error.Description);
    }

    [Theory]
    [InlineData("int Read(int value)=>1;int Read(int value)=>2;", "cannot differ only")]
    [InlineData("class Box{public int Read()=>1;public immutable int Read()=>2;}", "cannot differ only")]
    [InlineData("extern int Read(int value);int Read(int value)=>value;", "Duplicate declaration")]
    [InlineData("class Box{public static virtual int Read(int value)=>1;public static int Read(long value)=>2;}", "Static methods cannot")]
    public void OrdinaryGroupsRetainTheirExistingValidation(string declarations, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(declarations + "int main()=>0;"));
        Assert.Contains(message, error.Description);
    }

    [Theory]
    [InlineData("weak T Read<T>(T value)=>Missing();replace U Read<U>(U value)=>value;", "Read<int>(42)")]
    [InlineData("expect T Read<T>(T value);replace U Read<U>(U value)=>value;", "Read<int>(42)")]
    [InlineData("class Box<T>{public static weak T Read(T value)=>Missing();public static replace T Read(T value)=>value;}", "Box<int>::Read(42)")]
    public void GenericReplacementGroupsStillSelectBeforeSpecialization(string declarations, string call)
        => Assert.Equal(42, CompilerTestHelper.Run(declarations + "int main()=>" + call + ";").ExitCode);
}

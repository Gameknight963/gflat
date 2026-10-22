using System.Text.RegularExpressions;
using gflat.CompileExceptions;

namespace gflat.Tests;

public class NothrowConformanceTests
{
    private const string StrongImplementations="""
        interface I { int Read(); }
        struct Value : I { public int Read() nothrow=>1; }
        class Base { public virtual int Read()=>2; }
        class Derived : Base,I { public override int Read() nothrow=>3; }
        interface ICode {
            immutable int Read();
            implementation int { public immutable int Read() nothrow=>*this; }
        }
        static interface IProvider { int Read(); }
        static class Provider : IProvider { public static int Read() nothrow=>4; }
        int FromProvider<T:IProvider>()=>T::Read();
        int Direct(Value* value)=>value.Read();
        int main() {
            Value value=new(); Derived derived=new(); int number=5;
            I* first=&value; I* second=&derived; Base* parent=&derived; ICode* primitive=&number;
            return first.Read()+second.Read()+parent.Read()+primitive.Read()+Direct(&value)+FromProvider<Provider>()-17;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StrongerGuaranteesWorkThroughEveryDispatchPath(int optimization)
        => Assert.Equal(0,CompilerTestHelper.Run(StrongImplementations,optimization).ExitCode);

    [Fact]
    public void CallersUseTheirVisibleContract()
    {
        string ir=Compiler.Emit(StrongImplementations);
        string direct=Regex.Match(ir,@"(?ms)^define [^\n]*@gflat\$Direct\([^\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(direct);
        Assert.DoesNotContain("invoke",direct);
        string main=Regex.Match(ir,@"(?ms)^define [^\n]*@main\([^\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.Contains("invoke i32 %",main);
    }

    [Theory]
    [InlineData("interface I{int Read() nothrow;} struct C:I{public int Read()=>1;}")]
    [InlineData("interface I{int Read() nothrow;} class C:I{public int Read()=>1;}")]
    [InlineData("interface I{immutable int Read() nothrow; implementation int{public immutable int Read()=>*this;}}")]
    [InlineData("class B{public virtual int Read() nothrow=>1;} class C:B{public override int Read()=>2;}")]
    [InlineData("static interface I{int Read() nothrow;} static class C:I{public static int Read()=>1;}")]
    public void ImplementationsStillCannotWeakenAGuarantee(string source)
        => Assert.Contains("nothrow",Assert.Throws<TypeCheckException>(()=>Compiler.Check(source)).Message);

    [Fact]
    public void PropertyAccessorsCanAlsoStrengthenTheirContract()
    {
        Compiler.Check("""
            interface I { int Count { get; } }
            struct S:I { public int Count { get nothrow=>42; } }
            class B { public virtual int Count { get=>1; } }
            class C:B { public override int Count { get nothrow=>42; } }
            """);
    }
}

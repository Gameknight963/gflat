using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class UnqualifiedStaticMemberTests
{
    [Theory]
    [InlineData("class", 0)]
    [InlineData("class", 2)]
    [InlineData("struct", 0)]
    [InlineData("struct", 2)]
    public void ReadonlyInstanceCanCallUnqualifiedStaticHelper(string kind, int optimization)
    {
        var result = CompilerTestHelper.Run($$"""
            {{kind}} Sample {
                private static int Helper(int value) => value + 1;
                public readonly int Read() => Helper(41);
            }
            int main(){Sample value=new();readonly(Sample)* read=&value;return read.Read()-42;}
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("class", 0)]
    [InlineData("class", 2)]
    [InlineData("struct", 0)]
    [InlineData("struct", 2)]
    public void StaticFactoryCanReadUnqualifiedPrivateConstant(string kind, int optimization)
    {
        var result = CompilerTestHelper.Run($$"""
            {{kind}} Sample {
                private const int Limit=42;
                public static int Read() => Limit;
            }
            int main()=>Sample::Read()-42;
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("class", 0)]
    [InlineData("class", 2)]
    [InlineData("struct", 0)]
    [InlineData("struct", 2)]
    public void ReadonlyInstanceCanSelectStaticOverloadsAndReadPrivateConstant(string kind, int optimization)
    {
        var result = CompilerTestHelper.Run($$"""
            {{kind}} Sample {
                private const int Bias=0;
                private static int Helper(int value)=>value+1;
                private static int Helper(long value)=>(int)value;
                public readonly int Read()=>Helper(40)+Helper(1l)+Bias;
            }
            int main(){Sample value=new();readonly(Sample)* read=&value;return read.Read()-42;}
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void InstanceMutationAndPrivateConstantAccessRemainRestricted(string kind)
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            {{kind}} Sample{private int Helper()=>42;public readonly int Read()=>Helper();}
            """));
        Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            {{kind}} Sample{private const int Limit=42;}int main()=>Sample::Limit;
            """));
        Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            {{kind}} Sample{private int Helper()=>42;public static int Read()=>Helper();}
            """));
        Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            {{kind}} Sample{private int value;public static int Read()=>value;}
            """));
    }
}

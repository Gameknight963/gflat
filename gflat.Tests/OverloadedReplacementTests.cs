using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class OverloadedReplacementTests
{
    [Fact]
    public void ReplacementSelectsOnlyTheMatchingOverload()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            weak int Read() => 0;
            weak int Read(int value) => value;
            replace int Read() => 40;
            int Read(int first, int second) => first + second;
            int main() => Read() + Read(1) + Read(0, 1);
            """).ExitCode);

    [Fact]
    public void EveryExpectedOverloadRequiresItsOwnImplementation()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            expect int Read();
            expect int Read(int value);
            replace int Read() => 42;
            int main() => Read();
            """));
        Assert.Contains("has no implementation", error.Message);
    }

    [Fact]
    public void AllExpectedOverloadsCanBeImplemented()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            expect int Read();
            expect int Read(int value);
            replace int Read() => 40;
            replace int Read(int value) => value;
            int main() => Read() + Read(2);
            """).ExitCode);

    [Fact]
    public void AllocatorReplacementPairIsCheckedWhenOtherOverloadsExist()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            namespace Memory {
                public replace void*? Allocate(nuint bytes) => null;
                public void*? Allocate(nuint bytes, int tag) => null;
            }
            int main() => 0;
            """));
        Assert.Contains("together", error.Message);
    }
}

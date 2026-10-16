using Xunit;

namespace gflat.Tests;

public class StaticInitializationSafetyTests
{
    [Fact]
    public void IndirectReadCannotExposeUninitializedNonnullableGlobal()
    {
        var result = CompilerTestHelper.Run("""
            int storage = 42;
            int* first = ReadSecond();
            int* second = Address();
            int* ReadSecond() => second;
            int* Address() => &storage;
            int main() => 0;
            """);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void InitializedNonnullableGlobalCanBeReadDuringStartup()
    {
        var result = CompilerTestHelper.Run("""
            int storage = 42;
            int* first = Address();
            int* second = ReadFirst();
            int* ReadFirst() => first;
            int* Address() => &storage;
            int main() => *second - 42;
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void DefaultInitializedGlobalsRetainZeroInitializationSemantics()
    {
        var result = CompilerTestHelper.Run("""
            int first = ReadSecond();
            int second = Next();
            int ReadSecond() => second;
            int Next() => 42;
            int main() => first + second - 42;
            """);
        Assert.Equal(0, result.ExitCode);
    }
}

using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StaticLocalTests
{
    [Fact]
    public void ConstantAndZeroInitializedStaticsNeedNoGuardOrHostedRuntime()
    {
        string ir = Compiler.Emit("int Next(){static int value=4;return ++value;} int Zero(){static int value;return value;} int main()=>Next()-5+Zero();",
            options: new() { NoStdLib = true });
        Assert.Contains("internal global i32 4", ir);
        Assert.DoesNotContain("thread_local", ir);
        Assert.DoesNotContain("@atexit", ir);
        Assert.DoesNotContain("@llvm.global_ctors", ir);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FirstUseUsesTheFirstArgumentAndEachGenericAndLexicalDeclarationHasItsOwnStorage(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            int calls = 0;
            int Initialize(int value) { calls++; return value; }
            int Next(int start) { static int value = Initialize(start); return ++value; }
            int Generic<T>() { static int value = 0; return ++value; }
            int main()
            {
                if (calls != 0 || Next(4) != 5 || Next(90) != 6 || calls != 1) return 1;
                if (Generic<int>() != 1 || Generic<long>() != 1 || Generic<int>() != 2) return 2;
                { static int value = 10; value++; if (value != 11) return 3; }
                { static int value = 20; if (value != 20) return 4; }
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SuccessfulInitializationsAreDestroyedInReverseCompletionOrderAndUnusedLocalsAreNotDestroyed(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            extern "C" int putchar(int value);
            struct Owner { int id; public Owner(int value) { id = value; } ~Owner() { putchar(id); } }
            void First() { static Owner value = new(65); }
            void Second() { static Owner value = new(66); }
            void Unused() { static Owner value = new(68); }
            int main() { Second(); First(); Second(); putchar(67); return 0; }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("CAB", result.StandardOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingInitializationCanBeRetried(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            int calls = 0;
            int Initialize() { calls++; if (calls == 1) throw new Exception("retry", 7); return 42; }
            int Read() { static int value = Initialize(); return value; }
            int main()
            {
                try { Read(); return 1; } catch (ref Exception error) { if (error.GetCode() != 7) return 2; }
                return Read() == 42 && Read() == 42 && calls == 2 ? 0 : 3;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("int F(){static int* value;return 0;}", "initializer")]
    [InlineData("const int F(){static int value=0;return value;}", "const function")]
    [InlineData("struct Owner { public Owner(){} ~Owner(){} } void Take(Owner x){} void F(){static Owner x=new();Take(move x);}", "static local")]
    [InlineData("int F(){int value;static int x=(value=1);return value;}", "unassigned")]
    public void InvalidStaticLocalsAreRejected(string source, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Contains(message, error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConcurrentFirstUseInitializesOnceAndPublishesTheCompletedValue(int optimization)
    {
        const string source = """
            #lang cpp
            #include <atomic>
            #include <thread>
            #include <chrono>
            namespace { std::atomic<int> calls{0}; }
            int Initialize() noexcept {
                ++calls;
                std::this_thread::sleep_for(std::chrono::milliseconds(20));
                return 42;
            }
            int RunWorkers(int (*read)()) {
                int results[8] = {};
                std::thread workers[8];
                for (int i = 0; i < 8; ++i) workers[i] = std::thread([&, i] { results[i] = read(); });
                for (auto& worker : workers) worker.join();
                for (int result : results) if (result != 42) return 1;
                return calls == 1 ? 0 : 2;
            }
            #lang gflat
            int Read() { static int value = Initialize(); return value; }
            int main() => RunWorkers(Read);
            """;
        var program = Compiler.EmitProgram([new SourceFile("static-threads.gf", source)], options: new()
        {
            NoStdLib = true, InternalizeFunctions = true,
            ForeignCompilerResolver = () => NativeToolchain.FindClang(),
            ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
        });
        var result = CompilerTestHelper.RunIr(program.Ir, optimization, expectedExitCode: 0, foreignUnits: program.ForeignUnits);
        Assert.Equal(0, result.ExitCode);
    }
}

using System.Text;

namespace gflat.Tests;

public sealed class FunctionLinkageTests
{
    [Fact]
    public void ExecutableDefinitionsAreInternalExceptForMain()
    {
        string ir = Compiler.Emit("""
            int Helper() => 40;
            class Box {
                public Box() {}
                public int Read() => 1;
                ~Box() {}
            }
            struct Item {
                public Item() {}
                public int Read() => 1;
                ~Item() {}
            }
            int main() {
                int()* callback = () => Helper();
                Box box = new Box();
                Item item = new Item();
                return callback() + box.Read() + item.Read();
            }
            """, options: new() { InternalizeFunctions = true });
        string[] definitions = ir.Split('\n').Where(l => l.StartsWith("define ")).ToArray();
        Assert.Contains(definitions, d => d.StartsWith("define i32 @main("));
        Assert.All(definitions.Where(d => !d.StartsWith("define i32 @main(")),
            d => Assert.StartsWith("define internal ", d));
    }

    [Fact]
    public void EmittedModulesPreserveDefinitionsForNativeCallers()
    {
        string ir = Compiler.Emit("int Answer() => 42;", options: new() { NoStdLib = true });
        Assert.Contains("define i32 @gflat$Answer(", ir);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public void UnusedFunctionAndItsDataAreDiscarded(int optimization, bool internalize)
    {
        string ir = Compiler.Emit("""
            extern int puts(immutable(char*) text);
            int Unused() => puts("gflat_unused_function_data_984276");
            int main() => 42;
            """, options: new() { NoStdLib = true, InternalizeFunctions = internalize });
        BuildAndRun(ir, optimization, (result, executable) =>
        {
            Assert.Equal(42, result.ExitCode);
            Assert.Equal(-1, File.ReadAllBytes(executable).AsSpan().IndexOf(
                Encoding.UTF8.GetBytes("gflat_unused_function_data_984276")));
        });
    }

    [Fact]
    public void OptimizedExecutableDoesNotRequireAnUnreachableNativeDependency()
    {
        string ir = Compiler.Emit("""
            extern int gflat_missing_unused_dependency();
            int Unused() => gflat_missing_unused_dependency();
            int main() => 42;
            """, options: new() { NoStdLib = true, InternalizeFunctions = true });
        BuildAndRun(ir, 2, (result, _) => Assert.Equal(42, result.ExitCode));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeCodeCanRetainAndCallAnInternalCallback(int optimization)
    {
        string ir = Compiler.Emit("""
            extern int atexit(void()* callback);
            extern int putchar(int value);
            void Callback() { putchar(65); }
            int main() { atexit(Callback); return 42; }
            """, options: new() { InternalizeFunctions = true });
        Assert.Contains("define internal void @gflat$Callback(", ir);
        BuildAndRun(ir, optimization, (result, _) =>
        {
            Assert.Equal(42, result.ExitCode);
            Assert.Equal("A", result.StandardOutput);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InternalLinkagePreservesStaticLifetimeAndExceptionDestruction(int optimization)
    {
        string ir = Compiler.Emit("""
            extern int putchar(int value);
            struct Owner { public Owner() {} ~Owner() { putchar(65); } }
            Owner appOwner = new Owner();
            class Failure : Exception { ~Failure() { putchar(69); } }
            void Fail() { Owner local = new Owner(); throw new Failure(); }
            int main() {
                try { Fail(); }
                catch (ref Exception error) { putchar(67); }
                return 42;
            }
            """, options: new() { InternalizeFunctions = true });
        BuildAndRun(ir, optimization, (result, _) =>
        {
            Assert.Equal(42, result.ExitCode);
            Assert.Equal("ACEA", result.StandardOutput);
        });
    }

    private static void BuildAndRun(string ir, int optimization, Action<ProcessResult, string> verify)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-linkage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(input, ir);
            var compilation = NativeToolchain.Compile(input, executable, optimizationLevel: optimization);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            verify(NativeToolchain.Run(executable, []), executable);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

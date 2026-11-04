using System.Text.RegularExpressions;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NativeTrimmingTests
{
    private static string Emit(string code, bool native = true, bool debug = false)
        => Compiler.Emit(code, options: new() {
            NoStdLib = true, NoBuiltins = true, InternalizeFunctions = native, DebugSymbols = debug,
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeBuildDropsUnusedBodiesAndTablesWhileStandaloneModulesKeepDefinitions(bool debug)
    {
        const string source = """
            int Unused() => 99;
            class UnusedClass { public virtual int Read() => 7; }
            int main() => 42;
            """;
        string native = Emit(source, debug: debug);
        Assert.Single(Regex.Matches(native, @"(?m)^define "));
        Assert.DoesNotContain("@gflat$Unused(", native);
        Assert.DoesNotContain("@UnusedClass$vtable", native);
        Assert.DoesNotContain("@__gflat_eh_", native);
        Assert.Contains("@gflat$Unused(", Emit(source, native: false, debug: debug));
        Assert.Equal(42, CompilerTestHelper.RunIr(native).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallbackAddressesAndLambdaBodiesReachTheirCallees(int optimization)
    {
        string ir = Emit("""
            int Increment(int value) nothrow => value + 1;
            int Unused() => 99;
            int main() {
                int(int)* callback = &Increment;
                int(int)* lambda = (int value) => Increment(value);
                return callback(20) + lambda(20);
            }
            """, debug: true);
        Assert.Contains("@gflat$Increment(", ir);
        Assert.DoesNotContain("@gflat$Unused(", ir);
        Assert.Equal(42, CompilerTestHelper.RunIr(ir, optimization).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void VirtualAndInterfaceTablesRetainOverridesAndDropUnusedClasses(int optimization)
    {
        string ir = Emit("""
            interface IRead { int Read(); }
            class Base : IRead { public virtual int Read() => 3; }
            class Derived : Base { public override int Read() => 42; }
            class UnusedClass { public virtual int Read() => 99; }
            int main() {
                Derived value = new Derived();
                Base* parent = &value;
                IRead* view = &value;
                return parent.Read() + view.Read();
            }
            """, debug: true);
        Assert.DoesNotContain("@UnusedClass$vtable", ir);
        Assert.Contains("@Derived$vtable", ir);
        Assert.Equal(84, CompilerTestHelper.RunIr(ir, optimization).ExitCode);
    }

    [Fact]
    public void GlobalFunctionPointerInitializationRetainsItsTarget()
    {
        string ir = Emit("""
            int Increment(int value) nothrow => value + 1;
            int(int)* saved = &Increment;
            int main() => saved(41);
            """);
        Assert.Contains("@gflat$Increment(", ir);
        Assert.Equal(42, CompilerTestHelper.RunIr(ir).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnreferencedGlobalStillRunsItsInitializerAndDestructor(int optimization)
    {
        string ir = Emit("""
            extern int putchar(int value) nothrow;
            struct Owner {
                public Owner() nothrow { putchar(65); }
                ~Owner() { putchar(67); }
            }
            Owner unused = new Owner();
            int main() { putchar(66); return 0; }
            """, debug: true);
        Assert.Contains("@llvm.global_ctors", ir);
        Assert.Contains("@gflat$$static_fini", ir);
        var result = CompilerTestHelper.RunIr(ir, optimization);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ABC", result.StandardOutput);
    }

    [Fact]
    public void StringContentsDoNotRetainSameNamedFunctions()
    {
        string ir = Emit("""
            int Unused() => 99;
            int main() => __llvm_module<int>(
                "@text = private constant [14 x i8] c\"@gflat$Unused\\00\"\ndeclare i32 @puts(i8*)\ndefine i32 @entry() nounwind {\n%data = getelementptr [14 x i8], [14 x i8]* @text, i32 0, i32 0\n%written = call i32 @puts(i8* %data)\nret i32 0\n}", "entry");
            """);
        Assert.DoesNotMatch(@"(?m)^define[^\n]*@gflat\$Unused\(", ir);
        var result = CompilerTestHelper.RunIr(ir);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("@gflat$Unused", result.StandardOutput);
    }

    [Fact]
    public void ReflectionSplicesAndGenericSpecializationsRunBeforeReachability()
    {
        string ir = Emit("""
            const reflection Choose(bool wide) {
                if (wide) return reflect(long);
                return reflect(int);
            }
            alias Number = [:Choose(false):];
            T Identity<T>(T value) => value;
            int Unused() => 99;
            int main() { Number value = 42; return Identity(value); }
            """);
        Assert.DoesNotContain("@gflat$Unused(", ir);
        Assert.DoesNotContain("@gflat$Choose(", ir);
        Assert.Equal(42, CompilerTestHelper.RunIr(ir).ExitCode);
    }

    [Fact]
    public void InlineLlvmReferencesRetainLanguageFunctions()
    {
        string ir = Emit("""
            int Hidden() nothrow => 42;
            int Unused() => 99;
            int main() => __llvm_module<int>(
                "define i32 @entry() nounwind {\n%value = call i32 @gflat$Hidden()\nret i32 %value\n}", "entry");
            """);
        Assert.Contains("@gflat$Hidden(", ir);
        Assert.DoesNotContain("@gflat$Unused(", ir);
        Assert.Equal(42, CompilerTestHelper.RunIr(ir).ExitCode);
    }

    [Fact]
    public void SymbolNamesContainingDefineDoNotConfuseModuleRecords()
    {
        string ir = Emit("int define = 2; int main() => define + 40;");
        Assert.Equal(42, CompilerTestHelper.RunIr(ir).ExitCode);
    }

    [Fact]
    public void InvalidUnusedBodiesStillProduceDiagnostics()
        => Assert.Throws<TypeCheckException>(() => Emit("int Unused() => missing; int main() => 0;"));

    [Fact]
    public void InvalidUnusedLlvmModulesStillProduceCompilerDiagnostics()
    {
        var error = Assert.Throws<TypeCheckException>(() => Emit("""
            int Unused() => __llvm_module<int>("define i64 @entry() { ret i64 0 }", "entry");
            int main() => 0;
            """));
        Assert.Contains("LLVM entry result type", error.Message);
    }
}

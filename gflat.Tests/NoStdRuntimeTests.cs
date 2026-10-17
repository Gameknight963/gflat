using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NoStdRuntimeTests
{
    private static readonly CompilationOptions Bare = new() { NoStdLib = true };

    private static ExecutionResult Run(string ir)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-bare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(source, ir);
            var compilation = NativeToolchain.Compile(source, executable);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            var result = NativeToolchain.Run(executable, []);
            return new(result.ExitCode, result.StandardOutput, result.StandardError);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("float", "float")]
    [InlineData("double", "double")]
    public void FloatingRemainderLinksWithoutAnImplicitCppExceptionRuntime(string type, string llvmType)
    {
        string ir = Compiler.Emit($$"""
            {{type}} Rem({{type}} left, {{type}} right) nothrow => left % right;
            int main(int argc, char** argv) {
                {{type}} value = ({{type}})argc + ({{type}})4.5;
                if (value % ({{type}})2.0 != ({{type}})1.5) return 1;
                if (-value % ({{type}})2.0 != ({{type}})-1.5) return 2;
                return 42;
            }
            """, options: Bare);
        Assert.Contains("frem " + llvmType, ir);
        Assert.DoesNotContain("__gflat_eh_", ir);
        Assert.DoesNotContain("personality", ir);
        Assert.Equal(42, Run(ir).ExitCode);
    }

    [Fact]
    public void ScalarProgramHasNoImplicitStdOrExceptionRuntimeReferences()
    {
        string ir = Compiler.Emit("int main() => 42;", options: Bare);
        Assert.DoesNotContain("%Exception", ir);
        Assert.DoesNotContain("gflat$Memory$", ir);
        Assert.DoesNotContain("__gflat_eh_", ir);
        Assert.DoesNotContain("personality", ir);
        Assert.Equal(42, Run(ir).ExitCode);
    }

    [Theory]
    [InlineData("readonly(char*) Text() => \"@__gflat_eh_capture\";")]
    [InlineData("int Value() => __llvm<int>(\"; @__gflat_eh_capture\\nret i32 42\");")]
    [InlineData("int Value() => __llvm_module<int>(\"; @__gflat_eh_capture\\ndefine i32 @entry() { ret i32 42 }\", \"entry\");")]
    public void RuntimeSymbolTextInDataAndCommentsDoesNotAddRuntimeDeclarations(string source)
    {
        string ir = Compiler.Emit(source, options: Bare);
        Assert.DoesNotContain("declare i8* @__gflat_eh_capture", ir);
        Assert.DoesNotContain("declare i32 @__CxxFrameHandler3", ir);
        Assert.DoesNotContain("declare i32 @__gxx_personality_v0", ir);
    }

    [Theory]
    [InlineData("call void @__gflat_eh_release(i8* null)\\nret void")]
    [InlineData("call void @\\\"__gflat_eh_release\\\"(i8* null)\\nret void")]
    public void InlineLlvmCanReferenceTheExceptionRuntime(string body)
    {
        string ir = Compiler.Emit("void Release() => __llvm<void>(\"" + body + "\");", options: Bare);
        Assert.Contains("declare void @__gflat_eh_release(i8*) nounwind", ir);
    }

    [Fact]
    public void InlineLlvmModuleCanDeclareItsExceptionRuntimeDependency()
    {
        string ir = Compiler.Emit("""
            void Release() => __llvm_module<void>("declare void @__gflat_eh_release(i8*) nounwind\ndefine void @entry() {\ncall void @__gflat_eh_release(i8* null)\nret void\n}", "entry");
            int main() { Release(); return 42; }
            """, options: Bare);
        Assert.Equal(1, ir.Split("declare void @__gflat_eh_release(i8*)").Length - 1);
        Assert.Equal(42, Run(ir).ExitCode);
    }

    [Fact]
    public void ValueConstructionAndDestructionNeedNoAllocatorOrExceptionRoot()
    {
        string ir = Compiler.Emit("""
            extern int putchar(int value);
            struct Item { public int value; public Item(int input) { value = input; } ~Item() { putchar(value); } }
            int main() { Item item = new Item(65); return 42; }
            """, options: Bare);
        Assert.DoesNotContain("%Exception", ir);
        Assert.DoesNotContain("gflat$Memory$", ir);
        Assert.DoesNotContain("gflat$Exceptions$Unhandled", ir);
        var result = Run(ir);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("A", result.StandardOutput);
    }

    [Fact]
    public void AnExceptionTypeDefinitionDoesNotRequireItsAllocationRuntime()
    {
        string ir = Compiler.Emit("class Exception {} int main() { Exception value = new Exception(); return 42; }", options: Bare);
        Assert.DoesNotContain("gflat_destroy_exception", ir);
        Assert.DoesNotContain("gflat$Memory$", ir);
        Assert.Equal(42, Run(ir).ExitCode);
    }

    [Fact]
    public void PlacementConstructionRequiresNoStd()
        => Assert.Equal(42, Run(Compiler.Emit("""
            struct Item { public int value; public Item(int input) { value = input; } }
            int main() {
                Item value = new Item(1);
                Item* result = new* Item(42) at &value;
                return result.value;
            }
            """, options: Bare)).ExitCode);

    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void PlacementConstructorWithoutManagedFieldsNeedsNoExceptionRuntime(string kind)
    {
        string ir = Compiler.Emit(kind + """
             Item { public int value; public Item(int input) { value = input; } }
            int Construct(Item* storage) {
                Item* result = new* Item(42) at storage;
                return result.value;
            }
            """, options: Bare);
        Assert.DoesNotContain("__gflat_eh_", ir);
        Assert.DoesNotContain("personality", ir);
        Assert.DoesNotContain("gflat$Memory$", ir);
    }

    [Theory]
    [InlineData("struct Item {} void Run() { Item* value = new* Item(); }", "requires Memory::Allocate")]
    [InlineData("struct Item {} void Run(Item* value) { delete value; }", "requires Memory::Free")]
    [InlineData("namespace Memory { int Allocate(int count) => count; void Free(void* storage) {} } struct Item {} void Run() { Item* value = new* Item(); }", "Allocate(nuint")]
    [InlineData("namespace Memory { int Free(void* storage) => 0; } struct Item {} void Run(Item* value) { delete value; }", "void Free")]
    public void MissingOrMismatchedRuntimeServicesAreSourceErrors(string source, string diagnostic)
        => Assert.Contains(diagnostic, Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: Bare)).Message);

    [Fact]
    public void PartialMemoryNamespaceDoesNotForceAnAllocatorPair()
        => Assert.Equal(42, Run(Compiler.Emit("namespace Memory { int Answer() => 42; } int main() => Memory::Answer();", options: Bare)).ExitCode);

    [Fact]
    public void AllocationAndExceptionCleanupSelectTheMatchingAllocatorOverloads()
    {
        var result = CompilerTestHelper.Run("""
            namespace Memory {
                public void*? Allocate(nuint bytes, int tag) => null;
                public void Free(void* storage, int tag) {}
            }
            class Item { public int value; public Item(int input) { value = input; } }
            int main() {
                Item* item = new* Item(42);
                int value = item.value;
                delete item;
                try { throw new Exception(); } catch (ref Exception error) { return value; }
                return 1;
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }
}

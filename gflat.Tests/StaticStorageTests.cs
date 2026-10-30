using gflat.CompileExceptions;
using Xunit;

namespace gflat.Tests;

public class StaticStorageTests
{
    [Fact]
    public void ConstantAndDefaultInitializedTrivialStorageNeedsNoRuntimeStartup()
    {
        string ir = Compiler.Emit("int first; int second = 42; int main() { return second - 42; }",
            options: new() { NoStdLib = true });
        Assert.Contains("internal global i32 42", ir);
        Assert.DoesNotContain("@llvm.global_ctors", ir);
        Assert.DoesNotContain("@atexit", ir);
    }

    [Fact]
    public void GlobalsAndNamespaceFieldsHavePersistentAddressableStorage()
    {
        var result = CompilerTestHelper.Run("""
            int count;
            namespace Counters { public int value = 3; }
            void Increment() { count++; Counters::value += 2; }
            int main() {
                Increment(); Increment();
                int* address = &Counters::value;
                *address += count;
                return Counters::value - 9;
            }
            """);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void StaticFieldsDoNotBecomeInstanceFields()
    {
        var result = CompilerTestHelper.Run("""
            struct Counter {
                public static int total = 10;
                public int value;
                public Counter(int n) { value = n; total += n; }
                public static int Read() => total;
            }
            int main() {
                Counter a = new Counter(2);
                Counter b = new Counter(3);
                if (sizeof(Counter) != sizeof(int)) return 1;
                return Counter::Read() - 15;
            }
            """);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void DynamicInitializersRunBeforeMainInDeclarationOrder()
    {
        var result = CompilerTestHelper.Run("""
            int sequence;
            int Next() { sequence++; return sequence; }
            int first = Next();
            namespace Test { public int second = Next(); }
            static class State { public static int third = Next(); }
            int main() {
                if (first != 1 || Test::second != 2 || State::third != 3) return 1;
                return sequence - 3;
            }
            """);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GlobalOwningValuesAreDestroyedInReverseOrder()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int c);
            struct Owner {
                int id;
                public Owner(int value) { id = value; }
                ~Owner() { putchar(id); }
            }
            Owner first = new Owner(65);
            Owner second = new Owner(66);
            int main() { putchar(67); return 0; }
            """);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        Assert.Equal("CBA", result.StandardOutput);
    }

    [Fact]
    public void StaticConstructorRunsOnceAfterAllOfItsFieldInitializers()
    {
        var result = CompilerTestHelper.Run("""
            int calls;
            static class State {
                public static readonly(int) answer;
                static State() { calls++; answer = value + 2; }
                private static int value = 40;
                public static int Read() => answer;
            }
            int main() {
                if (State::Read() != 42 || State::Read() != 42) return 1;
                return calls - 1;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void GenericStaticFieldsAreSeparateForEachClosedType()
    {
        var result = CompilerTestHelper.Run("""
            struct Counter<T> {
                public static int count = 10;
                public static int Next() { count++; return count; }
            }
            int main() {
                if (Counter<int>::Next() != 11) return 1;
                if (Counter<char>::Next() != 11) return 2;
                return Counter<int>::Next() - 12;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ExplicitExitAlsoDestroysStaticValues()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int c);
            extern void exit(int code);
            struct Owner { public Owner() {} ~Owner() { putchar(65); } }
            Owner owner = new Owner();
            int main() { exit(0); return 1; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("A", result.StandardOutput);
    }

    [Fact]
    public void FailingInitializerDestroysOnlyCompletedGlobals()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int c);
            struct Owner {
                int id;
                public Owner(int n) { id = n; if (n == 66) throw new Exception("failed", 9); }
                ~Owner() { putchar(id); }
            }
            Owner first = new Owner(65);
            Owner second = new Owner(66);
            int main() { putchar(67); return 0; }
            """);
        Assert.Equal(9, result.ExitCode);
        Assert.Equal("A", result.StandardOutput);
    }

    [Fact]
    public void CrossFileInitializationUsesCanonicalSourceOrder()
    {
        var sources = new[] {
            new SourceFile("z.gf", "public int last = Next(); int main() { return last - 3; }"),
            new SourceFile("a.gf", "int sequence; public int Next() { sequence++; return sequence; } public int first = Next();"),
            new SourceFile("m.gf", "namespace State { public int middle = Next(); }")
        };
        var result = CompilerTestHelper.Run(sources);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ManagedGlobalsSurviveCollectionBeforeAndAfterMainStarts()
    {
        var sources = new[] { "libc/GC.gf", "libc/TracingCollector.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Prepend(new SourceFile("a.gf", """
                class Node { public int value; public Node() { value = 42; } }
                Node^ node = new^ Node();
                int CollectDuringInitialization() { GC::Collect(); return node.value; }
                int observed = CollectDuringInitialization();
                int main() { GC::Collect(); return node.value - observed; }
                """));
        var result = CompilerTestHelper.Run(sources);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void StaticAndNamespaceAutoPropertiesUseSharedStorage()
    {
        var result = CompilerTestHelper.Run("""
            namespace State { public int Count { get; set; } = 3; public int Fixed { get; } = 5; }
            static class Settings {
                public static int Answer { get; }
                public static int Count { get; private set; } = 4;
                static Settings() { Answer = 42; }
                public static void Increment() { Count++; }
            }
            int main() {
                Settings::Increment(); State::Count++;
                return Settings::Answer + Settings::Count + State::Count + State::Fixed - 56;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("int* value; int main() { return 0; }", "requires an initializer")]
    [InlineData("readonly(int) value = 1; int main() { value = 2; return 0; }", "readonly")]
    [InlineData("class State { private static int secret = 1; } int main() { return State::secret; }", "private")]
    [InlineData("static class C { static C(int value) {} }", "parameterless")]
    [InlineData("static class C { static Other() {} }", "name must match")]
    [InlineData("static class C { public static int Value { get; } } int main() { C::Value = 3; return 0; }", "Get-only")]
    [InlineData("namespace N { public int Value { get; } = 3; } int main() { N::Value = 4; return 0; }", "Get-only")]
    public void InvalidStorageIsRejected(string source, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(source));
        Assert.Contains(message, error.Message);
    }
}

namespace gflat.Tests;

public class StabilityTests
{
    [Fact]
    public void LambdaReturnInsideDestructorUsesItsOwnFunction()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* format, ...);
            struct S {
                ~S() { int()* f = () => { return 7; }; printf("%d", f()); }
            }
            int main() { S s = new S(); return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("7", result.StandardOutput);
    }

    [Fact]
    public void ThrowingDeferReleasesReplacedException()
    {
        var result = CompilerTestHelper.Run(TrackedAllocator + """
            void fail() throws { printf("T"); throw new* Exception(); }
            int main() {
                try { defer printf("A"); defer fail(); throw new* Exception(); }
                catch { printf("C"); }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("TAFCF", result.StandardOutput);
    }

    [Theory]
    [InlineData("")]
    [InlineData("return 0;")]
    public void ThrowingDeferRunsRemainingCleanupOnce(string exit)
    {
        var result = CompilerTestHelper.Run(TrackedAllocator + """
            void fail() throws { printf("T"); throw new* Exception(); }
            int main() {
                try { defer printf("A"); defer fail(); defer printf("B");
            """ + exit + " } catch { printf(\"C\"); } return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("BTACF", result.StandardOutput);
    }

    [Fact]
    public void UnhandledExceptionRunsDestructorBeforeFree()
    {
        var result = CompilerTestHelper.Run(TrackedAllocator + "class E : Exception { public ~E() { printf(\"D\"); } } int main() { throw new* E(); }");
        Assert.Equal(1, result.ExitCode);
        Assert.EndsWith("DF", result.StandardOutput);
    }

    private const string Leaf = """
        extern int printf(readonly(char)* format, ...);
        struct Leaf {
            public int id;
            public Leaf(int value) { id = value; }
            public int Get() { return id; }
            public ~Leaf() { printf("%d", id); }
        }
        """;

    [Theory]
    [InlineData("struct Outer { Leaf a = new Leaf(1); Leaf b = new Leaf(2); }", "21")]
    [InlineData("struct Outer { Leaf a = new Leaf(1); Leaf b = new Leaf(2); ~Outer() { printf(\"O\"); } }", "O21")]
    public void NestedStructFieldsAreDestroyed(string declaration, string expected)
    {
        var result = CompilerTestHelper.Run(Leaf + declaration + " int main() { { Outer o = new Outer(); } return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public void HeapNestedFieldsAreDestroyed()
    {
        var result = CompilerTestHelper.Run(Leaf + "struct Outer { Leaf a = new Leaf(1); } int main() { Outer* o = new* Outer(); delete o; return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", result.StandardOutput);
    }

    [Theory]
    [InlineData("new Leaf(1);", "1")]
    [InlineData("make();", "2")]
    public void DiscardedTemporaryIsDestroyed(string expression, string expected)
    {
        var result = CompilerTestHelper.Run(Leaf + "Leaf make() { return new Leaf(2); } int main() { " + expression + " return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public void ClassDestructorCanCallVirtualMethod()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char)* format, ...);
            class C {
                public virtual int Value() { return 7; }
                public ~C() { printf("%d", this.Value()); }
            }
            int main() { { C c = new C(); } return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("7", result.StandardOutput);
    }

    [Fact]
    public void EarlyDestructorReturnStillCleansBaseAndFields()
    {
        var result = CompilerTestHelper.Run(Leaf + """
            class Base { public ~Base() { printf("B"); } }
            class Derived : Base {
                Leaf leaf = new Leaf(1);
                public ~Derived() { printf("D"); return; }
            }
            int main() { { Derived d = new Derived(); } return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("D1B", result.StandardOutput);
    }

    [Fact]
    public void NestedFieldsCleanUpOnThrow()
    {
        var result = CompilerTestHelper.Run(Leaf + """
            struct Outer { Leaf leaf = new Leaf(1); }
            void fail() throws { Outer o = new Outer(); throw new* Exception(); }
            int main() { try { fail(); } catch (Exception* e) { printf("C"); } return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1C", result.StandardOutput);
    }

    private const string TrackedAllocator = """
        extern int printf(readonly(char)* format, ...);
        extern void* malloc(ulong size);
        extern void free(void* ptr);
        namespace Allocator { public replace void*? Allocate(ulong size) { return malloc(size); } }
        namespace Allocator { public replace void Free(void* ptr) { printf("F"); free(ptr); } }
        """;

    [Theory]
    [InlineData("return 0;", "DF")]
    [InlineData("break;", "DF")]
    [InlineData("continue;", "DF")]
    public void CatchExitRunsDeferBeforeFree(string exit, string expected)
    {
        var result = CompilerTestHelper.Run(TrackedAllocator +
            "int main() { for (int i = 0; i < 1; i++) { try { throw new* Exception(); } catch { defer printf(\"D\"); " + exit + " } } return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public void ThrowFromCatchReleasesBothExceptions()
    {
        var result = CompilerTestHelper.Run(TrackedAllocator + """
            int main() {
                try { try { throw new* Exception(); } catch (Exception* e) { throw new* Exception(); } }
                catch { printf("C"); }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("FCF", result.StandardOutput);
    }

    [Fact]
    public void RethrowReleasesExceptionOnce()
    {
        var result = CompilerTestHelper.Run(TrackedAllocator + """
            int main() {
                try { try { throw new* Exception(); } catch (Exception* e) { throw e; } }
                catch { printf("C"); }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("CF", result.StandardOutput);
    }

    [Theory]
    [InlineData("make().id")]
    [InlineData("make().Get()")]
    public void TemporaryReceiverLivesThroughAccess(string expression)
    {
        var result = CompilerTestHelper.Run(Leaf + "Leaf make() { return new Leaf(2); } int main() { int value = " + expression + "; printf(\"V%d\", value); return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("2V2", result.StandardOutput);
    }

    [Fact]
    public void NestedMemberAccessUsesAddress()
    {
        var result = CompilerTestHelper.Run(Leaf + "struct Outer { public Leaf leaf = new Leaf(3); } int main() { Outer o = new Outer(); printf(\"%d\", o.leaf.id); return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("33", result.StandardOutput);
    }

    [Fact]
    public void ExceptionDestructorRunsBeforeFree()
    {
        var result = CompilerTestHelper.Run(TrackedAllocator + "class E : Exception { public ~E() { printf(\"D\"); } } int main() { try { throw new* E(); } catch { printf(\"C\"); } return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("CDF", result.StandardOutput);
    }
}

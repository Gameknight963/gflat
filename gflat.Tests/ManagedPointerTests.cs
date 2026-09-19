using gflat.CompileExceptions;

namespace gflat.Tests;

public class ManagedPointerTests
{
    // Test adapter only: malloc does not implement garbage collection.
    private const string Runtime = """
        extern int printf(readonly char* format, ...);
        extern void*? malloc(ulong size);
        void*? __gflat_gc_alloc(ulong size) { printf("A"); return malloc(size); }
        """;

    [Theory]
    [InlineData("int^ p = new^ int(42); return *p;", 42, "A")]
    [InlineData("int^ p = new^ int(); *p = 9; int^ q = p; *q = 17; return *p;", 17, "A")]
    [InlineData("int^ p = new^ int(); return *p;", 0, "A")]
    [InlineData("int^[2] a = [new^ int(3), new^ int(4)]; return *a[0] + *a[1];", 7, "AA")]
    [InlineData("int^? p = new^ int(6); int^ q = (int^)p; return *q;", 6, "A")]
    [InlineData("int^? p = null; if (p == null) { return 7; } return 0;", 7, "")]
    [InlineData("int^ p = new^ int(7); readonly int^ q = p; return *q;", 7, "A")]
    public void ScalarReferencesExecute(string body, int expected, string output)
    {
        var result = CompilerTestHelper.Run(Runtime + "int main() { " + body + " }");
        Assert.Equal(expected, result.ExitCode);
        Assert.Equal(output, result.StandardOutput);
    }

    [Fact]
    public void ReferencesCanBeReturnedPassedAndStoredInManagedObjects()
    {
        var result = CompilerTestHelper.Run(Runtime + """
            struct Box { public int value; public Box(int n) { value = n; } public int Get() { return value; } }
            class Holder { public Box^? item; }
            Box^ make() { return new^ Box(12); }
            int read(Box^ b) { return b.Get(); }
            int main() { Holder^ h = new^ Holder(); h.item = make(); return read((Box^)h.item); }
            """);
        Assert.Equal(12, result.ExitCode);
        Assert.Equal("AA", result.StandardOutput);
    }

    [Fact]
    public void ManagedClassesSupportVirtualAndInterfaceDispatch()
    {
        var result = CompilerTestHelper.Run(Runtime + """
            interface I { int Value(); }
            class Base { public virtual int Value() { return 1; } }
            class Derived : Base, I { public override int Value() { return 7; } }
            int main() { Derived^ d = new^ Derived(); Base^ b = d; I^ i = d; return b.Value() + i.Value(); }
            """);
        Assert.Equal(14, result.ExitCode);
        Assert.Equal("A", result.StandardOutput);
    }

    [Fact]
    public void ScopeExitDoesNotCallRawFree()
    {
        var result = CompilerTestHelper.Run(Runtime + "void __gflat_free(void* p) { printf(\"F\"); } int main() { { int^ p = new^ int(7); } return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("A", result.StandardOutput);
    }

    [Fact]
    public void MissingRuntimeIsDiagnostic()
        => Assert.Contains("__gflat_gc_alloc", Assert.Throws<TypeCheckException>(() => Compiler.Check("int main() { int^ p = new^ int(); return 0; }")).Message);

    [Theory]
    [InlineData("int __gflat_gc_alloc(ulong size) { return 0; }")]
    [InlineData("void*? __gflat_gc_alloc(int size) { return null; }")]
    [InlineData("void*? __gflat_gc_alloc(ulong size) throws { return null; }")]
    [InlineData("extern void*? __gflat_gc_alloc(ulong size, ...);")]
    public void IncorrectRuntimeSignatureIsRejected(string declaration)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(declaration + "int main() { int^ p = new^ int(); return 0; }"));

    [Theory]
    [InlineData("struct S { ~S() {} }", "S")]
    [InlineData("struct S { ~S() {} } struct Outer { S value; }", "Outer")]
    [InlineData("struct S { ~S() {} } struct Outer { S[2] values; }", "Outer")]
    [InlineData("class Base { ~Base() {} } class Derived : Base {}", "Derived")]
    public void DestructorBearingTypesAreRejected(string declarations, string type)
        => Assert.Contains("destructor", Assert.Throws<TypeCheckException>(() => Compiler.Check(Runtime + declarations + "int main() { " + type + "^ p = new^ " + type + "(); return 0; }")).Message);

    [Theory]
    [InlineData("int^ p = new^ int(); delete p;")]
    [InlineData("int^ p = new^ int(); int* q = (int*)p;")]
    [InlineData("int x = 1; int^ p = (int^)&x;")]
    [InlineData("int^ p = new^ int(); ulong x = (ulong)p;")]
    [InlineData("int^ p = (int^)0;")]
    [InlineData("int^ p = new^ int(); p++;")]
    [InlineData("int^ p = new^ int(); int* q = &*p;")]
    [InlineData("int^? p = null; int x = *p;")]
    [InlineData("readonly int^ p = new^ int(); *p = 3;")]
    [InlineData("readonly int^ p = new^ int(); int^ q = (int^)p;")]
    [InlineData("int^ p = new^ int(); float^ q = (float^)p;")]
    [InlineData("int^ p = new^ int(1, 2);")]
    [InlineData("int^ p = new^ int(true);")]
    [InlineData("const int^ p = new^ int();")]
    [InlineData("throw new^ Exception();")]
    [InlineData("try { throw new* Exception(); } catch (Exception^ e) {}")]
    public void UnsupportedManagedOperationsAreRejected(string body)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Runtime + "int main() { " + body + " return 0; }"));

    [Fact]
    public void NullableInterfaceDefaultsAndCheckedCastsWork()
    {
        var result = CompilerTestHelper.Run(Runtime + "interface I { int Get(); } struct S : I { public int Get() { return 9; } } int main() { I^? p = default(I^?); if (p != null) { return 1; } p = new^ S(); I^ q = (I^)p; return q.Get(); }");
        Assert.Equal(9, result.ExitCode);
    }

    [Fact]
    public void NullableCastTrapsOnNull()
    {
        var result = CompilerTestHelper.Run("int main() { int^? p = null; int^ q = (int^)p; return 0; }");
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void ManagedAndRawAllocationHooksAreIndependent()
    {
        var result = CompilerTestHelper.Run(Runtime + """
            void*? __gflat_alloc(ulong size) { printf("R"); return malloc(size); }
            void __gflat_free(void* p) { printf("F"); }
            struct S { public int value; }
            int main() { S* raw = new* S(); S^ managed = new^ S(); delete raw; return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("RAF", result.StandardOutput);
    }

    [Fact]
    public void FieldWritesAreVisibleThroughCopiedReference()
    {
        var result = CompilerTestHelper.Run(Runtime + "struct S { public int value; } int main() { S^ a = new^ S(); S^ b = a; b.value = 23; return a.value; }");
        Assert.Equal(23, result.ExitCode);
    }

    [Fact]
    public void RecursiveManagedFieldsAndGenericReferencesWork()
    {
        var result = CompilerTestHelper.Run(Runtime + """
            class Node { public Node^? next; public int value; }
            struct Box<T> { public T item; public Box(T value) { item = value; } }
            int main() {
                Node^ a = new^ Node(); Node^ b = new^ Node();
                a.next = b; b.next = a; a.value = 13;
                Box<Node^>^ box = new^ Box<Node^>(b);
                Node^ first = (Node^)box.item.next;
                return first.value;
            }
            """);
        Assert.Equal(13, result.ExitCode);
        Assert.Equal("AAA", result.StandardOutput);
    }

    [Fact]
    public void ForeignRuntimeDeclarationIsAccepted()
        => Assert.Contains("call i8* @__gflat_gc_alloc", Compiler.Emit("extern void*? __gflat_gc_alloc(ulong size); int main() { int^ p = new^ int(1); return *p; }"));

    [Fact]
    public void RawAddressOfManagedFieldIsRejected()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Runtime + "struct S { public int value; } int main() { S^ p = new^ S(); int* address = &p.value; return 0; }"));

    [Fact]
    public void AllocationFailureTrapsBeforeUse()
    {
        var result = CompilerTestHelper.Run("void*? __gflat_gc_alloc(ulong size) { return null; } int main() { int^ p = new^ int(1); return *p; }");
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEqual(1, result.ExitCode);
    }
}

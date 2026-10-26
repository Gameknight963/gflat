namespace gflat.Tests;

public sealed class EarlyGenericHierarchyTests
{
    [Fact]
    public void CyclicBasesAreRejectedBeforeGenericBodyInheritanceChecks()
    {
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Emit("""
            struct Box<T> { public void Fail() { throw new A(); } }
            void Call(Box<int>* value) {}
            class A : B {}
            class B : A {}
            int main() => 0;
            """));
        Assert.Contains("Circular inheritance", error.Message);
    }

    [Fact]
    public void GenericParameterSignatureCanThrowAForwardDeclaredException()
    {
        var result = CompilerTestHelper.Run("""
            struct Box<T> { public void Fail() { throw new Failure(); } }
            void Call(Box<int>* value) { value.Fail(); }
            class Failure : Exception { public Failure() : base("failure", 42) {} }
            int main() {
                Box<int> value = new Box<int>();
                try { Call(&value); } catch (ref Failure error) { return error.GetCode(); }
                return 1;
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ManagedVectorGrowthFailureUsesBadAllocAndPreservesRoots()
    {
        var result = CompilerTestHelper.Run(new[] { "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf", "libc/GC.gf", "libc/TracingCollector.gf", "core/Collections/Vector.gf", "core/Memory/Span.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", """
                using std; using std::Memory; using std::Queries; using std::Collections;
                extern void*? malloc(nuint size);
                extern void free(void* storage);
                namespace Memory {
                    public replace void*? Allocate(nuint size) {
                        if (size >= (nuint)100 * (nuint)sizeof(int^) ||
                            size == (nuint)sizeof(BadAllocException)) return null;
                        return malloc(size);
                    }
                    public replace void Free(void* storage) nothrow { free(storage); }
                }
                void Fill(Vector<int^>* values) { values.Add(new^ int(42)); }
                int main() {
                    Vector<int^> values = new Vector<int^>(); Fill(&values);
                    nuint capacity = values.Capacity;
                    try { values.Reserve(100); return 1; }
                    catch (ref BadAllocException error) { if (error.GetCode() != 1) return 2; }
                    GC::Collect();
                    if (values.Count != (nuint)1 || values.Capacity != capacity || *values[(nuint)0] != 42) return 3;
                    return 0;
                }
                """)));
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}

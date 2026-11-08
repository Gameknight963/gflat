using gflat.CompileExceptions;

namespace gflat.Tests.Std.Memory;

public sealed class UniqueGetTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PointerBorrowsPreserveOwnershipIdentityAndEmptyStateWithoutAllocating(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Memory;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int allocations = 0;
            int destroyed = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    allocations++;
                    if (deny) return null;
                    return malloc(bytes);
                }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            struct Payload
            {
                public int Number;
                public Payload(int number) { Number = number; }
                ~Payload() { destroyed++; }
            }
            int main()
            {
                Payload*? released = null;
                {
                    Unique<Payload> empty = new();
                    Unique<Payload> owner = new(new* Payload(42));
                    Payload* original = &owner.Value;
                    readonly(Unique<Payload>)* shallow = &owner;
                    immutable(Unique<Payload>)* deep = &owner;
                    int before = allocations;
                    deny = true;
                    if (empty.Get() != null || empty.GetImmutable() != null) return 1;
                    if (owner.Get() != original || shallow.Get() != original || deep.GetImmutable() != original) return 2;
                    shallow.Get()!.Number = 43;
                    if (deep.GetImmutable()!.Number != 43 || owner.IsEmpty || destroyed != 0 || allocations != before) return 3;
                    released = owner.Release();
                    if (released != original || owner.Get() != null || owner.GetImmutable() != null || allocations != before) return 4;
                }
                if (destroyed != 0 || released!.Number != 43) return 5;
                delete released!;
                if (destroyed != 1) return 6;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Unique pointer borrow exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData("owner.GetImmutable()!.Number = 0;")]
    [InlineData("*owner.GetImmutable()!.Data = 0;")]
    public void ImmutablePointerBorrowsKeepDeepProtection(string statement)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using Standard::Memory;

            struct Payload { public int Number; public int* Data; }
            """ + "\nvoid Invalid(immutable(Unique<Payload>)* owner) { " + statement + " }")));

    private static IEnumerable<SourceFile> Sources(string code)
        => StandardTestSources.Load("core/Memory/Unique.gf").Append(new SourceFile("unique-get.gf", code));
}

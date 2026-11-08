using gflat.CompileExceptions;

namespace gflat.Tests.Std.Memory;

public sealed class UniqueResetSwapTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("UNIQUE.md", 2);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SwappingAndResettingOwnersPreservesAddressesWithoutAllocating(int optimization)
    {
        Run("""
            using Standard;
            using Standard::Memory;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            bool deny = false;
            int allocations = 0;
            int frees = 0;
            int destroyed = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    allocations++;
                    if (deny) return null;
                    return malloc(bytes);
                }
                public replace void Free(void* storage) nothrow
                {
                    frees++;
                    free(storage);
                }
            }
            struct Payload
            {
                public int Value;
                public Payload(int value) { Value = value; }
                ~Payload() { destroyed += Value; }
            }
            int main()
            {
                {
                    Unique<Payload> first = new(new* Payload(21));
                    Unique<Payload> second = new(new* Payload(22));
                    Unique<Payload> empty = new();
                    Payload* one = &first.Value;
                    Payload* two = &second.Value;
                    int before = allocations;
                    deny = true;
                    first.Swap(ref second);
                    if (&first.Value != two || &second.Value != one || destroyed != 0) return 1;
                    first.Swap(ref first);
                    if (&first.Value != two) return 2;
                    second.Swap(ref empty);
                    if (!second.IsEmpty || &empty.Value != one) return 3;
                    second.Reset();
                    first.Reset();
                    first.Reset();
                    if (!first.IsEmpty || destroyed != 22 || frees != 1) return 4;
                    empty.Swap(ref first);
                    if (!empty.IsEmpty || &first.Value != one) return 5;
                    first.Reset();
                    if (destroyed != 43 || frees != 2 || allocations != before) return 6;
                }
                if (destroyed != 43 || allocations != frees) return 7;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ResetAndSwappedCleanupUseTheDynamicPointeeDestructor(int optimization)
    {
        Run("""
            using Standard;
            using Standard::Memory;

            int baseDestroyed = 0;
            int derivedDestroyed = 0;
            class Base { ~Base() { baseDestroyed++; } }
            class Derived : Base { ~Derived() { derivedDestroyed++; } }
            int main()
            {
                Unique<Base> first = new((Base*)new* Derived());
                Unique<Base> second = new();
                first.Swap(ref second);
                second.Reset();
                if (!second.IsEmpty || baseDestroyed != 1 || derivedDestroyed != 1) return 1;
                try
                {
                    Unique<Base> owner = new((Base*)new* Derived());
                    Unique<Base> destination = new();
                    owner.Swap(ref destination);
                    throw new Exception("after swap");
                }
                catch (ref Exception error) { }
                if (baseDestroyed != 2 || derivedDestroyed != 2) return 2;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData("readonly", "owner.Reset();")]
    [InlineData("immutable", "owner.Reset();")]
    [InlineData("readonly", "Unique<int> other = new(); owner.Swap(ref other);")]
    [InlineData("immutable", "Unique<int> other = new(); owner.Swap(ref other);")]
    public void MutationRequiresAMutableOwner(string qualifier, string operation)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources($$"""
            using Standard::Memory;

            void Invalid({{qualifier}}(Unique<int>)* owner) { {{operation}} }
            """)));

    private static IEnumerable<SourceFile> Sources(string code)
        => StandardTestSources.Load("core/Memory/Unique.gf").Append(new SourceFile("unique-reset-swap.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Unique reset/swap exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardArenaTests
{
    private static IEnumerable<SourceFile> Sources(string program) => new[] { "Memory/Arena.gf" }
        .Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
        .Append(new SourceFile("main.gf", program));

    [Fact]
    public void AlignmentGrowthAndZeroLengthRequestsKeepEarlierStorageValid()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                Arena arena = new(32);
                byte* first = (byte*)arena.Allocate(1, 1); *first = (byte)42;
                for (nuint alignment = 1; alignment <= 256; alignment *= 2) {
                    byte* data = (byte*)arena.Allocate(17, alignment);
                    if ((nuint)data % alignment != 0) return 1;
                    for (nuint i = 0; i < 17; i++) data[i] = (byte)i;
                    if (*first != (byte)42) return 2;
                }
                void* empty = arena.Allocate(0);
                void* another = arena.Allocate(0);
                if (empty == another || (nuint)empty % (nuint)alignof(nuint) != 0) return 3;
                arena.Reset(); arena.Reset();
                int* value = (int*)arena.Allocate((nuint)sizeof(int)); *value = 99;
                return *value - 99;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void InvalidSizesAndAlignmentsPreserveEarlierAllocations()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                Arena arena = new(32); int* value = (int*)arena.Allocate((nuint)sizeof(int)); *value = 42;
                int caught = 0;
                try { arena.Allocate(1, 0); } catch (ref Exception error) { caught++; }
                try { arena.Allocate(1, 3); } catch (ref Exception error) { caught++; }
                try { arena.Allocate(1, 6); } catch (ref Exception error) { caught++; }
                try { arena.Allocate((nuint)0 - (nuint)1, 1); } catch (ref Exception error) { caught++; }
                try { arena.Allocate((nuint)0 - (nuint)1, 256); } catch (ref Exception error) { caught++; }
                try { Arena invalid = new(0); } catch (ref Exception error) { caught++; }
                if (*value != 42) return 1;
                return caught - 6;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FailedBlockAllocationPreservesExistingStorageAndItsFreeSpace()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            bool fail = false;
            extern void* malloc(nuint bytes); extern void free(void* pointer);
            namespace Memory {
                replace void*? Allocate(nuint bytes) { if (fail) return null; return malloc(bytes); }
                replace void Free(void* pointer) { free(pointer); }
            }
            int main() {
                Arena arena = new(64);
                int* first = (int*)arena.Allocate((nuint)sizeof(int)); *first = 42;
                fail = true;
                try { arena.Allocate(1000); return 1; } catch (ref BadAllocException error) {}
                if (*first != 42) return 2;
                int* second = (int*)arena.Allocate((nuint)sizeof(int)); *second = 99;
                if (*first != 42 || *second != 99) return 3;
                fail = false;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void MovingResettingAndDestroyingBalanceAllBackingAllocations()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int allocated = 0; int released = 0;
            extern void*? malloc(nuint bytes); extern void free(void* pointer);
            namespace Memory {
                replace void*? Allocate(nuint bytes) {
                    void*? result = malloc(bytes); if (result != null) allocated++; return result;
                }
                replace void Free(void* pointer) { released++; free(pointer); }
            }
            int main() {
                {
                    Arena original = new(16);
                    original.Allocate(8); original.Allocate(100);
                    if (allocated != 2 || released != 0) return 1;
                    Arena moved = move original;
                    original = new(16); original.Allocate(1);
                    moved.Reset();
                    if (released != 2) return 2;
                    moved.Allocate(1);
                }
                if (allocated != 4 || released != 4) return 3;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void PlacementObjectDestructionRemainsTheCallersResponsibility()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            struct Resource {
                private int* destroyed;
                public Resource(int* counter) { destroyed = counter; }
                ~Resource() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0;
                {
                    Arena arena = new();
                    Resource* first = new* Resource(&destroyed) at (Resource*)arena.Allocate((nuint)sizeof(Resource), (nuint)alignof(Resource));
                    arena.Reset();
                    if (destroyed != 0) return 1;
                    Resource* second = new* Resource(&destroyed) at (Resource*)arena.Allocate((nuint)sizeof(Resource), (nuint)alignof(Resource));
                    second.~Resource();
                }
                return destroyed - 1;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ArenaCannotBeCopiedImplicitly()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using std;
            int main() { Arena first = new(); Arena second = first; return 0; }
            """)));
        Assert.Contains("copy", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

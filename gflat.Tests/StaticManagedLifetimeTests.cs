namespace gflat.Tests;

public sealed class StaticManagedLifetimeTests
{
    [Theory]
    [InlineData("struct", "Owner owner = new Owner();", "MD")]
    [InlineData("class", "Owner owner = new Owner();", "MD")]
    [InlineData("struct", "Owner[2] owners = [new Owner(), new Owner()];", "MDD")]
    public void OwningGlobalRemovesItsManagedSlotsExactlyOnce(string kind, string declaration, string expected)
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            extern void*? malloc(nuint bytes);
            static class Probe : std::IGarbageCollector {
                private static void*?[128] slots;
                private static nuint count;
                public static void* Allocate(nuint bytes, nuint alignment) => (void*)malloc(bytes);
                public static nuint Collect() => 0;
                public static void ReferenceAdded(void* address) nothrow {
                    for (nuint i = 0; i < count; i++) if (slots[i] == address) return;
                    slots[count] = address; count++;
                }
                public static void ReferenceRemoved(void* address) nothrow {
                    for (nuint i = 0; i < count; i++) {
                        if (slots[i] != address) continue;
                        count--; slots[i] = slots[count]; slots[count] = null;
                        return;
                    }
                    putchar(88);
                }
            }
            """ + kind + """
             Owner {
                private managed<int, Probe>? item;
                public Owner() { item = null; }
                ~Owner() { putchar(68); }
            }
            """ + declaration + """
            int main() { putchar(77); return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }
}

namespace gflat.Tests;

public sealed class StaticManagedLifetimeTests
{
    [Theory]
    [InlineData("struct", "Owner owner = new Owner();", "MD")]
    [InlineData("class", "Owner owner = new Owner();", "MD")]
    [InlineData("struct", "Owner[2] owners = [new Owner(), new Owner()];", "MDD")]
    public void OwningGlobalRemovesItsManagedSlotsExactlyOnce(string kind, string declaration, string expected)
    {
        var result = CompilerTestHelper.Run(ProbeSource + kind + """
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

    [Fact]
    public void FailedManagedGlobalInitializerUnregistersItsStorageBeforeTheUnhandledHook()
    {
        var result = CompilerTestHelper.Run(ProbeSource + """
            extern void exit(int status);
            managed<int, Probe>? Fail() { throw new Exception(); }
            managed<int, Probe>? value = Fail();
            namespace Exceptions {
                public replace void Unhandled(Exception*? error) {
                    putchar(48 + (int)Probe::Count()); exit(17);
                }
            }
            int main() => 99;
            """);
        Assert.Equal(17, result.ExitCode);
        Assert.Equal("0", result.StandardOutput);
    }

    [Theory]
    [InlineData("Owner value = Fail();")]
    [InlineData("Owner value;")]
    [InlineData("Owner value; value = Fail();")]
    [InlineData("Owner value; value = new Owner();")]
    public void UnconstructedOrFailedOwningLocalsDoNotLeaveStackSlotsRegistered(string declaration)
    {
        var result = CompilerTestHelper.Run(ProbeSource + OwnerSource + """
            Owner Fail() { throw new Exception(); }
            void Work() { DECLARATION }
            int main() { try { Work(); } catch {} return (int)Probe::Count(); }
            """.Replace("DECLARATION", declaration));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }


    private const string OwnerSource = """
        struct Owner {
            private managed<int, Probe>? item;
            public Owner() { item = null; }
            ~Owner() {}
        }
        """;

    private const string ProbeSource = """
            extern int putchar(int value);
            extern void*? malloc(nuint bytes);
            static class Probe : std::IGarbageCollector {
                private static void*?[128] slots;
                private static nuint count;
                public static void* Allocate(nuint bytes, nuint alignment) => (void*)malloc(bytes);
                public static nuint Collect() => 0;
                public static nuint Count() => count;
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
        """;

}

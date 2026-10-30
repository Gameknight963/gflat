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
    [InlineData("Owner value = new Owner(); value = new Owner();")]
    [InlineData("Owner value = new Owner(); Owner other = move value; value = new Owner();")]
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

    [Fact]
    public void ReturningAnOwningLocalTransfersTheValueAndUnregistersItsStackStorage()
    {
        var result = CompilerTestHelper.Run(ProbeSource + OwnerSource + """
            Owner Create() { Owner value = new Owner(); return value; }
            void Work() { Owner value = Create(); }
            int main() { Work(); return (int)Probe::Count(); }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public void ConstructorFieldAndGlobalReassignmentBalanceManagedRoots()
    {
        var result = CompilerTestHelper.Run(ProbeSource + OwnerSource + """
            struct Box {
                private Owner item;
                public Box() { item = new Owner(); item = new Owner(); }
            }
            Owner globalValue = new Owner();
            int main() {
                { Box value = new Box(); }
                globalValue = new Owner();
                return (int)Probe::Count() - 1;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Theory]
    [InlineData("managed<int, Probe>? value = null; managed<int, Probe>? other = move value;")]
    [InlineData("managed<int, Probe>? value = null; managed<int, Probe>? other = move value; value = null;")]
    [InlineData("Take(null);")]
    [InlineData("Consume(new Owner());")]
    public void ConsumedManagedLocalsAndParametersLeaveBalancedEmptyStorage(string body)
    {
        var result = CompilerTestHelper.Run(ProbeSource + OwnerSource + """
            void Take(managed<int, Probe>? input) { managed<int, Probe>? other = move input; }
            void Consume(Owner input) { Owner other = move input; }
            void Work() { BODY }
            int main() { Work(); return (int)Probe::Count(); }
            """.Replace("BODY", body));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public void VectorManagedElementsBalanceParameterMovesAndRawStorageRelocation()
    {
        var sources = new[] { "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf", "core/Memory/Span.gf", "core/Collections/Vector.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("main.gf", "using Standard; using Standard::Memory; using Standard::Queries; using Standard::Collections;\n" + ProbeSource + """
                void Work() {
                    Vector<managed<int, Probe>?> values = new Vector<managed<int, Probe>?>();
                    values.Add(null); values.Add(null); values.Reserve(16);
                }
                int main() { Work(); return (int)Probe::Count(); }
                """));
        var result = CompilerTestHelper.Run(sources);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Theory]
    [InlineData("Owner value = new Owner();")]
    [InlineData("Owner* value = new* Owner();")]
    [InlineData("void* storage = Memory::Allocate((nuint)sizeof(Owner))!; defer { Memory::Free(storage); } new* Owner() at (Owner*)storage;")]
    public void ThrowingConstructorLeavesNoManagedRoots(string declaration)
    {
        var result = CompilerTestHelper.Run(ProbeSource + """
            struct Owner {
                private managed<int, Probe>? item;
                public Owner() { item = null; throw new Exception(); }
                ~Owner() { putchar(68); }
            }
            void Work() { DECLARATION }
            int main() { try { Work(); } catch {} return (int)Probe::Count(); }
            """.Replace("DECLARATION", declaration));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public void PartiallyConstructedArrayRemovesEachManagedRootExactlyOnce()
    {
        var result = CompilerTestHelper.Run(ProbeSource + OwnerSource + """
            Owner Fail() { throw new Exception(); }
            void Work() { Owner[2] values = [new Owner(), Fail()]; }
            int main() { try { Work(); } catch {} return (int)Probe::Count(); }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Theory]
    [InlineData("Owner value = new Owner();", "D")]
    [InlineData("Owner* value = new* Owner();", "D")]
    [InlineData("void* storage = Memory::Allocate((nuint)sizeof(Owner))!; defer { Memory::Free(storage); } new* Owner() at (Owner*)storage;", "D")]
    [InlineData("Child a = new Child(); Child b = a;", "D")]
    [InlineData("Child[2] a = [new Child(), new Child()]; Child[2] b = a;", "DD")]
    public void PartialSubobjectDestructionLeavesRootRemovalToItsEnclosingStorage(string body, string output)
    {
        var result = CompilerTestHelper.Run(ProbeSource + """
            struct Child {
                private managed<int, Probe>? item;
                public Child() { item = null; }
                public copy Child(immutable(Child)* source) { item = null; throw new Exception(); }
                ~Child() { putchar(68); }
            }
            struct Owner {
                private Child child;
                public Owner() { child = new Child(); throw new Exception(); }
            }
            void Work() { BODY }
            int main() { try { Work(); } catch {} return (int)Probe::Count(); }
            """.Replace("BODY", body));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(output, result.StandardOutput);
    }

    [Theory]
    [InlineData("Child[2] a = [new Child(), new Child()]; Child[2] b = a;")]
    [InlineData("Pair a = new Pair(); Pair b = a;")]
    public void PartialAutomaticCopyDestroysOnlyCompletedElementsAndBalancesRoots(string body)
    {
        var result = CompilerTestHelper.Run(ProbeSource + """
            struct Child {
                private managed<int, Probe>? item;
                private static int copies;
                public Child() { item = null; }
                public copy Child(immutable(Child)* source) {
                    item = null; copies++;
                    if (copies == 2) throw new Exception();
                }
                ~Child() { putchar(68); }
            }
            struct Pair {
                private Child first;
                private Child second;
                public Pair() { first = new Child(); second = new Child(); }
            }
            void Work() { BODY }
            int main() { try { Work(); } catch {} return (int)Probe::Count(); }
            """.Replace("BODY", body));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("DDD", result.StandardOutput);
    }

    [Fact]
    public void CompletedBaseConstructionOnDerivedFailureLeavesRootRemovalToCaller()
    {
        var result = CompilerTestHelper.Run(ProbeSource + """
            class Base {
                private managed<int, Probe>? item;
                public Base() { item = null; }
                ~Base() { putchar(66); }
            }
            class Derived : Base {
                private managed<int, Probe>? other;
                public Derived() { other = null; throw new Exception(); }
            }
            void Work() { Derived value = new Derived(); }
            int main() { try { Work(); } catch {} return (int)Probe::Count(); }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("B", result.StandardOutput);
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void ImplicitConstructorFailureDestroysCompletedOwningField(string kind)
    {
        var result = CompilerTestHelper.Run(ProbeSource + """
            struct Child {
                private managed<int, Probe>? item;
                public Child() { item = null; }
                ~Child() { putchar(68); }
            }
            int Fail() { throw new Exception(); }
            """ + kind + """
             Owner {
                private Child child = new Child();
                private int failure = Fail();
            }
            void Work() { Owner value = new Owner(); }
            int main() { try { Work(); } catch {} return (int)Probe::Count(); }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("D", result.StandardOutput);
    }

    [Theory]
    [InlineData("Consume(new Owner(), Fail());")]
    [InlineData("Consume(new Owner(), 0);")]
    [InlineData("Throws(new Owner());")]
    [InlineData("Holder holder = new Holder(new Owner(), Fail());")]
    [InlineData("Holder holder = new Holder(new Owner(), 0);")]
    public void OwningArgumentRootsAreBalancedBeforeAndAfterCallTransfer(string body)
    {
        var result = CompilerTestHelper.Run(ProbeSource + OwnerSource.Replace("~Owner() {}", "~Owner() { putchar(68); }") + """
            int Fail() { throw new Exception(); }
            void Consume(Owner first, int second) {}
            void Throws(Owner value) { throw new Exception(); }
            struct Holder { public Holder(Owner first, int second) { throw new Exception(); } }
            void Work() { BODY }
            int main() { try { Work(); } catch {} return (int)Probe::Count(); }
            """.Replace("BODY", body));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("D", result.StandardOutput);
    }

    [Theory]
    [InlineData("Owner value = new Owner(); (Owner)value;")]
    [InlineData("Owner value = new Owner(); Owner other = (Owner)value;")]
    [InlineData("(Owner)new Owner();")]
    [InlineData("Owner value = new Owner(); (Owner)move value;")]
    [InlineData("Owner value = new Owner(); try { (Owner)value; } catch {}", true)]
    public void CastedOwningValuesBalanceManagedStorage(string body, bool throws = false)
    {
        string owner = OwnerSource.Replace("~Owner() {}", "public copy Owner(immutable(Owner)* source) { item = null; " +
            (throws ? "throw new Exception();" : "") + " } ~Owner() {}");
        var result = CompilerTestHelper.Run(ProbeSource + owner + """
            void Work() { BODY }
            int main() { Work(); return (int)Probe::Count(); }
            """.Replace("BODY", body));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Theory]
    [InlineData("throw new Exception();", "t$\"text\";")]
    [InlineData("item = null;", "t$\"text\";")]
    [InlineData("item = null;", "Destination value = t$\"text\";")]
    [InlineData("", "t$\"text\";")]
    [InlineData("item = null;", "Destination value = t$\"text\"; value.Write(\"x\", 1);")]
    [InlineData("item = null;", "Destination value = t$\"text\";", false)]
    [InlineData("throw new Exception();", "t$\"text\";", false)]
    [InlineData("item = null;", "Destination value = t$\"\";")]
    [InlineData("item = null;", "t$\"{t$\"nested\"}\";", true, "DD")]
    public void InterpolationDestinationBalancesManagedRootsDuringWritesAndTransfer(string write, string body, bool owning = true, string output = "D")
    {
        var result = CompilerTestHelper.Run(ProbeSource + """
            struct Destination : ITextWriter, IFormattable {
                private managed<int, Probe>? item;
                public Destination() { item = null; }
                public static Destination operator t""(readonly(char)* text, nuint count) => new Destination();
                public void Write(readonly(char)* text, nuint count) { WRITE }
                public immutable void Format(ITextWriter* output) { output.Write("nested", 6); }
                DESTRUCTOR
            }
            void Work() { BODY }
            int main() { try { Work(); } catch {} return (int)Probe::Count(); }
            """.Replace("WRITE", write).Replace("BODY", body)
                .Replace("DESTRUCTOR", owning ? "~Destination() { putchar(68); }" : ""));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(owning ? output : "", result.StandardOutput);
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
            static class Probe : Standard::IGarbageCollector {
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

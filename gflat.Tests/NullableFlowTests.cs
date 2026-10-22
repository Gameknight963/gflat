using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NullableFlowTests
{
    private const string Item = """
        struct Item {
            public int value;
            public Item(int n) { value = n; }
            public immutable int Get() => value;
            ~Item() {}
        }
        void Other() {}
        bool Reset(ref Item*? value) { value = null; return true; }
        """;

    [Theory]
    [InlineData("if (p == null) return 0; return p.Get();", 42)]
    [InlineData("if (null == p) return 0; return p.value;", 42)]
    [InlineData("if (!(p != null)) return 0; return p.value;", 42)]
    [InlineData("if (p == null) throw new Exception(); return p.value;", 42)]
    [InlineData("if (p != null) return p.value; return 0;", 42)]
    [InlineData("if (p != null && p.Get() == 42) return p.value; return 0;", 42)]
    [InlineData("if (p == null || p.Get() != 42) return 0; return p.value;", 42)]
    [InlineData("if (p == null) return 0; Other(); return p.value;", 42)]
    [InlineData("if (p == null) return 0; { Item*? p = null; if (p != null) return p.value; } return p.value;", 42)]
    [InlineData("while (p != null) { if (p.value == 42) return p.value; p = null; } return 0;", 42)]
    [InlineData("try { if (p == null) throw new Exception(); } catch { return 0; } return p.value;", 42)]
    [InlineData("if (p == null) return 0; p = null; return 0;", 0)]
    [InlineData("if (p == null) return 0; Reset(ref p); if (p == null) return 0; return p.value;", 0)]
    [InlineData("if (p == null) return 0; Item*?* slot = &p; if (p == null) return 0; return p.value;", 42)]
    public void NullGuardsNarrowReadsWithoutChangingStorage(string body, int expected)
    {
        var result = CompilerTestHelper.Run(Item + "int Read(Item*? p) { " + body + " } int main() { Item item = new Item(42); return Read(&item); }");
        Assert.Equal(expected, result.ExitCode);
    }


    [Theory]
    [InlineData("return p.value;")]
    [InlineData("if (p != null) { } return p.value;")]
    [InlineData("if (p == null) return 0; p = null; return p.value;")]
    [InlineData("if (p == null) return 0; Reset(ref p); return p.value;")]
    [InlineData("if (p != null && Reset(ref p)) return p.value; return 0;")]
    [InlineData("if (p == null) return 0; Item*?* slot = &p; Other(); return p.value;")]
    [InlineData("Item*?* slot = &p; if (p == null) return 0; *slot = null; return p.value;")]
    [InlineData("if (p == null) return 0; while (true) { Other(); break; } return p.value;")]
    [InlineData("if (p == null) return 0; defer { p.value = 1; } p = null; return 0;")]
    [InlineData("if (p == null) return 0; { defer { p = null; } if (p == null) return 0; } return p.value;")]
    [InlineData("if (p == null) return 0; try { p = null; throw new Exception(); } catch {} return p.value;")]
    [InlineData("while (p != null) { p = null; return p.value; } return 0;")]
    public void InvalidatedOrIncompleteFactsDoNotPermitNullableReads(string body)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Item + "int Read(Item*? p) { " + body + " }"));

    [Fact]
    public void BranchAssignmentsAndReadonlyPointersPreserveTheirTypes()
    {
        Assert.Equal(42, CompilerTestHelper.Run(Item + """
            int Read(immutable(Item)*? p) { if (p == null) return 0; return p.Get(); }
            int main() {
                Item first = new Item(42); Item second = new Item(12);
                Item*? p;
                if (first.value == 42) p = &first; else p = &second;
                return Read(p);
            }
            """).ExitCode);
        Assert.Throws<TypeCheckException>(() => Compiler.Check(Item + """
            void Bad(immutable(Item)*? p) { if (p == null) return; p.value = 3; }
            """));
    }

    [Fact]
    public void RawManagedAndFunctionPointerGuardsExecute()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            extern void*? malloc(nuint bytes);
            alias GC = TestCollector;
            static class TestCollector {
                public static void* Allocate(nuint bytes, nuint alignment) => malloc(bytes)!;
                public static void ReferenceAdded(void* slot) nothrow {}
                public static void ReferenceRemoved(void* slot) nothrow {}
            }
            int Read(int^? p) { if (p == null) return 0; return *p; }
            int Call(int()*? f) { if (f == null) return 0; return f(); }
            int Value() => 2;
            int main() { int^? p = new^ int(40); int()*? f = &Value; return Read(p) + Call(f); }
            """).ExitCode);
    }

    [Fact]
    public void RefParametersAreInvalidatedByPotentialMutatingCalls()
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Check(Item + """
            int Bad(ref Item*? p) { if (p == null) return 0; Other(); return p.value; }
            """));
        Compiler.Check(Item + """
            int Good(ref Item*? p) { if (p == null) return 0; return p.value; }
            """);
    }

    [Fact]
    public void DeferredBodiesCanCheckTheirOwnNullableValues()
    {
        Assert.Equal(42, CompilerTestHelper.Run(Item + """
            int Read(Item*? p) { defer { if (p != null) p.value = 42; } return 0; }
            int main() { Item item = new Item(1); Read(&item); return item.value; }
            """).ExitCode);
    }

    [Fact]
    public void FieldsAndPropertiesDoNotInheritLocalNarrowing()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Item + """
            struct Holder { public Item*? item; }
            int Bad(Holder* holder) { if (holder.item == null) return 0; return holder.item.value; }
            """));

    [Theory]
    [InlineData("Item constructed = new Item(1);")]
    [InlineData("delete garbage;")]
    [InlineData("garbage.~Item();")]
    [InlineData("Number sum = first + second;")]
    [InlineData("first += second;")]
    [InlineData("Owner copied = original;")]
    public void ImplicitUserCallsInvalidateEscapedSlotFacts(string operation)
    {
        string source = Item + """
            struct Number {
                public int value;
                public static Number operator +(Number a, Number b) => new Number();
            }
            struct Owner {
                public Owner() {}
                public copy Owner(immutable(Owner)* other) {}
                ~Owner() {}
            }
            int Read(Item*? p) {
                Item* garbage = new* Item(1);
                Number first = new Number(); Number second = new Number();
                Owner original = new Owner();
                Item*?* slot = &p;
                if (p == null) return 0;
            """ + operation + " return p.value; }";
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private const string Mutator = """
        struct Mutator {
            private Item*?* slot;
            public Mutator(Item*?* storage) { slot = storage; }
            public int Value { get { *slot = null; return 1; } set { *slot = null; } }
            public int this[int index] { get { *slot = null; return index; } set { *slot = null; } }
        }
        struct Nuller {
            private Item*?* slot;
            public Nuller(Item*?* storage) { slot = storage; }
            ~Nuller() { *slot = null; }
        }
        """;

    [Theory]
    [InlineData("int value = mutator.Value;")]
    [InlineData("mutator.Value = 1;")]
    [InlineData("int value = mutator[0];")]
    [InlineData("mutator[0] = 1;")]
    public void PropertyAndIndexerCallsInvalidateEscapedSlots(string operation)
    {
        string source = Item + Mutator + "int Read(Item*? p) { Mutator mutator = new Mutator(&p); if (p == null) return 0; " + operation;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source + " return p.value; }"));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, CompilerTestHelper.Run(source + " if (p == null) return 0; return p.value; } int main() { Item item = new Item(42); return Read(&item); }").ExitCode);
    }

    [Fact]
    public void ScopeDestructorsInvalidateEscapedSlots()
    {
        string source = Item + Mutator + """
            int Read(Item*? p) {
                { Nuller owner = new Nuller(&p); if (p == null) return 0; }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source + " return p.value; }"));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, CompilerTestHelper.Run(source + " if (p == null) return 0; return p.value; } int main() { Item item = new Item(42); return Read(&item); }").ExitCode);
    }

    [Fact]
    public void ForInitializerDoesNotProveLaterIterationsNonNull()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Item + """
            int main() {
                Item item = new Item(42); bool again = true;
                for (Item*? p = &item; again; p = null) { int n = p.value; }
                return 0;
            }
            """));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(42, CompilerTestHelper.Run(Item + """
            int main() {
                Item item = new Item(42);
                for (Item*? p = &item; p != null; p = null) { return p.value; }
                return 0;
            }
            """).ExitCode);
    }

    private const string CopyOnly = """
        struct CopyOnly {
            private Item*?* slot;
            public CopyOnly(Item*?* storage, int ignored = 0) { slot = storage; }
            public copy CopyOnly(immutable(CopyOnly)* source) nothrow {
                slot = (Item*?*)source.slot;
                *slot = null;
            }
        }
        struct Sink { public Sink(CopyOnly owned, int number) {} }
        int Consume(CopyOnly owned, int number) => number;
        """;

    [Theory]
    [InlineData("CopyOnly copied = original; return p.value;")]
    [InlineData("return Consume(original, p.value);")]
    [InlineData("Sink sink = new Sink(original, p.value); return 0;")]
    [InlineData("CopyOnly[2] values = [original, new CopyOnly(slot, p.value)]; return 0;")]
    [InlineData("CopyOnly copied = (CopyOnly)original; return p.value;")]
    public void NothrowCopiesInvalidateBeforeSubsequentReads(string operation)
    {
        string source = Item + CopyOnly + """
            int Read(Item*? p) {
                Item*?* slot = &p;
                CopyOnly original = new CopyOnly(slot);
                if (p == null) return 0;
            """ + operation + " }";
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CopySideEffectsRunAndAnIgnoredBorrowDoesNotCopy()
    {
        Assert.Equal(0, CompilerTestHelper.Run(Item + CopyOnly + """
            int Read(Item*? p) {
                CopyOnly original = new CopyOnly(&p);
                if (p == null) return 0;
                original;
                int before = p.value;
                CopyOnly copied = original;
                if (p == null) return 0;
                return before;
            }
            int main() { Item item = new Item(42); return Read(&item); }
            """).ExitCode);
    }

    [Fact]
    public void IgnoredFreshValuesStillAccountForTheirDestructor()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Item + """
            extern void SideEffect();
            struct Temporary { ~Temporary() { SideEffect(); } }
            int Read(Item*? p) {
                Item*?* slot = &p;
                if (p == null) return 0;
                default(Temporary);
                return p.value;
            }
            """));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReplacingAnOwnedValueAccountsForItsOldDestructor()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Item + """
            extern void SideEffect();
            struct Temporary { ~Temporary() { SideEffect(); } }
            int Read(Item*? p) {
                Temporary owner = default(Temporary);
                Item*?* slot = &p;
                if (p == null) return 0;
                owner = default(Temporary);
                return p.value;
            }
            """));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("p++;")]
    [InlineData("p += 1;")]
    [InlineData("p = p + 1;")]
    public void GuardedPointerArithmeticUsesAReadWithoutChangingSlotType(string operation)
    {
        Assert.Equal(42, CompilerTestHelper.Run("int Read(int*? p) { if (p == null) return 0; " + operation +
            " if (p == null) return 0; return *p; } int main() { int[2] values = [1, 42]; return Read(values); }").ExitCode);
    }

    [Fact]
    public void GuardedInterfacePointersDispatchFromStructsClassesAndNull()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            interface I { immutable int Get(); }
            struct S : I { public immutable int Get() => 20; }
            class C : I { public immutable int Get() => 22; }
            int Read(I*? p) { if (p == null) return 0; return p.Get(); }
            int main() { S first = new S(); C second = new C(); return Read(&first) + Read(&second) + Read(null); }
            """).ExitCode);
    }

    [Fact]
    public void RefParameterWritesInvalidateOtherPotentiallyAliasedSlots()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Item + """
            int Bad(ref Item*? p, ref Item*? q) { if (p == null) return 0; q = null; return p.value; }
            """));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, CompilerTestHelper.Run(Item + """
            int Read(ref Item*? p, ref Item*? q) {
                if (p == null) return 0;
                q = null;
                if (p == null) return 0;
                return p.value;
            }
            int main() { Item item = new Item(42); Item*? p = &item; return Read(ref p, ref p); }
            """).ExitCode);
    }
}

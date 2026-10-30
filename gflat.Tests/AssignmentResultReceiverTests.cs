using System.Text.RegularExpressions;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class AssignmentResultReceiverTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PlainReceiversAreIndependentValuesAndEvaluateAssignmentOnce(int optimization)
        => Run("""
            int targets = 0;
            int values = 0;
            struct Value {
                public int number;
                public Value(int input) { number = input; }
                public int Next() { number++; return number; }
                public immutable int Read() => number;
            }
            Value* Target(Value* value) { targets++; return value; }
            Value Make() { values++; return new Value(42); }
            int ReadInt(int value) => value;
            extension int { public int ReadInt() => (*this); }
            int main() {
                Value left = new(1);
                Value right = new(42);
                if ((left = right).Next() != 43 || left.number != 42) return 1;
                if ((*Target(&left) = Make()).Read() != 42 || targets != 1 || values != 1) return 2;
                Value middle = new(0);
                if ((left = middle = right).Read() != 42 || middle.number != 42) return 3;
                int first = 0;
                int second = 42;
                if ((first = second).ReadInt() != 42 || first != 42) return 4;
                int* temporary = &(first = second);
                *temporary = 99;
                if (first != 42) return 5;
                return 0;
            }
            """, optimization);

    private const string CountedOwner = """
        int copies = 0;
        int deaths = 0;
        struct Owner {
            public int id;
            public Owner(int value) { id = value; }
            public copy Owner(immutable(Owner)* source) { id = source.id; copies++; }
            ~Owner() { deaths++; }
            public immutable int Read() => id;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CustomCopiesChainsAndArrayProjectionsHaveOneCopyPerIndependentOwner(int optimization)
        => Run(CountedOwner + """
            int main() {
                {
                    Owner first = new(1);
                    Owner second = new(2);
                    Owner third = new(3);
                    if ((first = second).Read() != 2 || first.id != 2) return 1;
                    if (copies != 2 || deaths != 2) return 2;
                    first = (second = third);
                    if (first.id != 3 || second.id != 3 || third.id != 3) return 3;
                    if (copies != 4 || deaths != 4) return 4;
                }
                if (deaths != 7) return 5;
                copies = 0;
                deaths = 0;
                {
                    Owner[1] first = [new Owner(1)];
                    Owner[1] second = [new Owner(42)];
                    if ((first = second)[0].Read() != 42 || first[0].id != 42) return 6;
                    if (copies != 2 || deaths != 2) return 7;
                }
                if (deaths != 4) return 8;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopyFailurePreservesCommittedInnerAssignmentAndPrecedesLaterArguments(int optimization)
        => Run("""
            int copies = 0;
            int deaths = 0;
            int failAt = 2;
            int arguments = 0;
            int methods = 0;
            struct Owner {
                public int id;
                public Owner(int value) { id = value; }
                public copy Owner(immutable(Owner)* source) {
                    copies++;
                    if (copies == failAt) throw new Exception("copy denied", 71);
                    id = source.id;
                }
                ~Owner() { deaths++; }
                public int Read(int ignored) { methods++; return id; }
            }
            int Argument() { arguments++; return 7; }
            int main() {
                {
                    Owner first = new(1);
                    Owner second = new(2);
                    Owner third = new(3);
                    bool caught = false;
                    try { (first = second).Read(Argument()); }
                    catch (ref Exception error) {
                        if (error.GetCode() != 71) return 1;
                        caught = true;
                    }
                    if (!caught || first.id != 2 || second.id != 2 || copies != 2 || deaths != 1) return 2;
                    if (arguments != 0 || methods != 0) return 3;
                    failAt = 4;
                    caught = false;
                    try { first = (second = third); }
                    catch (ref Exception error) {
                        if (error.GetCode() != 71) return 4;
                        caught = true;
                    }
                    if (!caught || first.id != 2 || second.id != 3 || third.id != 3) return 5;
                    if (copies != 4 || deaths != 2) return 6;
                }
                if (deaths != 5) return 7;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AssignmentResultCopiesPreserveManagedFieldsAndBalanceDestructors(int optimization)
        => Run("""
            int deaths = 0;
            Owner*? assigned = null;
            struct Owner {
                public int^? value;
                public Owner(int number) { value = new^ int(number); }
                public copy Owner(readonly(Owner)* source) {
                    if (assigned != null) assigned!.value = null;
                    GC::Collect();
                    value = source.value;
                }
                ~Owner() { deaths++; }
                public int Read() {
                    GC::Collect();
                    return *value!;
                }
            }
            int main() {
                {
                    Owner target = new(1);
                    assigned = &target;
                    if ((target = new Owner(42)).Read() != 42) return 1;
                    assigned = null;
                    if (target.value != null || deaths != 2) return 2;
                }
                if (deaths != 3) return 3;
                if (GC::Collect() != (nuint)1 || std::TracingCollector::ManagedCount != (nuint)0) return 4;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ClassIdentityFieldsArraysAndPropertyWritesUseValueReceivers(int optimization)
        => Run("""
            class Base {
                public int number;
                public Base(int value) { number = value; }
                public immutable virtual int Read() => 1;
            }
            class Derived : Base {
                public int[8] tail;
                public Derived() : base(0) {}
                public immutable override int Read() => 2;
            }
            struct Cell {
                public int number;
                public Cell(int value) { number = value; }
                public immutable int Read() => number;
                public int Number { get => number; set { number = value; } }
            }
            struct Envelope {
                public Cell cell;
                public Envelope(int value) { cell = new Cell(value); }
                public int this[int index] { get => cell.number + index; set { cell.number = value; } }
            }
            int main() {
                Derived destination = new();
                Base* part = &destination;
                Base source = new(42);
                if ((*part = source).Read() != 1 || part.Read() != 2 || destination.Read() != 2) return 1;
                if (destination.number != 42) return 2;
                Envelope first = new(1);
                Envelope second = new(42);
                if ((first = second).cell.Read() != 42) return 3;
                (first = second).cell.Number = 99;
                if (first.cell.number != 42) return 4;
                (first = second)[1] = 99;
                if (first.cell.number != 42 || (first = second)[1] != 43) return 5;
                Cell[2] left = [new Cell(1), new Cell(2)];
                Cell[2] right = [new Cell(42), new Cell(43)];
                if ((left = right)[1].Read() != 43 || left[1].number != 43) return 6;
                return 0;
            }
            """, optimization);

    private const string MoveOnly = """
        struct Owner {
            public Owner() {}
            ~Owner() {}
            public int Read() => 7;
        }
        """;
    private const string PrivateCopy = """
        struct Owner {
            public Owner() {}
            private copy Owner(immutable(Owner)* source) {}
            public int Read() => 7;
        }
        """;

    [Theory]
    [InlineData(MoveOnly, "(left = move right).Read();")]
    [InlineData(PrivateCopy, "(left = new Owner()).Read();")]
    [InlineData(MoveOnly, "Owner third = new(); left = (right = move third);")]
    public void NonfreshResultsRequireCopyabilityAndCopyAccess(string declarations, string statement)
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(declarations + """
            int main() {
                Owner left = new();
                Owner right = new();
            """ + statement + "return 0;}"));
        Assert.Contains("copy", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MoveOnlyNamedDestinationStillBorrowsButAssignmentResultCannotBeRefBorrowed()
    {
        _ = CompilerTestHelper.Check(MoveOnly + """
            int main() {
                Owner left = new();
                Owner right = new();
                left = move right;
                return left.Read();
            }
            """);
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(MoveOnly + """
            void Borrow(ref Owner value) {}
            int main() {
                Owner left = new();
                Owner right = new();
                Borrow(ref (left = move right));
                return 0;
            }
            """));
        Assert.Contains("storage", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResourcefulPropertyAssignmentKeepsItsExistingVoidResult()
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("""
            struct Owner {
                public Owner() {}
                public copy Owner(immutable(Owner)* source) {}
                ~Owner() {}
                public int Read() => 7;
            }
            struct Holder {
                public Owner Value { get => new Owner(); set {} }
            }
            int main() {
                Holder holder = new();
                return (holder.Value = new Owner()).Read();
            }
            """));
        Assert.Contains("void", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private const string NullableCopy = """
        class Item {
            public int value;
            public Item(int number) { value = number; }
        }
        struct CopyOnly {
            public int number;
            private Item*?* slot;
            public CopyOnly(Item*?* storage) { slot = storage; number = 0; }
            public copy CopyOnly(immutable(CopyOnly)* source) nothrow {
                slot = (Item*?*)source.slot;
                number = source.number;
                *slot = null;
            }
            public int Read(int input) => input;
            public int this[int index] { get => index; set {} }
        }
        """;

    [Theory]
    [InlineData("return (target = move original).Read(p.value);")]
    [InlineData("return (target = move original)[p.value];")]
    [InlineData("(target = move original)[p.value] = 1; return 0;")]
    [InlineData("(target = move original).number = p.value; return 0;")]
    public void ReceiverCopyInvalidatesEscapedNullableBeforeArgumentsIndicesAndAssignedValue(string statement)
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(NullableCopy + """
            int Read(Item*? p) {
                CopyOnly original = new(&p);
                CopyOnly target = new(&p);
                if (p == null) return 0;
            """ + statement + "}"));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuiltinArrayCopyInvalidatesNullableBeforeItsIndex()
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(NullableCopy + """
            int Read(Item*? p) {
                CopyOnly[1] original = [new CopyOnly(&p)];
                CopyOnly[1] target = [new CopyOnly(&p)];
                if (p == null) return 0;
                return (target = move original)[p.value].number;
            }
            """));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ArrayIndexCanReestablishNullableFactBeforeLaterArgumentAndAssignedValue(int optimization)
        => Run(NullableCopy + """
            int Read(Item*? p) {
                Item live = new(42);
                CopyOnly[1] original = [new CopyOnly(&p)];
                CopyOnly[1] target = [new CopyOnly(&p)];
                if (p == null) return 0;
                return (target = move original)[(p = &live).reinterpret<nuint>() & (nuint)0].Read(p.value);
            }
            int Write(Item*? p) {
                Item live = new(42);
                CopyOnly[1] original = [new CopyOnly(&p)];
                CopyOnly[1] target = [new CopyOnly(&p)];
                if (p == null) return 0;
                int observed = ((target = move original)[(p = &live).reinterpret<nuint>() & (nuint)0].number = p.value);
                if (target[0].number != 0) return 1;
                return observed;
            }
            int main() {
                Item value = new(42);
                if (Read(&value) != 42 || Write(&value) != 42) return 1;
                return 0;
            }
            """, optimization);

    [Fact]
    public void ReceiverCopyExceptionPrecedesLaterArgumentDefiniteAssignment()
    {
        const string source = """
            struct Receiver {
                public Receiver() {}
                public copy Receiver(immutable(Receiver)* source) {}
                public void Run(int value) nothrow {}
            }
            int main() {
                Receiver original = new();
                Receiver target = new();
                int pending;
                try { (target = move original).Run(pending = 7); }
                catch { return pending; }
                return 0;
            }
            """;
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(source));
        Assert.Contains("unassigned", error.Message, StringComparison.OrdinalIgnoreCase);
        _ = CompilerTestHelper.Check(source.Replace(
            "public copy Receiver(immutable(Receiver)* source) {}",
            "public copy Receiver(immutable(Receiver)* source) nothrow {}"));
    }

    [Fact]
    public void NothrowResultCopiesUseExistingDirectCallContract()
    {
        const string source = """
            struct Value {
                public int number;
                public copy Value(immutable(Value)* source) nothrow { number = source.number; }
                public immutable int Read() nothrow => number;
            }
            int Observe(Value* target, Value* source) nothrow => (*target = *source).Read();
            """;
        string ir = Compiler.Emit([new SourceFile("nothrow-assignment-result.gf", source)], options: new() { NoStdLib = true });
        string body = Regex.Match(ir, @"(?ms)^define [^\r\n]*@gflat\$Observe\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);
        Assert.DoesNotContain("invoke ", body);
        Assert.DoesNotContain("__gflat_eh_", body);
    }

    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("assignment-result-receiver.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

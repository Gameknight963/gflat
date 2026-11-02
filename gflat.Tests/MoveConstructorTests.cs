namespace gflat.Tests;

public sealed class MoveConstructorTests
{
    private const string Sample = """
        struct Sample {
            private int value;
            private int* address;
            public Sample(int initial) nothrow { value = initial; address = &value; }
            public move Sample(Sample* source) nothrow { value = source.value; address = &value; }
            public bool Valid() nothrow => address == &value && *address == value;
            public int Value { get nothrow => value; }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallsAndReturnsRelocateValuesIntoTheirFinalStorage(int optimization)
    {
        var result = CompilerTestHelper.Run(Sample + """
            Sample Create() { Sample result = new(42); return result; }
            Sample Forward(Sample input) {
                if (!input.Valid()) return new Sample(-1);
                return input;
            }
            bool Consume(Sample input) => input.Valid() && input.Value == 42;
            int main() {
                Sample value = Forward(Create());
                if (!value.Valid() || value.Value != 42) return 1;
                if (!Consume(move value)) return 2;
                Sample(Sample)* forward = &Forward;
                Sample another = forward(Create());
                if (!another.Valid() || another.Value != 42) return 3;
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstructionLocalTransferAndPlacementRelocationRepairSelfPointers(int optimization)
    {
        var result = CompilerTestHelper.Run(Sample + """
            int main() {
                Sample first = new(42);
                if (!first.Valid()) return 1;
                Sample second = move first;
                if (!second.Valid() || second.Value != 42) return 2;
                Sample* storage = (Sample*)Memory::Allocate((nuint)sizeof(Sample))!;
                Sample* placed = move second at storage;
                bool valid = placed.Valid() && placed.Value == 42;
                placed.~Sample();
                Memory::Free((void*)placed);
                if (!valid) return 3;
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("public move Sample(readonly(Sample)* source) nothrow {}", "mutable")]
    [InlineData("public move Sample(Sample*? source) nothrow {}", "non-null")]
    [InlineData("public move Sample(Sample* source) {}", "nothrow")]
    [InlineData("public move Sample(int source) nothrow {}", "own type")]
    public void InvalidMoveConstructorContractsAreRejected(string declaration, string diagnostic)
    {
        var error = Assert.Throws<CompileExceptions.TypeCheckException>(() => Compiler.Check("struct Sample { " + declaration + " }"));
        Assert.Contains(diagnostic, error.Message);
    }

    [Fact]
    public void OrdinaryConstructionDoesNotSelectTheMoveConstructor()
    {
        Assert.Throws<CompileExceptions.TypeCheckException>(() => Compiler.Check(Sample + """
            int main() { Sample value = new(42); Sample other = new(&value); return 0; }
            """));
        Compiler.Check("""
            struct Item {
                public Item(Item* source) {}
                public move Item(Item* source) nothrow {}
            }
            """);
        var error = Assert.Throws<CompileExceptions.TypeCheckException>(() => Compiler.Check("""
            class Base {
                public Base(int value) {}
                public move Base(Base* source) nothrow {}
            }
            class Derived : Base {
                public Derived(Base* source) : base(source) {}
            }
            """));
        Assert.Contains("No matching base constructor", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SynthesizedRelocationVisitsInlineFieldsAndArrayElements(int optimization)
    {
        var result = CompilerTestHelper.Run(Sample + """
            struct Pair {
                public Sample first;
                public Sample second;
                public Pair() { first = new(10); second = new(20); }
                public bool Valid() => first.Valid() && second.Valid();
            }
            int main() {
                Pair original = new();
                Pair relocated = move original;
                if (!relocated.Valid()) return 1;
                Sample[2] items = [new Sample(1), new Sample(2)];
                if (!items[0].Valid() || !items[1].Valid()) return 2;
                Sample[2] moved = move items;
                if (!moved[0].Valid() || !moved[1].Valid()) return 3;
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ClassRelocationMovesBaseAndSkipsOrdinaryInitializers(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            class Base {
                private int value;
                private int* address;
                public Base(int initial) { value = initial; address = &value; }
                public move Base(Base* source) nothrow { value = source.value; address = &value; }
                public bool Valid() => address == &value && value == 42;
            }
            class Derived : Base {
                private int extra = Initialize();
                private static int runs;
                public static int Runs => runs;
                private static int Initialize() { runs++; return 7; }
                public Derived(int value) : base(value) { extra = 7; }
                public move Derived(Derived* source) nothrow { extra = source.extra; }
            }
            int main() {
                Derived original = new(42);
                Derived relocated = move original;
                if (!relocated.Valid() || Derived::Runs != 1) return 1;
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MovingAnOwnerDestroysOnlyTheFinalObjectAndCopiesStayDistinct(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            struct Resource {
                private int* data;
                private int* destroyed;
                private int* address;
                public Resource(int* count) { data = new* int(42); destroyed = count; address = &*data; }
                public copy Resource(immutable(Resource)* source) {
                    data = new* int(*source.data); destroyed = (int*)source.destroyed; address = &*data;
                }
                public move Resource(Resource* source) nothrow {
                    data = source.data; destroyed = source.destroyed; address = &*data;
                }
                public int* Data => data;
                ~Resource() { (*destroyed)++; delete data; }
            }
            Resource Forward(Resource item) { return item; }
            int main() {
                int destroyed = 0;
                {
                    Resource original = new(&destroyed);
                    Resource copied = original;
                    if (original.Data == copied.Data) return 1;
                    Resource moved = Forward(move original);
                    if (*moved.Data != 42 || destroyed != 0) return 2;
                }
                if (destroyed != 2) return 3;
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void VectorGrowthShiftsAndReturnsHonorElementRelocation(int optimization)
    {
        string[] names = ["Collections/IEnumerable.gf", "Queries/Enumerable.gf", "IStringConvertable.gf", "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "Text/String.gf", "Text/ByteSearch.gf", "Collections/Vector.gf", "Memory/Span.gf"];
        var sources = names.Select(name => new SourceFile(name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", name))))
            .Append(new SourceFile("main.gf", """
                using Standard::Collections;

                """ + Sample + """
                int main() {
                    Vector<Sample> values = new();
                    for (int i = 0; i < 12; i++) values.Add(new Sample(i));
                    values.Insert(0, new Sample(42));
                    for (nuint i = 0; i < values.Count; i++)
                        if (!values[i].Valid()) return 1;
                    Sample removed = values.RemoveAt(0);
                    if (!removed.Valid() || removed.Value != 42) return 2;
                    Sample last = values.Pop();
                    if (!last.Valid() || last.Value != 11) return 3;
                    for (nuint i = 0; i < values.Count; i++)
                        if (!values[i].Valid()) return 4;
                    return 0;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.Equal(0, result.ExitCode);
    }
}

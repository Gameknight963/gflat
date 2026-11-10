namespace gflat.Tests;

public sealed class NativeValueTests
{
    private const string MoveOnly = """
        #lang cpp
        class Token {
            int value;
        public:
            explicit Token(int input) noexcept : value(input) {}
            Token(const Token&) = delete;
            Token(Token&& source) noexcept : value(source.value) {}
            ~Token() noexcept {}
            int Read() const noexcept { return value; }
        };
        #lang gflat
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeletedNativeCopiesDoNotPreventConsumingMoves(int optimization)
        => Run(MoveOnly + "\nint main() { Token first = new Token(42); Token second = move first; return second.Read(); }", optimization);

    [Fact]
    public void DeletedNativeCopiesAreRejectedByTheHostChecker()
    {
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Check(MoveOnly +
            "\nint main() { Token first = new Token(42); Token second = first; return 0; }", options: Options()));
        Assert.Contains("copy", error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrivateDestructionDoesNotBlockNativeAllocationThroughAPublicConstructor(int optimization)
    {
        Run("""
            #lang cpp
            int live = 0;
            class Cell {
                ~Cell() noexcept { --live; }
            public:
                Cell() noexcept { ++live; }
                void Release() noexcept { delete this; }
            };
            int Live() noexcept { return live; }
            #lang gflat
            int main() {
                Cell* cell = new* Cell();
                if (Live() != 1) return 1;
                cell.Release();
                if (Live() != 0) return 2;
                return 42;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeletedNativeAllocationDoesNotBlockConstructionOfAHostLocal(int optimization)
    {
        Run("""
            #lang cpp
            class Cell {
                static void* operator new(__SIZE_TYPE__) = delete;
                int value;
            public:
                Cell() noexcept : value(42) {}
                int Read() const noexcept { return value; }
            };
            #lang gflat
            int main() { Cell cell = new Cell(); return cell.Read(); }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeTagsAndNamespaceLocalConstructorParametersKeepTheirLookupContext(int optimization)
    {
        Run("""
            #lang cpp
            namespace Native {
                class Cell {
                    int value;
                public:
                    explicit Cell(int input) noexcept : value(input) {}
                    Cell(const Cell& source) noexcept : value(source.value) {}
                    ~Cell() noexcept {}
                    int Read() const noexcept { return value; }
                };
                int Cell() noexcept { return 0; }
            }
            #lang gflat
            int main() {
                Native::Cell first = new Native::Cell(42);
                Native::Cell second = first;
                Native::Cell third = move second;
                return third.Read();
            }
            """, optimization);
    }

    [Theory]
    [InlineData("Cell(Cell&&) noexcept(false) {} ~Cell() noexcept {}")]
    [InlineData("Cell(Cell&&) noexcept {} ~Cell() noexcept(false) {}")]
    [InlineData("private: Cell(Cell&&) noexcept {} public: ~Cell() noexcept {}")]
    [InlineData("Cell(Cell&&) noexcept {} private: ~Cell() noexcept {}")]
    public void UnavailableOrThrowingRelocationAndDestructionRemainPointerOnly(string operations)
    {
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Check(
            "#lang cpp\nclass Cell { int value; public: Cell(int) noexcept {} " + operations +
            " };\n#lang gflat\nint main() { Cell value = new Cell(42); return 0; }", options: Options()));
        Assert.Contains("pointer-only", error.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PlacementConstructionAndExplicitDestructionUseNativeLifetimeOperations(int optimization)
    {
        Run("""
            #lang cpp
            int live = 0;
            class alignas(32) Cell {
                int value;
            public:
                explicit Cell(int input) noexcept : value(input) { ++live; }
                Cell(Cell&& other) noexcept : value(other.value) { ++live; }
                ~Cell() noexcept { --live; }
                int Read() const noexcept { return value; }
            };
            Cell* Storage() noexcept { alignas(Cell) static unsigned char bytes[sizeof(Cell)]; return reinterpret_cast<Cell*>(bytes); }
            int Live() noexcept { return live; }
            #lang gflat
            int main() {
                Cell* storage = Storage()!;
                Cell* first = new* Cell(41) at storage;
                if (first.Read() != 41 || Live() != 1) return 1;
                first.~Cell();
                if (Live() != 0) return 2;
                Cell* second = new* Cell(42) at storage;
                int result = second.Read();
                second.~Cell();
                if (Live() != 0) return 3;
                return result;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeMembersOfHostValuesCopyRelocateAndDestroyRecursively(int optimization)
    {
        Run("""
            #lang cpp
            int live = 0;
            class Cell {
                int value;
                const int* self;
            public:
                explicit Cell(int input) noexcept : value(input), self(&value) { ++live; }
                Cell(const Cell& source) noexcept : value(source.value), self(&value) { ++live; }
                Cell(Cell&& source) noexcept : value(source.value), self(&value) { ++live; }
                ~Cell() noexcept { --live; }
                int Read() const noexcept { return self == &value ? value : -999; }
            };
            int Live() noexcept { return live; }
            #lang gflat
            struct Box {
                public Cell Value;
                public Box(int input) { Value = new Cell(input); }
            }
            Box Make() => new Box(42);
            int main() {
                {
                    Box first = Make();
                    Box second = first;
                    Box third = move second;
                    if (first.Value.Read() != 42 || third.Value.Read() != 42 || Live() != 2) return 1;
                }
                if (Live() != 0) return 2;
                return 42;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedNativeConstructionAndCopyLeaveExistingOwnersIntact(int optimization)
    {
        Run("""
            #lang cpp
            int live = 0;
            bool fail_copy = false;
            class Item {
                int value;
            public:
                explicit Item(int input) : value(input) { if (input < 0) throw 71; ++live; }
                Item(const Item& source) : value(source.value) { if (fail_copy) throw 72; ++live; }
                Item(Item&& source) noexcept : value(source.value) { ++live; }
                ~Item() noexcept { --live; }
                int Read() const noexcept { return value; }
            };
            void FailCopies(bool enabled) noexcept { fail_copy = enabled; }
            int Live() noexcept { return live; }
            #lang gflat
            int main() {
                {
                    Item first = new Item(42);
                    bool caught = false;
                    try { Item failed = new Item(-1); } catch { caught = true; }
                    if (!caught || Live() != 1) return 1;
                    FailCopies(true);
                    caught = false;
                    try { Item failed = first; } catch { caught = true; }
                    if (!caught || Live() != 1 || first.Read() != 42) return 2;
                }
                if (Live() != 0) return 3;
                return 42;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LocalCopiesMovesAndReturnsPreserveNativeAddressesAndReleaseEveryOwner(int optimization)
    {
        const string code = """
            #lang cpp
            int live = 0;
            class alignas(32) Owner {
                int value;
                const int* self;
            public:
                explicit Owner(int input) noexcept : value(input), self(&value) { ++live; }
                Owner(const Owner& source) noexcept : value(source.value), self(&value) { ++live; }
                Owner(Owner&& source) noexcept : value(source.value), self(&value) { ++live; }
                ~Owner() noexcept { --live; }
                int Read() const noexcept { return self == &value ? value : -999; }
                void Set(int input) noexcept { value = input; }
            };
            int Live() noexcept { return live; }
            #lang gflat
            Owner Make(int input) => new Owner(input);
            int main() {
                {
                    Owner first = new Owner(40);
                    if ((&first).reinterpret<nuint>() % (nuint)32 != (nuint)0) return 1;
                    Owner second = first;
                    second.Set(41);
                    if (first.Read() != 40 || second.Read() != 41) return 2;
                    Owner third = move second;
                    if (third.Read() != 41) return 3;
                    Owner fourth = Make(42);
                    if (fourth.Read() != 42 || Live() != 3) return 4;
                }
                if (Live() != 0) return 5;
                return 42;
            }
            """;
        Run(code, optimization);
    }

    private static void Run(string code, int optimization)
    {
        var program = Compiler.EmitProgram([new SourceFile("native-values.gf", code)], options: Options());
        var result = CompilerTestHelper.RunIr(program.Ir, optimization, foreignUnits: program.ForeignUnits);
        Assert.True(result.ExitCode == 42, $"Native value exit {result.ExitCode}: {result.StandardError}");
    }

    private static CompilationOptions Options() => new() {
            NoStdLib = true, InternalizeFunctions = true,
            ForeignCompilerResolver = () => NativeToolchain.FindClang(),
            ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
        };
}

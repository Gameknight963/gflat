using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NativeReferenceResultTests
{
    private const string Native = """
        #lang cpp
        namespace Native {
            int value = 40;
            int* pointer = &value;
            int& Value() noexcept { return value; }
            const int& ReadOnly() noexcept { return value; }
            int*& Pointer() noexcept { return pointer; }
            class Cell {
                int value;
            public:
                explicit Cell(int input) noexcept : value(input) {}
                Cell(const Cell&) = delete;
                Cell(Cell&&) noexcept = default;
                ~Cell() noexcept {}
                int& Value() noexcept { return value; }
                const int& ReadOnly() const noexcept { return value; }
                int Read() const noexcept { return value; }
            };
            Cell& Borrow(Cell* object) noexcept { return *object; }
            const Cell& BorrowReadOnly(const Cell* object) noexcept { return *object; }
        }
        #lang gflat
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ScalarPointerAndMemberReferenceResultsReferToOriginalNativeStorage(int optimization)
        => Run(Native + """

            void Increment(ref int value) { value++; }
            int main()
            {
                Native::Value() = 41;
                Increment(ref Native::Value());
                if (Native::ReadOnly() != 42) return 1;
                int* address = &Native::Value();
                Native::Pointer() = address;
                if (Native::Pointer() != address || *Native::Pointer()! != 42) return 2;
                Native::Cell* cell = new* Native::Cell(40);
                Native::Borrow(cell).Value() = 41;
                Increment(ref Native::Borrow(cell).Value());
                if (&Native::Borrow(cell) != cell || Native::BorrowReadOnly(cell).ReadOnly() != 42) return 3;
                delete cell;
                return 42;
            }
            """, optimization);

    [Fact]
    public void OrdinaryValueReturningCallsDoNotBecomeAssignmentTargets()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "int Value() => 41; int main() { Value() = 42; return 0; }", options: new() { NoStdLib = true }));
        Assert.Contains("not assignable storage", error.Description);
    }

    [Theory]
    [InlineData("Native::ReadOnly() = 1;", "readonly")]
    [InlineData("Increment(ref Native::ReadOnly());", "readonly")]
    [InlineData("Native::BorrowReadOnly(cell).Value() = 1;", "readonly")]
    [InlineData("Native::Cell copy = Native::Borrow(cell);", "copy")]
    public void ConstReferencesAndDeletedCopiesAreEnforced(string statement, string diagnostic)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Native +
            "\nvoid Increment(ref int value) { value++; }\nint main() { Native::Cell* cell = new* Native::Cell(42); " +
            statement + " delete cell; return 0; }", options: Options()));
        Assert.Contains(diagnostic, error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopyingABorrowedOwnerRunsTheNativeCopyAndLeavesTheOriginalAlive(int optimization)
        => Run("""
            #lang cpp
            int live = 0;
            int copies = 0;
            class Cell {
                int value;
            public:
                explicit Cell(int input) noexcept : value(input) { ++live; }
                Cell(const Cell& source) noexcept : value(source.value) { ++copies; ++live; }
                Cell(Cell&& source) noexcept : value(source.value) { ++live; }
                ~Cell() noexcept { --live; }
                void Set(int input) noexcept { value = input; }
                int Read() const noexcept { return value; }
            };
            Cell& Borrow(Cell* source) noexcept { return *source; }
            int Live() noexcept { return live; }
            int Copies() noexcept { return copies; }
            #lang gflat
            int main()
            {
                Cell* original = new* Cell(41);
                {
                    Cell copy = Borrow(original);
                    copy.Set(42);
                    if (Live() != 2 || Copies() != 1 || original.Read() != 41 || copy.Read() != 42) return 1;
                }
                if (Live() != 1 || original.Read() != 41) return 2;
                delete original;
                if (Live() != 0) return 3;
                return 42;
            }
            """, optimization);

    private static CompilationOptions Options() => new()
    {
        NoStdLib = true,
        InternalizeFunctions = true,
        ForeignCompilerResolver = () => NativeToolchain.FindClang(),
        ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true)
    };

    private static void Run(string code, int optimization)
    {
        var program = Compiler.EmitProgram([new SourceFile("native-references.gf", code)], options: Options());
        var result = CompilerTestHelper.RunIr(program.Ir, optimization, foreignUnits: program.ForeignUnits);
        Assert.True(result.ExitCode == 42, $"Native reference exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConstExtensionBorrowTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LiteralAndEnumReceiversCanBeBorrowedDuringConstantEvaluation(int optimization)
        => Assert.Equal(42, CompilerTestHelper.Run("""
            enum Code { Answer = 40 }
            extension int { public const readonly int Plus(int amount = 2) => *this + amount; }
            extension Code { public const readonly int Read() => (int)*this; }
            const int literal = (40).Plus();
            const int enumeration = Code::Answer.Read().Plus();
            int main() { if (literal != enumeration) return 1; return literal; }
            """, optimization).ExitCode);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ScalarLocalsAndPointerAliasesRetainOneBorrowedStorageCell(int optimization)
        => Assert.Equal(42, CompilerTestHelper.Run("""
            extension int { public const int Add(int amount) { *this += amount; return *this; } }
            const int Fold() {
                int value = 39;
                int* storage = &value;
                value.Add(1);
                *storage += 1;
                storage.Add(1);
                return value;
            }
            const int answer = Fold();
            int main() => answer;
            """, optimization).ExitCode);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ScalarFieldsAndArrayElementsAreBorrowedWithoutCopies(int optimization)
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct Box {
                public int value;
                public const Box(int input) { value = input; }
                public const void Advance() { value.Add(1); }
            }
            extension int { public const void Add(int amount) { *this += amount; } }
            const int Fold() {
                Box box = new Box(40);
                int[1] values = [40];
                box.Advance();
                box.value.Add(1);
                values[0].Add(1);
                return box.value + values[0] - 41;
            }
            const int answer = Fold();
            int main() => answer;
            """, optimization).ExitCode);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FieldReceiverIsCapturedOnceBeforeLaterArgumentsChangeItsPointer(int optimization)
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct Box { public int value; public const Box(int input) { value = input; } }
            struct State {
                public int calls;
                public Box* current;
                private Box* replacement;
                public const State(Box* first, Box* second) { calls = 0; current = first; replacement = second; }
                public const Box* Select() { calls++; return current; }
                public const int Change() { current = replacement; return 2; }
            }
            extension int { public const void Add(int amount) { *this += amount; } }
            const int Fold() {
                Box first = new Box(40); Box second = new Box(20);
                State state = new State(&first, &second);
                state.Select().value.Add(state.Change());
                if (second.value != 20) return 0;
                return state.calls * 100 + first.value;
            }
            const int answer = Fold();
            int main() => answer - 100;
            """, optimization).ExitCode);

    [Theory]
    [InlineData("const int* Address() { int value = 42; return &value; } const int* escaped = Address();")]
    [InlineData("struct Holder { public int* value; public const Holder(int* input) { value = input; } } const Holder Address() { int value = 42; return new Holder(&value); } const Holder escaped = Address();")]
    public void TransientScalarAddressesCannotEscapeInsideEmittedConstants(string source)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source + " int main() => 0;"));
        Assert.Contains("borrowed compile-time address cannot escape", error.Message);
    }
}

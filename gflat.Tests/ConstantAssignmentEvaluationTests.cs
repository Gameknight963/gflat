namespace gflat.Tests;

public sealed class ConstantAssignmentEvaluationTests
{
    [Theory]
    [InlineData("+=", 11)]
    [InlineData("-=", -9)]
    [InlineData("*=", 10)]
    public void CompoundConstantAssignmentReadsTheOldValueBeforeTheRightOperand(string operation, int expected)
        => Assert.Equal(0, CompilerTestHelper.Run($$"""
            const int Fold() { int value = 1; value {{operation}} (value = 10); return value; }
            const int folded = Fold();
            int main() {
                int value = 1; value {{operation}} (value = 10);
                if (value != {{expected}} || folded != value) return 1;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void CompoundConstantFieldAssignmentCapturesTheOldFieldValue()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Box { public int value; public const Box(int input) { value = input; } }
            const int Fold() { Box box = new Box(1); box.value += (box.value = 10); return box.value; }
            const int folded = Fold();
            int main() { Box box = new Box(1); box.value += (box.value = 10); if (folded == box.value && folded == 11) return 0; return 1; }
            """).ExitCode);

    [Fact]
    public void ConstantAssignmentCapturesPointerReceiverBeforeRightOperandReplacesIt()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Box { public int value; public const Box(int input) { value = input; } }
            struct State {
                public Box* current;
                private Box* replacement;
                public const State(Box* first, Box* second) { current = first; replacement = second; }
                public const int Change() { current = replacement; return 42; }
            }
            const int Fold() {
                Box first = new Box(1); Box second = new Box(2);
                State state = new State(&first, &second);
                state.current.value = state.Change();
                return first.value;
            }
            const int folded = Fold();
            int main() {
                Box first = new Box(1); Box second = new Box(2);
                State state = new State(&first, &second);
                state.current.value = state.Change();
                if (first.value != 42 || second.value != 2 || folded != first.value) return 1;
                return 0;
            }
            """).ExitCode);

    [Theory]
    [InlineData("=", 142)]
    [InlineData("+=", 143)]
    public void ConstantFieldReceiverCallsAreEvaluatedExactlyOnce(string operation, int expected)
        => Assert.Equal(0, CompilerTestHelper.Run($$"""
            struct Box { public int value; public const Box(int input) { value = input; } }
            struct State {
                public int calls;
                public Box* current;
                private Box* replacement;
                public const State(Box* first, Box* second) { calls = 0; current = first; replacement = second; }
                public const Box* Select() { calls += 1; return current; }
                public const int Change() { current = replacement; return 42; }
            }
            const int Fold() {
                Box first = new Box(1); Box second = new Box(2);
                State state = new State(&first, &second);
                state.Select().value {{operation}} state.Change();
                return state.calls * 100 + first.value;
            }
            const int folded = Fold();
            int main() {
                Box first = new Box(1); Box second = new Box(2);
                State state = new State(&first, &second);
                state.Select().value {{operation}} state.Change();
                int actual = state.calls * 100 + first.value;
                if (actual != {{expected}} || folded != actual || second.value != 2) return 1;
                return 0;
            }
            """).ExitCode);
}

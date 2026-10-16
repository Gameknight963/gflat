namespace gflat.Tests;

public sealed class ConstantUpdateEvaluationTests
{
    [Theory]
    [InlineData("value++", 708)]
    [InlineData("++value", 808)]
    [InlineData("value--", 706)]
    [InlineData("--value", 606)]
    public void ImplicitInstanceFieldUpdatesMatchRuntime(string update, int expected)
        => Assert.Equal(0, CompilerTestHelper.Run($$"""
            struct Counter {
                public int value;
                public const Counter() { value = 7; }
                public const int Update() { return {{update}}; }
            }
            const int Fold() { Counter value = new Counter(); int old = value.Update(); return old * 100 + value.value; }
            const int folded = Fold();
            int main() {
                Counter value = new Counter(); int old = value.Update();
                if (folded != {{expected}} || folded != old * 100 + value.value) return 1;
                return 0;
            }
            """).ExitCode);

    [Theory]
    [InlineData("pointer.value++", 708)]
    [InlineData("--pointer.value", 606)]
    public void ExplicitPointerFieldUpdatesMatchRuntime(string update, int expected)
        => Assert.Equal(0, CompilerTestHelper.Run($$"""
            class Counter { public int value; public const Counter() { value = 7; } }
            const int Fold() { Counter value = new Counter(); Counter* pointer = &value; int old = {{update}}; return old * 100 + value.value; }
            const int folded = Fold();
            int main() {
                Counter value = new Counter(); Counter* pointer = &value; int old = {{update}};
                if (folded != {{expected}} || folded != old * 100 + value.value) return 1;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void UpdatingACalledReceiverEvaluatesItOnce()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Counter { public int value; public const Counter() { value = 7; } }
            struct State {
                public int calls;
                private Counter* pointer;
                public const State(Counter* target) { calls = 0; pointer = target; }
                public const Counter* Select() { calls += 1; return pointer; }
            }
            const int Fold() {
                Counter value = new Counter(); State state = new State(&value);
                int old = state.Select().value++;
                return state.calls * 1000 + old * 100 + value.value;
            }
            const int folded = Fold();
            int main() {
                Counter value = new Counter(); State state = new State(&value);
                int old = state.Select().value++;
                if (folded != 1708 || folded != state.calls * 1000 + old * 100 + value.value) return 1;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void FieldUpdatesNormalizeTheStoredIntegerWidth()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Counter {
                public byte value;
                public const Counter() { value = 255; }
                public const int Update() => (int)value++;
            }
            const int Fold() { Counter value = new Counter(); int old = value.Update(); return old * 100 + (int)value.value; }
            const int folded = Fold();
            int main() {
                Counter value = new Counter(); int old = value.Update();
                if (folded != 25500 || folded != old * 100 + (int)value.value) return 1;
                return 0;
            }
            """).ExitCode);
}

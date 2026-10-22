using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ForExpressionInitializerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AssignmentAndCallInitializersRunOnceAndPreserveOuterVariables(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            int calls = 0;
            void Initialize(ref int value) { calls++; value = 1; }
            int main() {
                int index = 99; int sum = 0;
                for (index = 0; index < 5; index++) {
                    if (index == 2) continue;
                    sum += index;
                }
                if (index != 5 || sum != 8) return 1;
                for (Initialize(ref index); index < 4; index++) sum += index;
                if (calls != 1 || index != 4 || sum != 14) return 2;
                for (index = 12; false; index++) return 3;
                if (index != 12) return 4;
                for (index = 0;;) { index++; break; }
                if (index != 1) return 5;
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DiscardedOwningInitializerIsDestroyedBeforeConditionAndThrowsUnwind(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            int destroyed = 0; bool fail = false;
            struct Owner { ~Owner() { destroyed++; } }
            Owner Create() {
                Owner item = new();
                if (fail) throw new Exception("initializer failed");
                return move item;
            }
            bool Condition() {
                if (destroyed != 1) throw new Exception("initializer cleanup delayed");
                return false;
            }
            int main() {
                for (Create(); Condition();) return 1;
                if (destroyed != 1) return 2;
                fail = true; bool caught = false;
                try { for (Create(); true;) return 3; }
                catch (Exception* error) { caught = true; }
                if (!caught || destroyed != 2) return 4;
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void DeclarationInitializersStillHaveLoopScope()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            int main() { for (int local = 0; local < 1; local++) {} return local; }
            """));
        Assert.Contains("local", error.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DiscardedOwningIncrementIsDestroyedBeforeNextCondition(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            int destroyed = 0; int constructed = 0;
            struct Owner {
                public Owner() { constructed++; }
                ~Owner() { destroyed++; }
            }
            Owner Advance(ref int index) { index++; return new Owner(); }
            bool More(int index) {
                if (destroyed != constructed) throw new Exception("increment cleanup delayed");
                return index < 5;
            }
            int main() {
                int index = 0;
                for (; More(index); Advance(ref index)) {
                    if (index == 2) continue;
                }
                if (index != 5 || constructed != 5 || destroyed != 5) return 1;
                for (; true; Advance(ref index)) break;
                if (constructed != 5 || destroyed != 5) return 2;
                return 0;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }
}

namespace gflat.Tests;

public class TemporaryLifetimeTests
{
    private const string Prelude = """
        extern int printf(readonly char* format, ...);
        struct Item {
            public int id;
            public Item(int value) { id = value; printf("N%d;", id); }
            public ~Item() { printf("D%d;", id); }
            public int Get() { printf("G%d;", id); return id; }
            public Item Next(int value) { printf("S%d;", id); return new Item(value); }
            public int Fail() throws { printf("T%d;", id); throw new* Exception(); }
            public int With(int value) { printf("W%d;", id); return value; }
            public bool Test() { printf("B%d;", id); return true; }
        }
        Item make(int id) { return new Item(id); }
        int fail() throws { printf("F;"); throw new* Exception(); }
        int combine(int a, int b) { printf("C;"); return a + b; }
        void consume(bool value) { printf("C;"); }
        void consumeWith(bool value, int other) { printf("U;"); }
        """;

    [Theory]
    [InlineData("make(1).Next(2).Get();", "N1;S1;N2;G2;D2;D1;E;")]
    [InlineData("combine(make(1).Get(), make(2).Get());", "N1;G1;N2;G2;C;D2;D1;E;")]
    [InlineData("make(1).With(make(2).Get());", "N1;N2;G2;W1;D2;D1;E;")]
    public void NestedCallsDestroyEachTemporaryAfterItsUse(string expression, string expected)
        => Check("int main() { " + expression + " printf(\"E;\"); return 0; }", expected);

    [Theory]
    [InlineData("make(1).Next(2).Fail();", "N1;S1;N2;T2;D2;D1;C;E;")]
    [InlineData("make(1).With(fail());", "N1;F;D1;C;E;")]
    [InlineData("combine(make(1).Get(), fail());", "N1;G1;F;D1;C;E;")]
    [InlineData("make(1).With(make(2).Fail());", "N1;N2;T2;D2;D1;C;E;")]
    public void ExceptionCleansOnlyCompletedTemporaries(string expression, string expected)
        => Check("int main() { try { " + expression + " } catch { printf(\"C;\"); } printf(\"E;\"); return 0; }", expected);

    [Fact]
    public void ReturnedResultOutlivesTemporaryReceiver()
        => Check("int main() { { Item result = make(1).Next(2); printf(\"R;\"); result.Get(); } printf(\"E;\"); return 0; }", "N1;S1;N2;D1;R;G2;D2;E;");

    [Fact]
    public void ThrowingFactoryDoesNotCreatePhantomTemporary()
        => Check("Item bad() throws { Item item = make(1); fail(); return item; } int main() { try { bad().Get(); } catch { printf(\"C;\"); } return 0; }", "N1;F;D1;C;");

    [Theory]
    [InlineData("false && make(1).Test()")]
    [InlineData("true || make(1).Test()")]
    public void SkippedOperandDoesNotConstructOrDestroyTemporary(string expression)
        => Check("int main() { consume(" + expression + "); printf(\"E;\"); return 0; }", "C;E;");

    [Theory]
    [InlineData("true && make(1).Test()", "N1;B1;C;D1;E;")]
    [InlineData("false || make(1).Test()", "N1;B1;C;D1;E;")]
    [InlineData("make(1).Test() || make(2).Test()", "N1;B1;C;D1;E;")]
    [InlineData("make(1).Test() && make(2).Test()", "N1;B1;N2;B2;C;D2;D1;E;")]
    public void EvaluatedLogicalOperandsCleanUpAfterEnclosingCall(string expression, string expected)
        => Check("int main() { consume(" + expression + "); printf(\"E;\"); return 0; }", expected);

    [Theory]
    [InlineData("consume(true && (make(1).Fail() == 1));", "N1;T1;D1;C;")]
    [InlineData("consumeWith(false && make(1).Test(), fail());", "F;C;")]
    [InlineData("consumeWith(true && make(1).Test(), fail());", "N1;B1;F;D1;C;")]
    public void ConditionalTemporariesCleanUpOnLaterException(string expression, string expected)
        => Check("int main() { try { " + expression + " } catch { printf(\"C;\"); } return 0; }", expected);

    [Fact]
    public void ConditionalCleanupResetsEachLoopIteration()
        => Check("int main() { for (int i = 0; i < 2; i++) { consume(i == 0 && make(i).Test()); } printf(\"E;\"); return 0; }", "N0;B0;C;D0;C;E;");

    private static void Check(string source, string expected)
    {
        var result = CompilerTestHelper.Run(Prelude + source);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput);
    }
}

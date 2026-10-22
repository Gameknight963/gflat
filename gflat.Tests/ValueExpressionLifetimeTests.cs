using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ValueExpressionLifetimeTests
{
    [Fact]
    public void CastsCannotProduceIncompleteArrayValues()
        => Assert.Contains("size", Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(
            "int main() { int[2] values = [1, 2]; (int[])values; return 0; }")).Message, StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("default(Value).number")]
    [InlineData("default(Value).Read()")]
    [InlineData("default(Value).Number")]
    [InlineData("((Value)new Value()).number")]
    [InlineData("((Value)new Value()).Read()")]
    [InlineData("((Value)new Value()).Number")]
    public void DefaultAndCastValuesSupportMemberUse(string expression)
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Value { public int number; public int Number => number; public int Read() => number; }
            int main() { return EXPRESSION; }
            """.Replace("EXPRESSION", expression)).ExitCode);

    [Theory]
    [InlineData("default(int[2])[1]", 0)]
    [InlineData("((Buffer)data)[1]", 22)]
    public void DefaultAndCastBufferValuesSupportIndexing(string expression, int expected)
        => Assert.Equal(expected, CompilerTestHelper.Run("alias Buffer = int[2]; int main() { Buffer data = [20, 22]; return " + expression + "; }").ExitCode);

    [Theory]
    [InlineData("struct Owner { ~Owner() {} }")]
    [InlineData("struct Owner { public Owner() {} private copy Owner(immutable(Owner)* source) {} ~Owner() {} }")]
    public void CastsCannotBypassAnOwningValuesCopyContract(string declaration)
        => Assert.Contains("copy", Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(declaration + " int main() { Owner value = new Owner(); (Owner)value; return 0; }")).Message, StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("(Owner)source;", "CDD")]
    [InlineData("Owner copy = (Owner)source;", "CDD")]
    [InlineData("int read = ((Owner)source).Read();", "CDD")]
    [InlineData("(Owner)new Owner(42);", "DD")]
    [InlineData("(Owner)move source;", "D")]
    public void OwningCastsCreateAndDestroyExactlyOneResult(string statement, string output)
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            struct Owner {
                private int number;
                public Owner(int value) { number = value; }
                public copy Owner(immutable(Owner)* source) { putchar(67); number = source.number; }
                public int Read() => number;
                ~Owner() { putchar(68); }
            }
            int main() { Owner source = new Owner(42); STATEMENT return 0; }
            """.Replace("STATEMENT", statement));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(output, result.StandardOutput);
    }

    [Fact]
    public void OwningDefaultTemporaryIsDestroyedAfterMemberUse()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            struct Owner { public int number; ~Owner() { putchar(68); } }
            int main() { return default(Owner).number; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("D", result.StandardOutput);
    }

    [Theory]
    [InlineData("default(Owners)[0].number", "DDDD")]
    [InlineData("((Owners)values)[0].number", "CCDDDD")]
    public void OwningBufferValueTemporariesDestroyAllElements(string expression, string output)
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            struct Owner {
                public int number;
                COPY_MEMBERS
                ~Owner() { putchar(68); }
            }
            alias Owners = Owner[2];
            int main() { Owners values = [new Owner(), new Owner()]; return EXPRESSION; }
            """.Replace("EXPRESSION", expression).Replace("COPY_MEMBERS", expression.StartsWith("default") ? "" :
                "public Owner() { number = 0; } public copy Owner(immutable(Owner)* source) { number = source.number; putchar(67); }"));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(output, result.StandardOutput);
    }

    [Fact]
    public void ThrowingCastCopyKeepsItsSourceAssignedForTheCatchPath()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            struct Owner {
                private int number;
                public Owner() { number = 42; }
                public copy Owner(immutable(Owner)* source) { putchar(67); throw new Exception(); }
                public int Read() => number;
                ~Owner() { putchar(68); }
            }
            int main() {
                Owner source = new Owner();
                try { (Owner)source; Owner consumed = move source; }
                catch { if (source.Read() == 42) putchar(65); }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("CAD", result.StandardOutput);
    }
}

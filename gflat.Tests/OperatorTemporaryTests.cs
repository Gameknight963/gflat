namespace gflat.Tests;

public sealed class OperatorTemporaryTests
{
    [Theory]
    [InlineData("(-value).Value")]
    [InlineData("(value + value).Value")]
    [InlineData("(-value).Read()")]
    [InlineData("(value + value).Read()")]
    public void MembersCanReadAnOperatorResultTemporary(string expression)
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct Number {
                public int Value;
                public Number(int value) { Value = value; }
                public int Read() => Value;
                public static Number operator -(Number value) => new Number(42);
                public static Number operator +(Number left, Number right) => new Number(42);
            }
            int main() { Number value = new Number(1); return EXPRESSION; }
            """.Replace("EXPRESSION", expression)).ExitCode);

}

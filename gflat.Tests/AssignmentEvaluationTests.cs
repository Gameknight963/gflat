namespace gflat.Tests;

public sealed class AssignmentEvaluationTests
{
    [Fact]
    public void CompoundAssignmentCapturesTheOldValueBeforeEvaluatingTheRightOperand()
        => Assert.Equal(3, CompilerTestHelper.Run("""
            int Change(ref int value) { value = 10; return 2; }
            int main() { int value = 1; value += Change(ref value); return value; }
            """).ExitCode);

    [Theory]
    [InlineData("*Target(&value) += Value();", "TV")]
    [InlineData("Target(&value)[0] += Value();", "TV")]
    public void CompoundStorageTargetIsEvaluatedOnceBeforeTheRightOperand(string statement, string output)
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            int* Target(int* value) { putchar(84); return value; }
            int Value() { putchar(86); return 2; }
            int main() { int value = 1; STATEMENT return value; }
            """.Replace("STATEMENT", statement));
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(output, result.StandardOutput);
    }

    [Fact]
    public void ThrowingCompoundTargetPreventsRightOperandEvaluation()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            int* Target() { putchar(84); throw new Exception(); }
            int Value() { putchar(86); return 2; }
            int main() { try { *Target() += Value(); } catch {} return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("T", result.StandardOutput);
    }

    [Fact]
    public void ReferenceIndexerCapturesAddressAndOldValueBeforeTheRightOperand()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            struct Buffer {
                private int* data;
                public Buffer(int* storage) { data = storage; }
                public ref int this[int index] { get { putchar(71); return data[index]; } }
            }
            Buffer* Receiver(Buffer* value) { putchar(82); return value; }
            int Index() { putchar(73); return 0; }
            int Value(ref int value) { putchar(86); value = 10; return 2; }
            int main() {
                int value = 1; Buffer buffer = new Buffer(&value);
                (*Receiver(&buffer))[Index()] += Value(ref value);
                return value;
            }
            """);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("RIGV", result.StandardOutput);
    }
}

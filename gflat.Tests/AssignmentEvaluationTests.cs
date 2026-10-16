namespace gflat.Tests;

public sealed class AssignmentEvaluationTests
{
    [Theory]
    [InlineData("*Target(&value) = Value();")]
    [InlineData("Target(&value)[0] = Value();")]
    public void SimpleStorageTargetIsEvaluatedOnceBeforeTheValue(string statement)
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            int* Target(int* value) { putchar(84); return value; }
            int Value() { putchar(86); return 42; }
            int main() { int value = 0; STATEMENT return value; }
            """.Replace("STATEMENT", statement));
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("TV", result.StandardOutput);
    }

    [Fact]
    public void SimpleReferenceIndexerEvaluatesReceiverIndexAndGetterBeforeTheValue()
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
            int Value() { putchar(86); return 42; }
            int main() {
                int value = 0; Buffer buffer = new Buffer(&value);
                (*Receiver(&buffer))[Index()] = Value(); return value;
            }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("RIGV", result.StandardOutput);
    }

    [Fact]
    public void ThrowingSimpleTargetPreventsConstructionOfTheAssignedOwner()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            struct Owner { public Owner() { putchar(86); } ~Owner() { putchar(68); } }
            Owner* Target() { putchar(84); throw new Exception(); }
            int main() { try { *Target() = new Owner(); } catch { putchar(67); } return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("TC", result.StandardOutput);
    }

    [Fact]
    public void ThrowingValueDestroysTheTemporaryReferenceIndexerReceiver()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            struct Buffer {
                private int* data;
                public Buffer(int* storage) { data = storage; putchar(77); }
                public ref int this[int index] { get { putchar(71); return data[index]; } }
                ~Buffer() { putchar(68); }
            }
            int Fail() { putchar(70); throw new Exception(); }
            int main() {
                int value = 42;
                try { new Buffer(&value)[0] = Fail(); } catch { putchar(67); }
                return value;
            }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("MGFDC", result.StandardOutput);
    }

    [Fact]
    public void ThrowingValueDoesNotDestroyTheExistingOwnerAtTheCapturedDestination()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            struct Owner {
                private int marker;
                public Owner(int value) { marker = value; }
                ~Owner() { putchar(marker); }
            }
            Owner* Target(Owner* owner) { putchar(84); return owner; }
            Owner Fail() { putchar(70); throw new Exception(); }
            int main() {
                Owner owner = new Owner(65);
                try { *Target(&owner) = Fail(); } catch { putchar(67); }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("TFCA", result.StandardOutput);
    }

    [Fact]
    public void AddressSideEffectsInitializeLocalsBeforeTheAssignedValueIsRead()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int main() {
                int index; int[1] data = [0];
                data[index = 0] = index + 42;
                return data[0];
            }
            """).ExitCode);

    [Theory]
    [InlineData("int index; int[1] data = [0]; data[index] = (index = 0);")]
    [InlineData("int value; try { *Target() = (value = 1); } catch { return value; }")]
    public void AssignmentAnalysisDoesNotInitializeTheValueBeforeTheTargetHasRun(string body)
        => Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => CompilerTestHelper.Check("""
            int* Target() { throw new Exception(); }
            int main() { BODY return 0; }
            """.Replace("BODY", body)));

    [Fact]
    public void SimpleWritesDoNotReadUninitializedLocalOrStructFieldValues()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct Pair { public int left; public int right; }
            int main() { int value; value = 20; Pair pair; pair.left = value; pair.right = 22; return pair.left + pair.right; }
            """).ExitCode);

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

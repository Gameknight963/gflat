using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReferenceReturnStorageTests
{
    [Theory]
    [InlineData("Pair local = new Pair(); return local.value;")]
    [InlineData("int[1] local = [42]; return local[0];")]
    [InlineData("Pair[1] local = [new Pair()]; return local[0].value;")]
    [InlineData("return new Pair().value;")]
    public void ReferenceGettersCannotReturnProjectionsOfLocalOrTemporaryValues(string body)
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("""
            struct Pair { public int value; }
            struct Source { public ref int Value { get { BODY } } }
            """.Replace("BODY", body)));
        Assert.Contains("existing storage", error.Description);
    }

    [Fact]
    public void BorrowedLocalViewsAndPointerReceiversCanReturnUnderlyingStorage()
    {
        var result = CompilerTestHelper.Run("""
            struct View {
                int* data;
                public View(int* pointer) { data = pointer; }
                public ref int Current { get => *data; }
                public ref int this[int index] { get => data[index]; }
            }
            struct Source {
                int* data;
                public Source(int* pointer) { data = pointer; }
                public ref int Value { get { View local = new View((int*)data); return local.Current; } }
                public ref int this[int index] { get { View local = new View((int*)data); return local[index]; } }
            }
            int main() {
                int value = 0;
                Source source = new Source(&value);
                source.Value = 20;
                source[0] += 22;
                return value;
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }
}

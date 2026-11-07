namespace gflat.Tests;

public sealed class ConstantReceiverDereferenceTests
{
    [Theory]
    [InlineData("struct", 0)]
    [InlineData("struct", 2)]
    [InlineData("class", 0)]
    [InlineData("class", 2)]
    public void ExplicitReceiverInitializationMatchesRuntime(string kind, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run($$"""
            {{kind}} Item
            {
                public readonly int Value;
                public const Item(int input) { (*this).Value = input; }
            }
            const int Read(int input) { Item item = new Item(input); return item.Value; }
            int Input() => 42;
            int main()
            {
                const int folded = Read(42);
                int runtime = Read(Input());
                if (folded != 42 || runtime != 42) return 1;
                return 0;
            }
            """, optimization).ExitCode);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitReceiverReadsAndWritesMatchRuntime(int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Item
            {
                public int Value;
                public const Item(int input) { Value = input; }
                public const int Next() { (*this).Value += 1; return (*this).Value; }
            }
            const int Read(int input) { Item item = new Item(input); return item.Next(); }
            int Input() => 41;
            int main()
            {
                const int folded = Read(41);
                int runtime = Read(Input());
                if (folded != 42 || runtime != 42) return 1;
                return 0;
            }
            """, optimization).ExitCode);
}

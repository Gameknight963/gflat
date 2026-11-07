using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReadonlyConstructorReceiverTests
{
    [Theory]
    [InlineData("struct", "readonly(int)", 0)]
    [InlineData("struct", "readonly(int)", 2)]
    [InlineData("struct", "readonly int", 0)]
    [InlineData("struct", "readonly int", 2)]
    [InlineData("class", "readonly(int)", 0)]
    [InlineData("class", "readonly(int)", 2)]
    [InlineData("class", "readonly int", 0)]
    [InlineData("class", "readonly int", 2)]
    public void ExplicitReceiverDereferenceCanInitializeOwnReadonlyField(string kind, string type, int optimization)
        => Assert.Equal(42, CompilerTestHelper.Run($$"""
            {{kind}} Item
            {
                public {{type}} Value;
                public Item(int input) { (*this).Value = input; }
            }
            int main() { Item item = new Item(42); return item.Value; }
            """, optimization).ExitCode);

    [Theory]
    [InlineData("struct", "public Item(Item* other) { Value = 1; (*other).Value = 42; }")]
    [InlineData("class", "public Item(Item* other) { Value = 1; (*other).Value = 42; }")]
    [InlineData("struct", "public Item() { Value = 1; } public void Change() { (*this).Value = 42; }")]
    [InlineData("class", "public Item() { Value = 1; } public void Change() { (*this).Value = 42; }")]
    public void ExplicitDereferenceDoesNotGrantOtherReadonlyWrites(string kind, string members)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(kind + " Item { public readonly int Value; " + members + " }"));
        Assert.Contains("readonly", error.Message);
    }

    [Theory]
    [InlineData("this.Value")]
    [InlineData("(*this).Value")]
    public void DerivedConstructorsCannotInitializeBaseReadonlyFields(string target)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("class Base { public readonly int Value; public Base() { Value = 1; } }" +
            "class Item : Base { public Item() { " + target + " = 42; } }"));
        Assert.Contains("readonly", error.Message);
    }
}

namespace gflat.Tests;

public sealed class AggregateCallbackTests
{
    [Fact]
    public void CallbackFieldsCanUseTheEnclosingTypeByValue()
    {
        var result = CompilerTestHelper.Run("""
            struct Item {
                public int Value;
                private int(Item)* read;
                public Item(int value, int(Item)* callback) { Value = value; read = callback; }
                public int Read() => read(*this);
            }
            int Read(Item item) => item.Value;
            int main() { Item item = new Item(42, Read); return item.Read(); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void CallbackFieldsCanUseALaterDeclaredTypeByValue()
    {
        var result = CompilerTestHelper.Run("""
            struct Callback {
                private int(Item)* read;
                public Callback(int(Item)* callback) { read = callback; }
                public int Invoke(Item item) => read(item);
            }
            struct Item { public int Value; public Item(int value) { Value = value; } }
            int Read(Item item) => item.Value;
            int main() { Callback callback = new Callback(Read); return callback.Invoke(new Item(42)); }
            """);
        Assert.Equal(42, result.ExitCode);
    }
}

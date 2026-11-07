namespace gflat.Tests;

public sealed class NestedExtensionReceiverTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RepeatedNestedExtensionsKeepConcreteCallbackTypes(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            struct Outer<T>
            {
                public struct Item
                {
                    public T Value;
                    public Item(T value) { Value = value; }
                }
            }
            struct Box<T>
            {
                public T Value;
                public Box(T value) { Value = value; }
            }
            extension Outer<T>::Item
            {
                public readonly Box<readonly(T)*> Borrow() => new Box<readonly(T)*>(&(*this).Value);
            }
            extension Box<T>
            {
                public bool Check(bool(T)* predicate) => predicate((*this).Value);
            }
            bool IsAnswer(readonly(int)* value) => *value == 42;
            int main()
            {
                Outer<int>::Item item = new(42);
                Box<readonly(int)*> first = item.Borrow();
                if (!first.Check(IsAnswer)) return 1;
                if (!item.Borrow().Check(IsAnswer)) return 2;
                return 0;
            }
            """, optimization);
        Assert.True(result.ExitCode == 0, $"Nested callback exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExistingNestedGenericReceiversSupportReturnedQueryLikeChains(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            struct Outer<T>
            {
                public struct Item
                {
                    public T Value;
                    public Item(T value) { Value = value; }
                }
            }
            struct Box<T>
            {
                public T Value;
                public Box(T value) { Value = value; }
            }
            extension Outer<T>::Item
            {
                public Box<Outer<T>::Item> Wrap() => new Box<Outer<T>::Item>(*this);
            }
            extension Box<T>
            {
                public T Unwrap() => (*this).Value;
            }
            int main()
            {
                Outer<int>::Item item = new(42);
                if (item.Wrap().Unwrap().Value != 42) return 1;
                Outer<long>::Item other = new(99l);
                if (other.Wrap().Unwrap().Value != 99l) return 2;
                return 0;
            }
            """, optimization);
        Assert.True(result.ExitCode == 0, $"Nested extension exit {result.ExitCode}: {result.StandardError}");
    }
}

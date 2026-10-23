namespace gflat.Tests;

public sealed class GenericBaseBindingReentryTests
{
    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void BaseSpecializationsCanReenterOverloadedParameterBinding(string kind)
    {
        // Seed resolves a nested iterator before IO's parameter creates another
        // specialization. Preparing that iterator's overloads revisits IO.
        Compiler.Check($$"""
            interface I<T, Iterator> { Iterator GetEnumerator(); }
            struct View<T> : I<T, View<T>::Iterator> {
                public struct Iterator { }
                public Iterator GetEnumerator() => new();
            }
            {{kind}} Buffer<T> : I<T, View<T>::Iterator> {
                public View<T>::Iterator GetEnumerator() => new();
            }
            class Seed : I<int, View<int>::Iterator> {
                public View<int>::Iterator GetEnumerator() => new();
            }
            static class IO {
                public static void Write(int value) { }
                public static void Write(ref immutable(Buffer<byte>) value) { }
            }
            int main() => 0;
            """);
    }
}

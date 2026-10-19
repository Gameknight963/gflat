namespace gflat.Tests;

public class StandardHashingTests
{
    private static IEnumerable<SourceFile> Sources(string code) =>
        new[] { "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "IStringConvertable.gf", "String.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));

    private static void Run(string code) => Assert.Equal(0, CompilerTestHelper.Run(Sources(code)).ExitCode);

    [Theory]
    [InlineData("byte", "0", "255")]
    [InlineData("sbyte", "-128", "127")]
    [InlineData("short", "-32768", "32767")]
    [InlineData("ushort", "0", "65535")]
    [InlineData("int", "-2147483647 - 1", "2147483647")]
    [InlineData("uint", "0", "4294967295u")]
    [InlineData("long", "-9223372036854775807l - 1l", "9223372036854775807l")]
    [InlineData("ulong", "0", "18446744073709551615ul")]
    [InlineData("nint", "-123", "123")]
    [InlineData("nuint", "0", "123")]
    [InlineData("char", "0", "127")]
    [InlineData("bool", "false", "true")]
    [InlineData("float", "-1.25f", "1.25f")]
    [InlineData("double", "-1.25d", "1.25d")]
    public void ScalarsSupportBorrowedGenericAndInterfaceCalls(string type, string low, string high)
        => Run($$"""
            using std;
            bool Same<T : IEquatable<T>>(ref readonly(T) a, ref readonly(T) b) => a.Equals(&b);
            int Order<T : IComparable<T>>(ref readonly(T) a, ref readonly(T) b) => a.CompareTo(&b);
            nuint Hash<T : IHashable>(ref readonly(T) a) => a.GetHashCode();
            int main() {
                {{type}} a = ({{type}})({{low}}); {{type}} b = ({{type}})({{high}}); {{type}} copy = a;
                if (!Same(ref a, ref copy) || Same(ref a, ref b)) return 1;
                if (Order(ref a, ref b) >= 0 || Order(ref b, ref a) <= 0 || Order(ref a, ref copy) != 0) return 2;
                if (Hash(ref a) != Hash(ref copy)) return 3;
                IHashable* hash = &a;
                IEquatable<{{type}}>* equal = &a;
                IComparable<{{type}}>* compare = &a;
                if (hash.GetHashCode() != Hash(ref a) || !equal.Equals(&copy) || compare.CompareTo(&b) >= 0) return 4;
                return 0;
            }
            """);

    [Fact]
    public void StringUsesCountedOrdinalContentsWithoutCopyingOrNullTruncation()
        => Run("""
            using std;
            int main() {
                String a = String::From("a\0b", 3); String b = String::From("a\0b", 3);
                String shorter = s"a"; String greater = String::From("a\0c", 3);
                IHashable* hashA = &a; IHashable* hashB = &b;
                IEquatable<String>* equality = &a; IComparable<String>* comparison = &a;
                if (!equality.Equals(&b) || hashA.GetHashCode() != hashB.GetHashCode()) return 1;
                if (a.Equals(&shorter) || comparison.CompareTo(&shorter) <= 0 || comparison.CompareTo(&greater) >= 0) return 2;
                String empty = new(); if (empty.GetHashCode() != (nuint)2166136261u) return 3;
                String hello = s"hello"; if (hello.GetHashCode() != (nuint)1335831723) return 4;
                b[2] = 'c'; if (a.Equals(&b) || a.GetHashCode() == b.GetHashCode()) return 5;
                return 0;
            }
            """);

    [Theory]
    [InlineData("float", "uint", "0x7fc00001u", "0x7fc00002u")]
    [InlineData("double", "ulong", "0x7ff8000000000001ul", "0x7ff8000000000002ul")]
    public void FloatingNaNsAndSignedZerosHaveConsistentEqualityHashAndOrdering(string type, string bits, string firstNaN, string secondNaN)
        => Run($$"""
            using std;
            int main() {
                {{bits}} aBits = {{firstNaN}}; {{bits}} bBits = {{secondNaN}};
                {{type}} a = *(({{type}}*)&aBits); {{type}} b = *(({{type}}*)&bBits);
                {{type}} zero = ({{type}})0; {{type}} negativeZero = -zero; {{type}} number = ({{type}})1;
                IEquatable<{{type}}>* equal = &a; IComparable<{{type}}>* compare = &a;
                IHashable* ah = &a; IHashable* bh = &b;
                if (!equal.Equals(&b) || equal.Equals(&number) || ah.GetHashCode() != bh.GetHashCode()) return 1;
                if (compare.CompareTo(&b) != 0 || compare.CompareTo(&number) >= 0) return 2;
                IEquatable<{{type}}>* zeros = &zero; IHashable* zh = &zero; IHashable* nh = &negativeZero;
                if (!zeros.Equals(&negativeZero) || zh.GetHashCode() != nh.GetHashCode()) return 3;
                return 0;
            }
            """);
}


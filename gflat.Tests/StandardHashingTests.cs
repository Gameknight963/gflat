namespace gflat.Tests;

public class StandardHashingTests
{
    private static IEnumerable<SourceFile> Sources(string code, bool maps = false) =>
        new[] { "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "IStringConvertable.gf", "String.gf", "ByteSearch.gf" }
            .Concat(maps ? ["Collections/HashMap.gf"] : [])
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
    [InlineData("extralong", "-170141183460469231731687303715884105728", "170141183460469231731687303715884105727")]
    [InlineData("nint", "-123", "123")]
    [InlineData("nuint", "0", "123")]
    [InlineData("char", "0", "255")]
    [InlineData("bool", "false", "true")]
    [InlineData("float", "-1.25f", "1.25f")]
    [InlineData("double", "-1.25d", "1.25d")]
    public void ScalarsSupportBorrowedGenericAndInterfaceCalls(string type, string low, string high)
        => Run($$"""
            using std; using std::Collections;
            bool Same<T : IEquatable<T>>(ref immutable(T) a, ref immutable(T) b) => a.Equals(&b);
            int Order<T : IComparable<T>>(ref immutable(T) a, ref immutable(T) b) => a.CompareTo(&b);
            nuint Hash<T : IHashable>(ref immutable(T) a) => a.GetHashCode();
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
            using std; using std::Collections;
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
            using std; using std::Collections;
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

    [Fact]
    public void StringContractCallsBorrowStorageAndNeverAllocate()
        => Run("""
            using std; using std::Collections;
            extern void* malloc(nuint count);
            extern void free(void* data);
            int allocations = 0; int releases = 0;
            namespace Memory {
                public replace void*? Allocate(nuint count) { allocations++; return malloc(count); }
                public replace void Free(void* data) nothrow { releases++; free(data); }
            }
            nuint Hash<T : IHashable>(ref immutable(T) value) => value.GetHashCode();
            bool Equal<T : IEquatable<T>>(ref immutable(T) a, ref immutable(T) b) => a.Equals(&b);
            int Compare<T : IComparable<T>>(ref immutable(T) a, ref immutable(T) b) => a.CompareTo(&b);
            int main() {
                {
                    String a = s"borrowed"; String b = s"borrowed"; b.Reserve(100);
                    int before = allocations;
                    for (int i = 0; i < 100; i++) {
                        if (!Equal(ref a, ref b) || Compare(ref a, ref b) != 0 || Hash(ref a) != Hash(ref b)) return 1;
                    }
                    if (allocations != before || a.Capacity == b.Capacity) return 2;
                }
                if (allocations != releases) return 3;
                return 0;
            }
            """);

    [Fact]
    public void StringComparisonUsesUnsignedBytesAndFullLengths()
        => Run("""
            using std; using std::Collections;
            int main() {
                char[2] highBytes = [(char)255, '\0'];
                char[2] lowBytes = [(char)127, '\0'];
                String high = String::From(highBytes, 1); String low = String::From(lowBytes, 1);
                String longer = String::From(highBytes, 2); String empty = new();
                if (high.CompareTo(&low) <= 0 || low.CompareTo(&high) >= 0) return 1;
                if (high.CompareTo(&longer) >= 0 || empty.CompareTo(&high) >= 0) return 2;
                return 0;
            }
            """);

    [Theory]
    [InlineData("float", "uint", "0x7fc00001u", "0xffc00002u", "0x7f800000u")]
    [InlineData("double", "ulong", "0x7ff8000000000001ul", "0xfff8000000000002ul", "0x7ff0000000000000ul")]
    public void HashMapSeparatesNaNsFromZeroDespiteSharedHashes(string type, string bits, string firstNaN, string secondNaN, string infinity)
    {
        string code = $$"""
            using std; using std::Collections;
            int main() {
                {{bits}} firstBits = {{firstNaN}}; {{bits}} secondBits = {{secondNaN}}; {{bits}} infiniteBits = {{infinity}};
                {{type}} nan = *(({{type}}*)&firstBits); {{type}} otherNaN = *(({{type}}*)&secondBits);
                {{type}} positiveInfinity = *(({{type}}*)&infiniteBits); {{type}} negativeInfinity = -positiveInfinity;
                {{type}} zero = ({{type}})0; {{type}} negativeZero = -zero;
                HashMap<{{type}}, int> map = new();
                if (!map.TryAdd(nan, 1)) return 1;
                if (map.TryAdd(otherNaN, 2)) return 2;
                if (!map.TryAdd(zero, 3)) return 3;
                if (map.TryAdd(negativeZero, 4)) return 4;
                map.Add(positiveInfinity, 5); map.Add(negativeInfinity, 6);
                map.Reserve(1000);
                if (map.Count != (nuint)4) return 5;
                if (map[&otherNaN] != 1 || map[&negativeZero] != 3) return 6;
                if (map[&positiveInfinity] != 5 || map[&negativeInfinity] != 6) return 7;
                map.Set(otherNaN, 7); if (map[&nan] != 7) return 8;
                if (!map.Remove(&nan) || map.ContainsKey(&otherNaN)) return 9;
                if (!map.ContainsKey(&zero) || map[&negativeZero] != 3) return 10;
                return 0;
            }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(Sources(code, maps: true)).ExitCode);
    }

    [Fact]
    public void ManagedPrimitiveInterfaceReferenceKeepsItsReceiverAlive()
    {
        string code = """
            using std; using std::Collections;
            IHashable^ Make() { int^ value = new(42); return value; }
            int main() {
                IHashable^ hash = Make();
                GC::Collect();
                int value = 42; IHashable* expected = &value;
                if (hash.GetHashCode() != expected.GetHashCode()) return 1;
                if (std::TracingCollector::ManagedCount != (nuint)1) return 2;
                return 0;
            }
            """;
        var sources = Sources(code).Concat(new[] { "GC.gf", "TracingCollector.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/libc", name)))));
        Assert.Equal(0, CompilerTestHelper.Run(sources).ExitCode);
    }

    [Fact]
    public void ExtraLongMapKeysKeepDistinctValuesAndSurviveRehash()
    {
        string code = """
            using std; using std::Collections;
            int main() {
                extralong minimum = -170141183460469231731687303715884105728;
                extralong maximum = 170141183460469231731687303715884105727;
                extralong zero = 0; extralong one = 1; extralong high = (extralong)1 << 64;
                HashMap<extralong, int> map = new();
                map.Add(minimum, 1); map.Add(maximum, 2); map.Add(zero, 3); map.Add(one, 4); map.Add(high, 5);
                map.Reserve(1000);
                if (map[&minimum] != 1 || map[&maximum] != 2 || map[&zero] != 3) return 3;
                if (map[&one] != 4 || map[&high] != 5) return 4;
                if (map.TryAdd(high, 6) || map.Count != (nuint)5) return 5;
                if (!map.Remove(&high) || map.ContainsKey(&high)) return 6;
                if (map[&one] != 4 || map.Count != (nuint)4) return 7;
                return 0;
            }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(Sources(code, maps: true)).ExitCode);
    }
}


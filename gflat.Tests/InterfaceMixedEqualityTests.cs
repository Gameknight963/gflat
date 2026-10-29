namespace gflat.Tests;

public sealed class InterfaceMixedEqualityTests
{
    private const string Declarations = """
        namespace Contracts { interface I { int Read(); } }
        alias View = Contracts::I;
        struct Entry : Contracts::I { public int Id; public Entry(int id) { Id = id; } public int Read() => Id; }
        class Box : Contracts::I { public int Id; public Box(int id) { Id = id; } public int Read() => Id; }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RawInterfaceAndConcretePointersCompareDataIdentityInEitherOrder(int optimization)
    {
        const string source = Declarations + """
            bool StructPair(View* left, Entry* right, bool expected) {
                return (left == right) == expected && (right == left) == expected
                    && (left != right) != expected && (right != left) != expected;
            }
            bool ClassPair(View* left, Box* right, bool expected) {
                return (left == right) == expected && (right == left) == expected
                    && (left != right) != expected && (right != left) != expected;
            }
            bool NullableStructPair(View*? left, Entry*? right, bool expected) {
                return (left == right) == expected && (right == left) == expected
                    && (left != right) != expected && (right != left) != expected;
            }
            bool NullableClassPair(View*? left, Box*? right, bool expected) {
                return (left == right) == expected && (right == left) == expected
                    && (left != right) != expected && (right != left) != expected;
            }
            bool ReadonlyPair(readonly(View)* left, readonly(Entry)* right, bool expected) {
                return (left == right) == expected && (right == left) == expected
                    && (left != right) != expected && (right != left) != expected;
            }
            bool ReadonlyNullablePair(readonly(View)*? left, readonly(Entry)*? right, bool expected) {
                return (left == right) == expected && (right == left) == expected
                    && (left != right) != expected && (right != left) != expected;
            }
            int main() {
                Entry first = new(7); Entry second = new(7); Box box = new(9); Box otherBox = new(9);
                View* structView = &first; View* classView = &box;
                if (!StructPair(structView, &first, true) || !StructPair(structView, &second, false)) return 1;
                if (!ClassPair(classView, &box, true) || !ClassPair(classView, &otherBox, false)) return 2;
                View*? absentView = null; Entry*? absentEntry = null; Box*? absentBox = null;
                if (!NullableStructPair(absentView, absentEntry, true) || !NullableStructPair(structView, absentEntry, false)
                    || !NullableStructPair(absentView, &first, false) || !NullableStructPair(structView, &first, true)) return 3;
                if (!NullableClassPair(absentView, absentBox, true) || !NullableClassPair(classView, absentBox, false)
                    || !NullableClassPair(absentView, &box, false) || !NullableClassPair(classView, &box, true)) return 4;
                if (!ReadonlyPair(structView, &first, true) || !ReadonlyPair(structView, &second, false)) return 5;
                if (!ReadonlyNullablePair(absentView, absentEntry, true) || !ReadonlyNullablePair(structView, absentEntry, false)
                    || !ReadonlyNullablePair(absentView, &first, false) || !ReadonlyNullablePair(structView, &first, true)) return 6;
                // Existing fat/fat, concrete/concrete and literal-null behavior remains valid.
                View* sameView = &first; View* otherView = &second;
                if (structView != sameView || structView == otherView || &first != &first || &first == &second) return 7;
                if (absentView != null || null != absentView || !(absentView == null) || !(null == absentView)) return 9;
                return 0;
            }
            """;
        Run(source, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedInterfaceAndConcreteReferencesUseTheSameAddressNormalization(int optimization)
    {
        // Existing test adapter pattern: malloc supplies live objects, not a real collector.
        const string source = Declarations + """
            extern void*? malloc(nuint size);
            alias GC = TestCollector;
            static class TestCollector {
                public static void* Allocate(nuint size, nuint alignment) => malloc(size)!;
                public static void ReferenceAdded(void* slot) nothrow { }
                public static void ReferenceRemoved(void* slot) nothrow { }
            }
            bool StructPair(View^ left, Entry^ right, bool expected) {
                return (left == right) == expected && (right == left) == expected
                    && (left != right) != expected && (right != left) != expected;
            }
            bool ClassPair(View^ left, Box^ right, bool expected) {
                return (left == right) == expected && (right == left) == expected
                    && (left != right) != expected && (right != left) != expected;
            }
            bool NullableStructPair(View^? left, Entry^? right, bool expected) {
                return (left == right) == expected && (right == left) == expected
                    && (left != right) != expected && (right != left) != expected;
            }
            bool NullableClassPair(View^? left, Box^? right, bool expected) {
                return (left == right) == expected && (right == left) == expected
                    && (left != right) != expected && (right != left) != expected;
            }
            bool ReadonlyPair(readonly(View)^ left, readonly(Entry)^ right, bool expected) {
                return (left == right) == expected && (right == left) == expected
                    && (left != right) != expected && (right != left) != expected;
            }
            int main() {
                Entry^ first = new^ Entry(7); Entry^ second = new^ Entry(7);
                Box^ box = new^ Box(9); Box^ otherBox = new^ Box(9);
                View^ managedView = first; View^ classView = box;
                if (!StructPair(managedView, first, true) || !StructPair(managedView, second, false)) return 1;
                if (!ClassPair(classView, box, true) || !ClassPair(classView, otherBox, false)) return 2;
                View^? absentView = null; Entry^? absentEntry = null; Box^? absentBox = null;
                if (!NullableStructPair(absentView, absentEntry, true) || !NullableStructPair(managedView, absentEntry, false)
                    || !NullableStructPair(absentView, first, false) || !NullableStructPair(managedView, first, true)) return 3;
                if (!NullableClassPair(absentView, absentBox, true) || !NullableClassPair(classView, absentBox, false)
                    || !NullableClassPair(absentView, box, false) || !NullableClassPair(classView, box, true)) return 4;
                if (!ReadonlyPair(managedView, first, true) || !ReadonlyPair(managedView, second, false)) return 5;
                View^ sameView = first; View^ otherView = second;
                if (managedView != sameView || managedView == otherView || first != first || first == second) return 6;
                if (absentView != null || null != absentView || !(absentView == null) || !(null == absentView)) return 7;
                return 0;
            }
            """;
        Run(source, optimization);
    }

    private static void Run(string source, int optimization)
    {
        string ir = Compiler.Emit([new SourceFile("interface-mixed-equality.gf", source)],
            options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}

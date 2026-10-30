using gflat.CompileExceptions;
using gflat.ast;

namespace gflat.Tests;

public sealed class ArrayLiteralTypeIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferredNestedArraysCannotUnifyDifferentPointeeCapabilities(bool reverse)
    {
        const string prefix = """
            int main()
            {
                int writableValue = 1;
                immutable(int) protectedValue = 7;
                int* writable = &writableValue;
                readonly(int)* protectedPointer = &protectedValue;
                readonly(int*[1]) writablePointers = [writable];
                readonly(readonly(int)*[1]) protectedPointers = [protectedPointer];
            """;
        Check(prefix + "return 0; }");
        string elements = reverse ? "protectedPointers, writablePointers" : "writablePointers, protectedPointers";
        var error = Assert.Throws<TypeCheckException>(() => Check(prefix + "var rows = [" + elements + "]; return 0; }"));
        Assert.Contains("matching types and dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferredNestedArraysCannotUnifyReadonlyAndImmutableClassValues(bool reverse)
    {
        const string prefix = """
            class Item
            {
                public int Value;
                public Item(int value) { Value = value; }
            }
            int main()
            {
                readonly(readonly(Item)[1]) shallow = [new Item(1)];
                readonly(immutable(Item)[1]) deep = [new Item(2)];
            """;
        Check(prefix + "return 0; }");
        string elements = reverse ? "deep, shallow" : "shallow, deep";
        var error = Assert.Throws<TypeCheckException>(() => Check(prefix + "var rows = [" + elements + "]; return 0; }"));
        Assert.Contains("matching types and dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StructuredInferenceStillRejectsDifferentFixedDimensionsAtEveryArrayDepth()
    {
        foreach (string body in new[]
        {
            "int[1] first = [1]; int[2] second = [2, 3]; var rows = [first, second];",
            "int[1] first = [1]; int[2] second = [2, 3]; var rows = [second, first];",
            "int[1][1] first = [[1]]; int[2][1] second = [[2, 3]]; var cubes = [first, second];"
        })
        {
            var error = Assert.Throws<TypeCheckException>(() => Check("int main() { " + body + " return 0; }"));
            Assert.Contains("matching types and dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void StructuredInferenceDoesNotEraseNestedPointerNullability()
    {
        const string prefix = """
            int main()
            {
                int value = 9;
                int* pointer = &value;
                int*? maybe = pointer;
                readonly(int*[1]) present = [pointer];
                readonly(int*?[1]) optional = [maybe];
            """;
        Check(prefix + "return 0; }");
        foreach (string elements in new[] { "present, optional", "optional, present" })
        {
            var error = Assert.Throws<TypeCheckException>(() => Check(prefix + "var rows = [" + elements + "]; return 0; }"));
            Assert.Contains("matching types and dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MatchingStructuredTypesAliasesAndExplicitReadonlyTargetsKeepTheirActualCapabilities(int optimization)
    {
        const string source = """
            struct Outer<T> {
                public struct Item {
                    public T Value;
                    public Item(T value) { Value = value; }
                }
            }
            T First<T>(Outer<T>::Item value) {
                var rows = [value, value];
                return rows[0].Value;
            }
            alias Reader = readonly(int)*;
            alias ProtectedRow = readonly(Reader[1]);
            alias Callback = int(int)*;
            int Increment(int value) => value + 1;
            int main()
            {
                int first = 3;
                int second = 5;
                int* firstPointer = &first;
                int* secondPointer = &second;
                readonly(int*[1]) firstRow = [firstPointer];
                readonly(int*[1]) secondRow = [secondPointer];
                var writableRows = [firstRow, secondRow];
                *writableRows[0][0] = 17;
                *writableRows[1][0] = 19;
                if (first != 17 || second != 19 || sizeof(writableRows) != 2 * sizeof(int*)) return 1;

                ProtectedRow aliased = [firstPointer];
                readonly(readonly(int)*[1]) direct = [secondPointer];
                var protectedRows = [aliased, direct];
                if (*protectedRows[0][0] != 17 || *protectedRows[1][0] != 19) return 2;
                if (sizeof(protectedRows) != 2 * sizeof(int*)) return 3;

                int*? maybe = firstPointer;
                int*? absent = null;
                readonly(int*?[1]) present = [maybe];
                readonly(int*?[1]) missing = [absent];
                var nullableRows = [present, missing];
                if (nullableRows[0][0] == null || nullableRows[1][0] != null) return 4;

                readonly(Callback[1]) callbackFirst = [Increment];
                readonly(Callback[1]) callbackSecond = [&Increment];
                var callbacks = [callbackFirst, callbackSecond];
                if (callbacks[0][0](41) != 42 || callbacks[1][0](5) != 6) return 5;

                immutable(int) protectedValue = 7;
                readonly(int)* protectedPointer = &protectedValue;
                readonly(int)*[2] readers = [firstPointer, protectedPointer];
                readonly(int)*[2] reverseReaders = [protectedPointer, firstPointer];
                if (*readers[0] != 17 || *readers[1] != 7 || *reverseReaders[0] != 7 || *reverseReaders[1] != 17) return 6;
                if (protectedValue != 7) return 7;
                Outer<int>::Item item = new(42);
                if (First<int>(item) != 42) return 8;
                Outer<long>::Item wide = new(9000000000l);
                if (First<long>(wide) != 9000000000l) return 9;
                return 0;
            }
            """;
        string ir = Compiler.Emit(source, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Fact]
    public void ExactArrayBoundsPropagateWithoutChangingDefaultWildcardMatching()
    {
        TypeExpression scalar = new NamedTypeExpression("int", null, 0);
        TypeExpression fixedArray = new ArrayTypeExpression(scalar, 2, 0);
        TypeExpression unsizedArray = new ArrayTypeExpression(scalar, (int?)null, 0);
        (TypeExpression Fixed, TypeExpression Unsized)[] pairs =
        [
            (fixedArray, unsizedArray),
            (new ArrayTypeExpression(fixedArray, 1, 0), new ArrayTypeExpression(unsizedArray, 1, 0)),
            (new PointerTypeExpression(fixedArray, false, 0), new PointerTypeExpression(unsizedArray, false, 0)),
            (new ReferenceTypeExpression(fixedArray, 0), new ReferenceTypeExpression(unsizedArray, 0)),
            (new ManagedTypeExpression(fixedArray, false, 0), new ManagedTypeExpression(unsizedArray, false, 0)),
            (new FunctionPointerTypeExpression(fixedArray, [], false, false, 0),
                new FunctionPointerTypeExpression(unsizedArray, [], false, false, 0)),
            (new FunctionPointerTypeExpression(scalar, [fixedArray], false, false, 0),
                new FunctionPointerTypeExpression(scalar, [unsizedArray], false, false, 0))
        ];
        foreach (var pair in pairs)
        {
            Assert.True(TypeChecker.TypesMatchPublic(pair.Fixed, pair.Unsized));
            Assert.True(TypeChecker.TypesMatchPublic(pair.Unsized, pair.Fixed));
            Assert.False(TypeChecker.TypesMatchPublic(pair.Fixed, pair.Unsized, exactArraySizes: true));
            Assert.False(TypeChecker.TypesMatchPublic(pair.Unsized, pair.Fixed, exactArraySizes: true));
            Assert.True(TypeChecker.TypesMatchPublic(pair.Fixed, pair.Fixed, exactArraySizes: true));
        }
    }

    private static void Check(string source)
        => Compiler.Check(source, options: new() { NoStdLib = true, NoBuiltins = true });
}

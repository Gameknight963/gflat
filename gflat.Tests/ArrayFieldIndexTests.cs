namespace gflat.Tests;

public sealed class ArrayFieldIndexTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImplicitStructArrayFieldsSupportReadWriteAddressAndValueCopies(int optimization)
        => Run("""
            struct Bytes {
                private byte[2] data;
                public Bytes() { data = [(byte)20, (byte)22]; }
                public void Write(int index, byte value) { data[index] = value; }
                public immutable byte Read(int index) => data[index];
                public byte* Address(int index) => &data[index];
                public byte* Storage() => data;
                public immutable byte ReadCast(int index) => ((readonly(byte)*)data)[index];
                public immutable byte[2] Snapshot() => data;
            }
            int main() {
                Bytes value = new();
                if (value.Read(0) + value.Read(1) != 42) return 1;
                byte[2] copy = value.Snapshot();
                value.Write(1, (byte)7);
                *value.Address(0) = (byte)9;
                if (value.Read(0) != (byte)9 || value.ReadCast(1) != (byte)7) return 2;
                if (value.Storage()[0] != (byte)9) return 4;
                if (copy[0] != (byte)20 || copy[1] != (byte)22) return 3;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ClassAndInheritedArrayFieldsUseTheObjectsStorage(int optimization)
        => Run("""
            class Base {
                protected int[2] data;
                public Base() { data = [20, 22]; }
                public immutable int Read(int index) => data[index];
            }
            class Derived : Base {
                public Derived() : base() {}
                public void Write(int index, int value) { data[index] = value; }
                public int* Address(int index) => &data[index];
            }
            int main() {
                Derived value = new();
                if (value.Read(0) + value.Read(1) != 42) return 1;
                value.Write(1, 7); *value.Address(0) = 9;
                if (value.Read(0) != 9 || value.Read(1) != 7) return 2;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GenericNestedArrayFieldsKeepRowStorageAndEvaluateIndicesOnce(int optimization)
        => Run("""
            int calls = 0;
            int Row() { calls++; return 1; }
            struct Grid<T> {
                private T[2][2] data;
                public Grid(T a, T b, T c, T d) { data = [[a, b], [c, d]]; }
                public T Read(int row, int column) => data[row][column];
                public void Write(int row, int column, T value) { data[row][column] = value; }
                public void WriteChosen(T value) { data[Row()][0] = value; }
                public T* Address(int row, int column) => &data[row][column];
            }
            int main() {
                Grid<int> value = new(10, 20, 30, 40);
                if (value.Read(0, 1) != 20 || value.Read(1, 0) != 30) return 1;
                value.Write(0, 0, 11); value.WriteChosen(41); *value.Address(1, 1) = 42;
                if (calls != 1 || value.Read(1, 0) != 41 || value.Read(1, 1) != 42) return 2;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstantArraysAndReturnedArrayValuesStillIndexWithoutDuplicateCalls(int optimization)
        => Run("""
            int calls = 0;
            int[2] Make() { calls++; return [20, 22]; }
            struct Constants {
                private static const uint[2] Values = [20u, 22u];
                public static uint Read(int index) => Values[index];
            }
            int main() {
                if (Make()[1] != 22 || calls != 1) return 1;
                int[2][2] rows = [[1, 2], [3, 4]];
                rows[1][0] = 7;
                if (rows[1][0] != 7 || rows[0][0] != 1) return 2;
                if (Constants::Read(0) + Constants::Read(1) != 42u) return 3;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ArrayIndexingPreservesNonnullableStaticInitializationGuards(int optimization)
    {
        var result = CompilerTestHelper.Run([new SourceFile("array-static-startup.gf", """
            int storage = 42;
            int* first = ReadSecond();
            int*[1] second = Make();
            int* ReadSecond() => second[0];
            int*[1] Make() => [&storage];
            int main() => 0;
            """)], optimization);
        Assert.NotEqual(0, result.ExitCode);
    }

    private static void Run(string source, int optimization)
    {
        var result = CompilerTestHelper.Run([new SourceFile("array-fields.gf", source)], optimization);
        Assert.True(result.ExitCode == 0, $"Array field exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

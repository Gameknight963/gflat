using gflat.CompileExceptions;

namespace gflat.Tests.Std.Binary;

public sealed class BinaryCursorQualifierTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShallowReadonlyWriterExposesBorrowedBytesWithoutChangingCursor(int optimization)
    {
        string ir = Compiler.Emit(Sources("""
            using Standard::Binary;
            byte*? Inspect(readonly(BinaryBufferWriter)* writer) nothrow => writer.Data;
            int main() nothrow
            {
                byte[3] storage = [10, 20, 30];
                BinaryBufferWriter writer = new(storage, 3);
                if (!writer.TrySeek(1)) return 1;
                readonly(BinaryBufferWriter)* view = &writer;
                byte*? data = view.Data;
                if (data == null || data != &storage[0] || Inspect(view) != &storage[0]) return 2;
                data![1] = 42;
                if (storage[0] != (byte)10 || storage[1] != (byte)42 || storage[2] != (byte)30) return 3;
                if (view.Position != 1 || view.Capacity != 3 || view.Remaining != 2) return 4;
                BinaryBufferWriter empty = new();
                readonly(BinaryBufferWriter)* emptyView = &empty;
                if (emptyView.Data != null || Inspect(emptyView) != null) return 5;
                return 0;
            }
            """), options: new() { NoStdLib = true, NoBuiltins = true });
        Assert.DoesNotContain("personality", ir);
        Assert.DoesNotContain("@__gflat_eh_", ir);
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Theory]
    [InlineData("byte*? Inspect(immutable(BinaryBufferWriter)* writer) => writer.Data;", "non-immutable property accessor 'Data'")]
    [InlineData("void Reset(readonly(BinaryBufferWriter)* writer) { writer.Reset(); }", "non-readonly method 'Reset'")]
    public void RestrictedReceiversKeepTheirMutationBoundaries(string declaration, string diagnostic)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            Sources("using Standard::Binary; " + declaration + " int main() => 0;"),
            options: new() { NoStdLib = true, NoBuiltins = true }));
        Assert.Contains(diagnostic, error.Message);
    }

    private static SourceFile[] Sources(string program)
        => new[] { "Binary/BinaryPrimitives.gf", "Binary/BinaryBufferWriter.gf", "Memory/bytes.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", name))))
            .Append(new SourceFile("main.gf", program)).ToArray();
}

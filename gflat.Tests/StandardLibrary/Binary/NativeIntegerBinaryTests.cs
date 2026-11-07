using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace gflat.Tests.Std.Binary;

public sealed class NativeIntegerBinaryTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("NATIVE_INTEGER_BINARY.md", 0);

    [Fact]
    public void CursorGuideCompiles() => StandardLibraryGuideTests.CheckProgram("NATIVE_INTEGER_BINARY.md", 1);

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 2)]
    public void NativeIntegerCodecsMatchDotNetOnUnalignedBuffersAndPreflightShortCounts(bool signed, bool little, int optimization)
    {
        int width = IntPtr.Size;
        ulong mask = width == 8 ? ulong.MaxValue : uint.MaxValue;
        var values = new HashSet<ulong> { 0, 1, 255, 256, mask, mask >> 1, (mask >> 1) + 1 };
        for (int bit = 0; bit < width * 8; bit++)
        {
            values.Add(1ul << bit);
            values.Add((1ul << bit) - 1);
        }
        var random = new Random(821395);
        byte[] randomBytes = new byte[8];
        for (int i = 0; i < 256; i++)
        {
            random.NextBytes(randomBytes);
            values.Add(BitConverter.ToUInt64(randomBytes) & mask);
        }
        var expected = new List<byte>();
        foreach (ulong value in values)
        {
            byte[] bytes = new byte[width];
            if (signed)
            {
                nint native = unchecked((nint)(nuint)value);
                if (little) BinaryPrimitives.WriteIntPtrLittleEndian(bytes, native);
                else BinaryPrimitives.WriteIntPtrBigEndian(bytes, native);
                nint decoded = little ? BinaryPrimitives.ReadIntPtrLittleEndian(bytes) : BinaryPrimitives.ReadIntPtrBigEndian(bytes);
                Assert.Equal(native, decoded);
            }
            else
            {
                nuint native = (nuint)value;
                if (little) BinaryPrimitives.WriteUIntPtrLittleEndian(bytes, native);
                else BinaryPrimitives.WriteUIntPtrBigEndian(bytes, native);
                nuint decoded = little ? BinaryPrimitives.ReadUIntPtrLittleEndian(bytes) : BinaryPrimitives.ReadUIntPtrBigEndian(bytes);
                Assert.Equal(native, decoded);
            }
            expected.AddRange(bytes);
        }
        string scalar = signed ? "nint" : "nuint";
        string suffix = (signed ? "NInt" : "NUInt") + (little ? "Little" : "Big") + "Endian";
        string code = $$"""
            using Standard::Binary;

            int main()
            {
                if (sizeof(nint) != {{width}}) return 1;
                nuint[{{values.Count}}] values = [{{string.Join(",", values.Select(v => "(nuint)" + v + "ul"))}}];
                byte[{{expected.Count}}] expected = [{{string.Join(",", expected.Select(v => "(byte)" + v))}}];
                byte[16] buffer;
                for (nuint row = 0; row < {{values.Count}}; row++)
                for (nuint offset = 1; offset <= 3; offset++)
                {
                    for (nuint i = 0; i < 16; i++) buffer[i] = (byte)165;
                    if (!BinaryPrimitives::TryWrite{{suffix}}(&buffer[offset], 16 - offset, ({{scalar}})values[row])) return 2;
                    for (nuint i = 0; i < 16; i++)
                    {
                        byte value = (byte)165;
                        if (i >= offset && i - offset < {{width}}) value = expected[row * {{width}} + i - offset];
                        if (buffer[i] != value) return 3;
                    }
                    {{scalar}} result = ({{scalar}})77;
                    if (!BinaryPrimitives::TryRead{{suffix}}(&buffer[offset], 16 - offset, ref result) || result != ({{scalar}})values[row]) return 4;
                    BinaryBufferWriter writer = new(&buffer[0], offset + {{width}});
                    if (!writer.TrySkip(offset)) return 9;
                    BinaryBufferWriter copiedWriter = writer;
                    if (!writer.TryWrite{{suffix}}(({{scalar}})values[row]) || writer.Position != offset + {{width}} || writer.Remaining != 0) return 10;
                    if (copiedWriter.Position != offset) return 11;
                    if (writer.TryWrite{{suffix}}(({{scalar}})99) || writer.Position != offset + {{width}}) return 12;
                    for (nuint i = 0; i < {{width}}; i++)
                        if (buffer[offset + i] != expected[row * {{width}} + i]) return 13;
                    if (buffer[offset - 1] != (byte)165 || buffer[offset + {{width}}] != (byte)165) return 14;
                    BinaryBufferReader reader = new(&buffer[0], offset + {{width}});
                    if (!reader.TrySkip(offset)) return 15;
                    BinaryBufferReader copiedReader = reader;
                    result = ({{scalar}})77;
                    if (!reader.TryRead{{suffix}}(ref result) || result != ({{scalar}})values[row] || reader.Position != offset + {{width}} || reader.Remaining != 0) return 16;
                    if (copiedReader.Position != offset || !copiedReader.TryRead{{suffix}}(ref result) || result != ({{scalar}})values[row]) return 17;
                    if (reader.TryRead{{suffix}}(ref result) || result != ({{scalar}})0 || reader.Position != offset + {{width}}) return 18;
                    for (nuint count = 0; count < {{width}}; count++)
                    {
                        if (BinaryPrimitives::TryWrite{{suffix}}(&buffer[offset], count, ({{scalar}})0)) return 5;
                        BinaryBufferWriter shortWriter = new(&buffer[0], offset + count);
                        if (!shortWriter.TrySkip(offset) || shortWriter.TryWrite{{suffix}}(({{scalar}})99) || shortWriter.Position != offset || shortWriter.Remaining != count) return 19;
                        BinaryBufferReader shortReader = new(((nuint)1).reinterpret<readonly(byte)*>(), count);
                        result = ({{scalar}})77;
                        if (shortReader.TryRead{{suffix}}(ref result) || result != ({{scalar}})0 || shortReader.Position != 0 || shortReader.Remaining != count) return 20;
                        for (nuint i = 0; i < {{width}}; i++)
                            if (buffer[offset + i] != expected[row * {{width}} + i]) return 6;
                        result = ({{scalar}})77;
                        if (BinaryPrimitives::TryRead{{suffix}}(((nuint)1).reinterpret<readonly(byte)*>(), count, ref result) || result != ({{scalar}})0) return 7;
                        if (BinaryPrimitives::TryWrite{{suffix}}(((nuint)1).reinterpret<byte*>(), count, ({{scalar}})0)) return 8;
                    }
                }
                BinaryBufferReader emptyReader = new();
                BinaryBufferWriter emptyWriter = new();
                {{scalar}} emptyResult = ({{scalar}})77;
                if (emptyReader.TryRead{{suffix}}(ref emptyResult) || emptyResult != ({{scalar}})0 || emptyReader.Position != 0) return 21;
                if (emptyWriter.TryWrite{{suffix}}(({{scalar}})99) || emptyWriter.Position != 0) return 22;
                return 0;
            }
            """;
        var sources = StandardTestSources.Load("core/Binary/BinaryPrimitives.gf", "core/IO/IByteReader.gf",
            "core/Binary/BinaryBufferReader.gf", "core/Binary/BinaryBufferWriter.gf", "core/Memory/bytes.gf")
            .Append(new SourceFile("native-integer-binary.gf", code)).ToArray();
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        Assert.DoesNotContain("@malloc", ir);
        Assert.DoesNotContain("@__gflat_eh_", ir);
        var methods = Regex.Matches(ir, @"(?m)^define [^\r\n]*\$BinaryPrimitives\$Try(Read|Write)N(U)?Int[^\r\n]*");
        Assert.NotEmpty(methods);
        foreach (Match method in methods) Assert.Contains("nounwind", method.Value);
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Native integers exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

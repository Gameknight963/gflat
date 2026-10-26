using System.Buffers.Binary;
using System.Text;

namespace gflat.Tests;

public class StandardSignedBinaryTests
{
    private static readonly long[] Values = [long.MinValue, long.MaxValue, int.MinValue, int.MaxValue, short.MinValue, short.MaxValue, -1, 0, 1, -128, -129, 255, 256, -0x0123456789abcdefL];
    private static string Number(long value) => value == long.MinValue ? "(-9223372036854775807l - 1l)" : value + "l";
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private static long Truncate(long value, int width) => width switch { 2 => (short)value, 4 => (int)value, _ => value };
    private static byte[] Reference(long value, int width, bool little)
    {
        byte[] result = new byte[width];
        if (width == 2) { if (little) BinaryPrimitives.WriteInt16LittleEndian(result, (short)value); else BinaryPrimitives.WriteInt16BigEndian(result, (short)value); }
        else if (width == 4) { if (little) BinaryPrimitives.WriteInt32LittleEndian(result, (int)value); else BinaryPrimitives.WriteInt32BigEndian(result, (int)value); }
        else { if (little) BinaryPrimitives.WriteInt64LittleEndian(result, value); else BinaryPrimitives.WriteInt64BigEndian(result, value); }
        return result;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SignedBuffersMatchDotNetForUnalignedBoundaryValuesAndShortCapacity(int optimization)
    {
        var program = new StringBuilder("using std; using std::Binary; int main(){byte[10] data; short small=0; int medium=0; long large=0l;");
        foreach (int width in new[] { 2, 4, 8 })
        foreach (bool little in new[] { true, false })
        foreach (long original in Values)
        {
            long value = Truncate(original, width);
            string type = width == 2 ? "short" : width == 4 ? "int" : "long";
            string result = width == 2 ? "small" : width == 4 ? "medium" : "large";
            string suffix = $"Int{width * 8}{(little ? "Little" : "Big")}Endian";
            byte[] expected = Reference(value, width, little);
            program.Append("for(int i=0;i<10;i++) data[i]=(byte)165;");
            program.Append($"if(!BinaryPrimitives::TryWrite{suffix}(&data[1],9,({type}){Number(value)})) return 1;");
            for (int index = 0; index < width; index++) program.Append($"if(data[{index + 1}]!=(byte){expected[index]}) return 2;");
            program.Append($"if(data[0]!=(byte)165 || data[{width + 1}]!=(byte)165) return 3;");
            program.Append($"if(!BinaryPrimitives::TryRead{suffix}(&data[1],{width},ref {result}) || {result}!=({type}){Number(value)}) return 4;");
            for (int capacity = 0; capacity < width; capacity++)
            {
                program.Append($"if(BinaryPrimitives::TryWrite{suffix}(&data[1],{capacity},({type})0)) return 5;");
                for (int index = 0; index < width; index++) program.Append($"if(data[{index + 1}]!=(byte){expected[index]}) return 6;");
                program.Append($"if(BinaryPrimitives::TryRead{suffix}(&data[1],{capacity},ref {result}) || {result}!=({type})0) return 7;");
            }
        }
        program.Append("return 0;}");
        RunCore(program.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SuccessfulReadsPermitOutputToAliasInputWithoutZeroingBytesFirst(int optimization)
    {
        var program = new StringBuilder("using std; using std::Binary; int main(){");
        foreach (int width in new[] { 2, 4, 8 })
        foreach (bool signed in new[] { true, false })
        foreach (bool little in new[] { true, false })
        {
            string type = (width, signed) switch { (2, true) => "short", (2, false) => "ushort", (4, true) => "int", (4, false) => "uint", (8, true) => "long", _ => "ulong" };
            string suffix = $"{(signed ? "Int" : "UInt")}{width * 8}{(little ? "Little" : "Big")}Endian";
            // All-ones yields -1 or the unsigned maximum in either byte order.
            program.Append($"{{{type} value=({type})0; byte* bytes=(byte*)&value;");
            program.Append($"for(int i=0;i<{width};i++)bytes[i]=(byte)255;");
            program.Append($"if(!BinaryPrimitives::TryRead{suffix}(bytes,{width},ref value) || value!=({type})-1l)return 1;}}");
        }
        program.Append("return 0;}");
        RunCore(program.ToString(), optimization);
    }

    private static void RunCore(string program, int optimization)
    {
        SourceFile[] sources = [new("BinaryPrimitives.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Binary/BinaryPrimitives.gf"))), new("main.gf", program)];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "@malloc", "gflat$Memory$" }) Assert.DoesNotContain(forbidden, ir);
        RunNative(ir, optimization);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    public void SignedFileValuesMatchDotNetAndPreserveEndOfStreamAndIoFailures(int optimization, bool little)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-signed-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.bin"), target = Path.Combine(directory, "target.bin"), partial = Path.Combine(directory, "partial.bin");
            var expected = new List<byte>();
            string order = little ? "LittleEndian" : "BigEndian";
            var program = new StringBuilder($"using std; using std::Binary; int main(){{File input=File::OpenRead({Literal(source)}); File output=File::OpenWrite({Literal(target)});");
            foreach (int width in new[] { 2, 4, 8 })
            foreach (long original in Values)
            {
                long value = Truncate(original, width);
                string type = width == 2 ? "short" : width == 4 ? "int" : "long";
                expected.AddRange(Reference(value, width, little));
                program.Append($"if(FileBinary::ReadInt{width * 8}{order}(&input)!=({type}){Number(value)}) return 1;");
                program.Append($"FileBinary::WriteInt{width * 8}{order}(&output,({type}){Number(value)});");
            }
            foreach (int width in new[] { 2, 4, 8 })
            {
                program.Append($"try{{FileBinary::ReadInt{width * 8}{order}(&input);return 2;}}catch(EndOfStreamException* error){{}}");
                program.Append($"{{File truncated=File::OpenRead({Literal(partial)});try{{FileBinary::ReadInt{width * 8}{order}(&truncated);return 3;}}catch(EndOfStreamException* error){{}}}}");
            }
            program.Append("output.Close(); input.Close();");
            foreach (int width in new[] { 2, 4, 8 })
            {
                program.Append($"try{{FileBinary::ReadInt{width * 8}{order}(&input);return 4;}}catch(EndOfStreamException* error){{return 5;}}catch(IOException* error){{}}");
                program.Append($"try{{FileBinary::WriteInt{width * 8}{order}(&output,({(width == 2 ? "short" : width == 4 ? "int" : "long")})-1l);return 6;}}catch(IOException* error){{}}");
            }
            program.Append("return 0;}");
            File.WriteAllBytes(source, expected.ToArray()); File.WriteAllBytes(partial, [255]);
            var sources = new[] { "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "libc/IO/File.gf", "libc/IO/FileBinary.gf" }
                .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
                .Append(new SourceFile("main.gf", program.ToString()));
            RunNative(Compiler.Emit(sources), optimization);
            Assert.Equal(expected.ToArray(), File.ReadAllBytes(target));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void RunNative(string ir, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-signed-binary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "binary.ll"), output = Path.Combine(directory, "binary.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(output, []);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }
}

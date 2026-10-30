using System.Buffers.Binary;
using System.Text;

namespace gflat.Tests.Std.Binary;

public class BinaryReadAliasTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnsignedReadsComputeBeforeAssigningAnOverlappingOutput(int optimization)
    {
        var code = new StringBuilder("using std; using std::Binary; int main(){");
        foreach (int width in new[] { 2, 4, 8 })
        foreach (bool little in new[] { true, false })
        {
            byte[] bytes = Enumerable.Range(0, width).Select(i => (byte)(i * 29 + 131)).ToArray();
            ulong expected = width switch
            {
                2 => little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes),
                4 => little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes),
                _ => little ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes)
            };
            string type = width == 2 ? "ushort" : width == 4 ? "uint" : "ulong";
            string method = $"TryReadUInt{width * 8}{(little ? "Little" : "Big")}Endian";
            // The scalar is naturally aligned; only its byte view aliases input.
            code.Append($"{{ {type} value=({type})0; byte* bytes=(&value).reinterpret<byte*>();");
            for (int i = 0; i < width; i++) code.Append($"bytes[{i}]=(byte){bytes[i]};");
            code.Append($"if(!BinaryPrimitives::{method}(bytes,{width},ref value)||(ulong)value!={expected}ul)return 1;");
            // Failure zeroes the output even when that changes overlapping bytes.
            code.Append($"if(BinaryPrimitives::{method}(bytes,{width - 1},ref value)||value!=({type})0)return 2;}}");
        }
        code.Append("return 0;}");
        SourceFile[] sources = [new("BinaryPrimitives.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Binary/BinaryPrimitives.gf"))), new("main.gf", code.ToString())];
        Assert.Equal(0, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }
}

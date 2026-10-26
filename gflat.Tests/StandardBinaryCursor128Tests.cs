using System.Buffers.Binary;

namespace gflat.Tests;

public sealed class StandardBinaryCursor128Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FullWidthPacketsMatchDotNetBytesAndPreserveCursorBounds(int optimization)
    {
        Int128[] values = [0, 1, -1, Int128.MinValue, Int128.MaxValue,
            ((Int128)0x0123456789abcdeful << 64) | 0xfedcba9876543210ul];
        ulong expected = 14695981039346656037ul;
        foreach (Int128 value in values)
        {
            byte[] row = new byte[32];
            BinaryPrimitives.WriteInt128LittleEndian(row, value);
            BinaryPrimitives.WriteInt128BigEndian(row.AsSpan(16), value);
            foreach (byte b in row) expected = unchecked((expected ^ b) * 1099511628211ul);
        }
        Run($$"""
            using std; using std::Binary;
            int main() {
                extralong[6] values = [(extralong)0, (extralong)1, (extralong)-1,
                    (extralong)1 << 127, ((extralong)1 << 127) ^ (extralong)-1,
                    ((extralong)81985529216486895ul << 64) | (extralong)18364758544493064720ul];
                byte[36] bytes;
                ulong hash=14695981039346656037ul;
                for(nuint index=0;index<6;index++) {
                    for(nuint i=0;i<36;i++) bytes[i]=(byte)165;
                    BinaryBufferWriter writer=new(&bytes[1],34);
                    if(!writer.TryWriteByte((byte)7)
                        ||!writer.TryWriteExtraLongLittleEndian(values[index])
                        ||!writer.TryWriteExtraLongBigEndian(values[index])
                        ||!writer.TryWriteByte((byte)9)) return 1;
                    if(writer.Position!=34||writer.Remaining!=0||bytes[0]!=(byte)165||bytes[35]!=(byte)165) return 2;
                    for(nuint i=2;i<34;i++) hash=(hash^(ulong)bytes[i])*1099511628211ul;
                    BinaryBufferReader reader=new(&bytes[1],34);
                    byte tag=(byte)0; extralong value=(extralong)0;
                    if(!reader.TryReadByte(ref tag)||tag!=(byte)7
                        ||!reader.TryReadExtraLongLittleEndian(ref value)||value!=values[index]
                        ||!reader.TryReadExtraLongBigEndian(ref value)||value!=values[index]
                        ||!reader.TryReadByte(ref tag)||tag!=(byte)9) return 3;
                    if(reader.Position!=34||reader.Remaining!=0) return 4;
                    value=(extralong)99;
                    if(reader.TryReadExtraLongLittleEndian(ref value)||value!=(extralong)0||reader.Position!=34) return 5;
                    if(writer.TryWriteExtraLongBigEndian(values[index])||writer.Position!=34) return 6;
                }
                if(hash!={{expected}}ul) return 7;
                // A child cursor cannot consume the bytes following its bounded region.
                BinaryBufferWriter parent=new(bytes,36); BinaryBufferWriter child=new();
                if(!parent.TrySkip(1)||!parent.TryWriteSlice(16,ref child)||parent.Position!=17) return 8;
                if(!child.TryWriteExtraLongBigEndian(values[3])||child.TryWriteByte((byte)0)||child.Position!=16) return 9;
                BinaryBufferReader whole=new(bytes,36); BinaryBufferReader part=new();
                if(!whole.TrySkip(1)||!whole.TryReadSlice(16,ref part)||whole.Position!=17) return 10;
                extralong result=(extralong)0;
                if(!part.TryReadExtraLongBigEndian(ref result)||result!=values[3]||part.Position!=16) return 11;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShortRegionsAvoidPointerAccessAndReadsAllowAlignedOverlappingOutputs(int optimization)
        => Run("""
            using std; using std::Binary;
            int main() {
                byte[20] bytes;
                for(nuint count=0;count<16;count++) {
                    for(nuint i=0;i<20;i++) bytes[i]=(byte)165;
                    BinaryBufferWriter writer=new(&bytes[1],count+1);
                    if(!writer.TrySkip(1)) return 1;
                    if(writer.TryWriteExtraLongLittleEndian((extralong)-1)
                        ||writer.TryWriteExtraLongBigEndian((extralong)-1)||writer.Position!=1) return 2;
                    for(nuint i=0;i<20;i++) if(bytes[i]!=(byte)165) return 3;
                    BinaryBufferReader reader=new((readonly(byte)*)1,count);
                    extralong value=(extralong)99;
                    if(reader.TryReadExtraLongLittleEndian(ref value)||value!=(extralong)0||reader.Position!=0) return 4;
                    value=(extralong)99;
                    if(reader.TryReadExtraLongBigEndian(ref value)||value!=(extralong)0||reader.Position!=0) return 5;
                    writer=new((byte*)1,count);
                    if(writer.TryWriteExtraLongLittleEndian(value)||writer.TryWriteExtraLongBigEndian(value)) return 6;
                }
                extralong[3] aligned;
                byte* raw=(byte*)&aligned[0];
                extralong expected=((extralong)81985529216486895ul<<64)|(extralong)18364758544493064720ul;
                for(int order=0;order<2;order++) for(nuint offset=0;offset<32;offset++) {
                    BinaryBufferWriter writer=new(raw+offset,16);
                    bool success=false;
                    if(order==0) success=writer.TryWriteExtraLongLittleEndian(expected);
                    else success=writer.TryWriteExtraLongBigEndian(expected);
                    if(!success) return 7;
                    BinaryBufferReader reader=new(raw+offset,16);
                    if(order==0) success=reader.TryReadExtraLongLittleEndian(ref aligned[1]);
                    else success=reader.TryReadExtraLongBigEndian(ref aligned[1]);
                    if(!success||aligned[1]!=expected||reader.Position!=16) return 8;
                }
                BinaryBufferReader emptyRead=new(); BinaryBufferWriter emptyWrite=new();
                extralong value=(extralong)99;
                if(emptyRead.TryReadExtraLongBigEndian(ref value)||value!=(extralong)0) return 9;
                if(emptyWrite.TryWriteExtraLongLittleEndian((extralong)1)) return 10;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        SourceFile[] sources = new[] { "Binary/BinaryPrimitives.gf", "Binary/BinaryBufferReader.gf", "Binary/BinaryBufferWriter.gf", "Memory/bytes.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", program)).ToArray();
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true });
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "gflat$Memory$Allocate", "@malloc", "@free" })
            Assert.DoesNotContain(forbidden, ir);
        Assert.False(NativeToolchain.RequiresWideIntegerRuntime(ir));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}

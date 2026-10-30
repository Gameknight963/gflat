using System.Buffers.Binary;
using System.Text;

namespace gflat.Tests.Std.Binary;

public class BinaryCursorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LengthDelimitedPacketComposesBoundedSlicesAndMatchesDotNetBytes(int optimization)
    {
        byte[] expected = new byte[18]; expected[0] = 1; expected[17] = 0x7e;
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(1), 10);
        BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(3), -123456789);
        BinaryPrimitives.WriteUInt64BigEndian(expected.AsSpan(7), 0xfedcba9876543210ul);
        BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(15), -7);
        string checks = string.Join("\n", expected.Select((value, index) => $"if(data[{index + 1}]!=(byte){value})return 10;"));
        Run($$"""
            using Standard; using Standard::Binary;
            int main() {
                byte[20] data;
                for (int i=0;i<20;i++)data[i]=(byte)165;
                BinaryBufferWriter packet=new(&data[1],18);
                if(packet.Data!=&data[1])return 18;
                if(!packet.TryWriteByte((byte)1)||!packet.TryWriteUInt16BigEndian((ushort)10)||!packet.TryWriteInt32LittleEndian(-123456789))return 1;
                BinaryBufferWriter payload=new();
                if(!packet.TryWriteSlice(10,ref payload)||packet.Position!=17||packet.Remaining!=1||payload.Capacity!=10)return 2;
                if(payload.Data!=&data[8])return 19;
                if(!payload.TryWriteUInt64BigEndian(18364758544493064720ul)||!payload.TryWriteInt16LittleEndian((short)-7))return 3;
                if(payload.TryWriteByte((byte)0)||payload.Position!=10||payload.Remaining!=0)return 4;
                if(!packet.TryWriteByte((byte)126)||packet.Position!=18||packet.TryWriteByte((byte)0))return 5;
                if(data[0]!=(byte)165||data[19]!=(byte)165)return 6;
                {{checks}}
                BinaryBufferReader reader=new(&data[1],18);
                byte version=(byte)0;ushort size=0;int sequence=0;ulong wide=0ul;short tail=0;
                if(!reader.TryReadByte(ref version)||version!=(byte)1||!reader.TryReadUInt16BigEndian(ref size)||size!=(ushort)10)return 11;
                if(!reader.TryReadInt32LittleEndian(ref sequence)||sequence!=-123456789)return 12;
                BinaryBufferReader part=new();
                if(!reader.TryReadSlice((nuint)size,ref part)||part.Length!=10||part.Position!=0||reader.Position!=17)return 13;
                if(reader.Data!=&data[1]||part.Data!=&data[8])return 20;
                if(!part.TryReadUInt64BigEndian(ref wide)||wide!=18364758544493064720ul||!part.TryReadInt16LittleEndian(ref tail)||tail!=(short)-7)return 14;
                if(part.TryReadByte(ref version)||version!=(byte)0||part.Position!=10)return 15;
                if(!reader.TryReadByte(ref version)||version!=(byte)126||reader.Remaining!=0)return 16;
                if(reader.TryReadSlice(1,ref part)||part.Length!=0||part.Position!=0||reader.Position!=18)return 17;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryFixedWidthOperationAdvancesOnlyOnSuccessAndChecksCapacityBeforeWriting(int optimization)
    {
        var code = new StringBuilder("using Standard; using Standard::Binary; int main(){byte[12] bytes;");
        foreach (int width in new[] { 2, 4, 8 })
        foreach (bool signed in new[] { true, false })
        foreach (bool little in new[] { true, false })
        {
            string type = (width, signed) switch { (2, true) => "short", (2, false) => "ushort", (4, true) => "int", (4, false) => "uint", (8, true) => "long", _ => "ulong" };
            string method = $"{(signed ? "Int" : "UInt")}{width * 8}{(little ? "Little" : "Big")}Endian";
            byte[] expected = new byte[width];
            ulong value = 0xfedcba9876543210ul;
            if (width == 2) { if (little) BinaryPrimitives.WriteUInt16LittleEndian(expected, (ushort)value); else BinaryPrimitives.WriteUInt16BigEndian(expected, (ushort)value); }
            else if (width == 4) { if (little) BinaryPrimitives.WriteUInt32LittleEndian(expected, (uint)value); else BinaryPrimitives.WriteUInt32BigEndian(expected, (uint)value); }
            else { if (little) BinaryPrimitives.WriteUInt64LittleEndian(expected, value); else BinaryPrimitives.WriteUInt64BigEndian(expected, value); }
            code.Append($"{{ for(int i=0;i<12;i++)bytes[i]=(byte)165; BinaryBufferWriter writer=new(&bytes[1],{width-1});");
            code.Append($"if(writer.TryWrite{method}(({type}){value}ul)||writer.Position!=0||writer.Remaining!={width-1})return 1;");
            code.Append("for(int i=0;i<12;i++)if(bytes[i]!=(byte)165)return 2;");
            code.Append($"writer=new(&bytes[1],{width+1}); if(!writer.TrySkip(1)||!writer.TryWrite{method}(({type}){value}ul)||writer.Position!={width+1}||writer.Remaining!=0)return 3;");
            for (int i = 0; i < width; i++) code.Append($"if(bytes[{i+2}]!=(byte){expected[i]})return 4;");
            code.Append($"if(bytes[0]!=(byte)165||bytes[1]!=(byte)165||bytes[{width+2}]!=(byte)165)return 5;");
            code.Append($"BinaryBufferReader reader=new(&bytes[1],{width}); {type} value=({type})99;");
            code.Append($"if(!reader.TrySkip(1)||reader.TryRead{method}(ref value)||value!=({type})0||reader.Position!=1||reader.Remaining!={width-1})return 6;");
            code.Append($"reader=new(&bytes[1],{width+1}); if(!reader.TrySkip(1)||!reader.TryRead{method}(ref value)||value!=({type}){value}ul||reader.Position!={width+1}||reader.Remaining!=0)return 7;");
            code.Append($"if(reader.TryRead{method}(ref value)||value!=({type})0||reader.Position!={width+1})return 8;}}");
        }
        code.Append("return 0;}");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyResetCopiesSlicesAndMaximalSkipMaintainIndependentBoundedPositions(int optimization)
        => Run("""
            using Standard; using Standard::Binary;
            int main() {
                BinaryBufferReader emptyRead=new(); BinaryBufferWriter emptyWrite=new();
                if(emptyRead.Data!=null||emptyWrite.Data!=null)return 15;
                BinaryBufferReader readPart=new(); BinaryBufferWriter writePart=new();
                byte value=(byte)99;
                if(emptyRead.Length!=0||emptyRead.Position!=0||emptyRead.Remaining!=0||emptyRead.TryReadByte(ref value)||value!=(byte)0)return 1;
                if(emptyWrite.Capacity!=0||emptyWrite.Position!=0||emptyWrite.Remaining!=0||emptyWrite.TryWriteByte((byte)1))return 2;
                if(!emptyRead.TrySkip(0)||!emptyWrite.TrySkip(0)||!emptyRead.TryReadSlice(0,ref readPart)||!emptyWrite.TryWriteSlice(0,ref writePart))return 3;
                nuint maximum=(nuint)0-(nuint)1;
                if(emptyRead.TrySkip(maximum)||emptyWrite.TrySkip(maximum)||emptyRead.TryReadSlice(maximum,ref readPart)||emptyWrite.TryWriteSlice(maximum,ref writePart))return 4;
                byte[4] bytes=[(byte)1,(byte)2,(byte)3,(byte)4];
                BinaryBufferReader reader=new(bytes,4); BinaryBufferWriter writer=new(bytes,4);
                if(!reader.TrySkip(1)||!writer.TrySkip(1))return 5;
                BinaryBufferReader copied=reader; BinaryBufferWriter copiedWriter=writer;
                if(!copied.TryReadByte(ref value)||value!=(byte)2||reader.Position!=1)return 6;
                if(!copiedWriter.TryWriteByte((byte)9)||writer.Position!=1||bytes[1]!=(byte)9)return 7;
                if(reader.TrySkip(maximum)||writer.TrySkip(maximum)||reader.Position!=1||writer.Position!=1)return 8;
                if(!reader.TryReadSlice(2,ref readPart)||!writer.TryWriteSlice(2,ref writePart)||reader.Position!=3||writer.Position!=3)return 9;
                if(!writePart.TryWriteByte((byte)8)||!readPart.TryReadByte(ref value)||value!=(byte)8)return 10;
                if(reader.TryReadSlice(2,ref readPart)||writer.TryWriteSlice(2,ref writePart)||readPart.Length!=0||writePart.Capacity!=0||reader.Position!=3||writer.Position!=3)return 11;
                if(readPart.Data!=null||writePart.Data!=null)return 16;
                reader.Reset(); writer.Reset();
                if(reader.Position!=0||writer.Position!=0||reader.Remaining!=4||writer.Remaining!=4||bytes[1]!=(byte)8)return 12;
                if(!reader.TryReadByte(ref value)||value!=(byte)1)return 13;
                // Skipping/reserving does not initialize or otherwise modify storage.
                if(!writer.TrySkip(4)||bytes[0]!=(byte)1||bytes[1]!=(byte)8||bytes[2]!=(byte)3||bytes[3]!=(byte)4)return 14;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        SourceFile[] sources = new[] { "Binary/BinaryPrimitives.gf", "IO/IByteReader.gf", "Binary/BinaryBufferReader.gf", "Binary/BinaryBufferWriter.gf", "Memory/bytes.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", name))))
            .Append(new SourceFile("main.gf", program)).ToArray();
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true});
        foreach(string forbidden in new[]{"@__gflat_eh_","personality","gflat$Memory$Allocate","gflat$Memory$Free","@malloc","@free"}) Assert.DoesNotContain(forbidden,ir);
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}

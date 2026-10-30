namespace gflat.Tests.Std.Binary;

public sealed class BinaryBufferByteReaderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConcretePartialReadsMatchHostBytesWithoutAllocationOrExceptionRuntime(int optimization)
    {
        byte[] expected = Enumerable.Range(0, 257).Select(index => (byte)(index * 37 + 11)).ToArray();
        string code = $$"""
            using std::Binary;
            int main()nothrow{
                BinaryBufferReader empty=new();nuint maximum=(nuint)0-(nuint)1;
                if(empty.Read(((nuint)1).reinterpret<char*>(),0)!=0||empty.Read(((nuint)1).reinterpret<char*>(),maximum)!=0||empty.Position!=0)return 1;
                byte[257] expected=[{{string.Join(",", expected.Select(value => "(byte)" + value))}}];
                nuint[7] lengths=[0,1,2,7,255,256,257];nuint[4] requests=[1,2,17,512];
                for(nuint n=0;n<7;n++)for(nuint r=0;r<4;r++){
                    nuint count=lengths[n];BinaryBufferReader reader=new(expected,count);
                    if(reader.Read(((nuint)1).reinterpret<char*>(),0)!=0||reader.Position!=0||reader.Remaining!=count)return 2;
                    char[260] actual;for(nuint i=0;i<260;i++)actual[i]='?';nuint position=0;
                    while(true){
                        nuint before=reader.Remaining;nuint wanted=requests[r];if(wanted>before)wanted=before;
                        nuint received=reader.Read(&actual[1]+position,requests[r]);
                        if(received!=wanted)return 3;if(received==0)break;position+=received;
                        if(reader.Position!=position||reader.Remaining!=count-position)return 4;
                        if(reader.Read(((nuint)1).reinterpret<char*>(),0)!=0||reader.Position!=position)return 5;
                    }
                    if(position!=count||actual[0]!='?'||actual[count+1]!='?')return 6;
                    for(nuint i=0;i<count;i++)if((byte)actual[i+1]!=expected[i])return 7;
                    if(reader.Read(((nuint)1).reinterpret<char*>(),maximum)!=0||reader.Position!=count)return 8;
                    BinaryBufferReader duplicate=reader;
                    if(!reader.TrySeek(0)||!duplicate.TrySeek(count/2))return 9;
                    nuint copiedPosition=duplicate.Position;reader.Read(&actual[1],1);
                    if(duplicate.Position!=copiedPosition)return 10;
                    duplicate.Reset();if(duplicate.Position!=0)return 11;
                    if(count>=2){
                        if(!reader.TrySeek(count-2))return 12;
                        if(reader.TryReadBytes(((nuint)1).reinterpret<byte*>(),3)||reader.Position!=count-2)return 13;
                        if(reader.Read(&actual[1],3)!=2||reader.Position!=count)return 14;
                        if((byte)actual[1]!=expected[count-2]||(byte)actual[2]!=expected[count-1])return 15;
                    }
                }
                return 0;
            }
            """;
        string ir = Compiler.Emit(Sources(code, sharedHelpers: false), options: new() {
            NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "gflat$Memory$Allocate", "gflat$Memory$Free", "@malloc", "@free" })
            Assert.DoesNotContain(forbidden, ir);
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InterfaceBinaryVarIntAndExactReadHelpersShareTheExistingCursorAndCopyKeepsItsOwnPosition(int optimization)
    {
        string code = """
            using std::Binary;using std::IO;
            int main(){
                byte[12] data=[(byte)0x12,(byte)0x34,(byte)0x56,(byte)0x78,(byte)0xac,(byte)0x02,(byte)0x53,(byte)0xef,(byte)0xbe,(byte)0,(byte)255,(byte)90];
                BinaryBufferReader reader=new(data,12);IByteReader* input=&reader;
                if(std::IO::ReadUInt32BigEndian(input)!=305419896u||reader.Position!=4)return 21;
                if(std::IO::ReadVarUInt32(input)!=300u||reader.Position!=6)return 22;
                if(std::IO::ReadZigZagInt64(input)!=-42l||reader.Position!=7)return 23;
                ushort word=0;if(!reader.TryReadUInt16LittleEndian(ref word)||word!=(ushort)48879||reader.Position!=9)return 24;
                char[5] tail;for(nuint i=0;i<5;i++)tail[i]='?';std::IO::ReadExactly(input,&tail[1],3);
                if(reader.Position!=12||tail[0]!='?'||tail[1]!='\0'||(byte)tail[2]!=(byte)255||tail[3]!='Z'||tail[4]!='?')return 25;
                if(input.Read(((nuint)1).reinterpret<char*>(),1)!=0||input.Read(((nuint)1).reinterpret<char*>(),0)!=0)return 26;
                if(!reader.TrySeek(4)||std::IO::ReadVarUInt32(input)!=300u||reader.Position!=6)return 27;
                reader.Reset();if(std::IO::ReadUInt32BigEndian(input)!=305419896u||reader.Position!=4)return 28;
                BinaryBufferReader duplicate=reader;IByteReader* copied=&duplicate;
                if(std::IO::ReadVarUInt32(copied)!=300u||duplicate.Position!=6||reader.Position!=4)return 29;
                byte[2] raw;if(!reader.TryReadBytes(raw,2)||raw[0]!=(byte)0xac||raw[1]!=(byte)2||reader.Position!=6||duplicate.Position!=6)return 30;
                if(!reader.TrySeek(10))return 31;
                if(reader.TryReadBytes(((nuint)1).reinterpret<byte*>(),3)||reader.Position!=10)return 32;
                for(nuint i=0;i<5;i++)tail[i]='?';bool caught=false;
                try{std::IO::ReadExactly(input,&tail[1],3);}catch(ref EndOfStreamException error){caught=true;}
                if(!caught||reader.Position!=12||tail[0]!='?'||(byte)tail[1]!=(byte)255||tail[2]!='Z'||tail[3]!='?'||tail[4]!='?')return 33;
                if(duplicate.Position!=6)return 34;
                byte[12] original=[(byte)0x12,(byte)0x34,(byte)0x56,(byte)0x78,(byte)0xac,(byte)0x02,(byte)0x53,(byte)0xef,(byte)0xbe,(byte)0,(byte)255,(byte)90];
                for(nuint i=0;i<12;i++)if(data[i]!=original[i])return 35;
                return 0;
            }
            """;
        string ir = Compiler.Emit(Sources(code, sharedHelpers: true), options: new() {
            NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    private static IEnumerable<SourceFile> Sources(string code, bool sharedHelpers)
    {
        var names = new List<string> { "core/IO/IByteReader.gf", "core/Binary/BinaryPrimitives.gf",
            "core/Binary/BinaryBufferReader.gf", "core/Memory/bytes.gf" };
        if (sharedHelpers) names.AddRange(["core/Exception.gf", "core/ITextWriter.gf", "core/IO/IOException.gf",
            "core/IO/EndOfStreamException.gf", "core/IO/byte_reader.gf", "core/IO/byte_reader_binary.gf",
            "core/Binary/VarInt.gf", "core/IO/byte_reader_var_int.gf", "libc/Memory/allocation.gf"]);
        return names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("binary-buffer-byte-reader-tests.gf", code));
    }
}

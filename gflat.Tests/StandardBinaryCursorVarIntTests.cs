using System.Buffers.Binary;

namespace gflat.Tests;

public sealed class StandardBinaryCursorVarIntTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CanonicalWireBytesAndCursorBoundsMatchBinaryWriter(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-cursor-varint-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var values=new List<(bool Wide,ulong Value)>();
            foreach(ulong value in new ulong[]{0,1,127,128,255,300,16383,16384,2097151,2097152,uint.MaxValue})values.Add((false,value));
            foreach(ulong value in new ulong[]{0,1,127,128,300,16383,16384,1ul<<21,1ul<<28,1ul<<35,1ul<<42,1ul<<49,1ul<<56,1ul<<63,ulong.MaxValue})values.Add((true,value));
            var random=new Random(59172);byte[] bytes=new byte[8];
            for(int i=0;i<64;i++){random.NextBytes(bytes);ulong value=BinaryPrimitives.ReadUInt64BigEndian(bytes);values.Add((false,(uint)value));values.Add((true,value));}
            string path=Path.Combine(directory,"wire.bin");
            using(var output=File.Create(path))foreach(var item in values)
            {
                using var buffer=new MemoryStream();
                using(var writer=new BinaryWriter(buffer,System.Text.Encoding.UTF8,leaveOpen:true)){
                    if(item.Wide)writer.Write7BitEncodedInt64(unchecked((long)item.Value));
                    else writer.Write7BitEncodedInt(unchecked((int)(uint)item.Value));
                }
                byte[] encoded=buffer.ToArray();
                output.WriteByte(item.Wide?(byte)1:(byte)0);BinaryPrimitives.WriteUInt64BigEndian(bytes,item.Value);output.Write(bytes);
                output.WriteByte((byte)encoded.Length);output.Write(encoded);output.Write(new byte[10-encoded.Length]);
            }
            Run($$"""
                using std;extern void* fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* output,nuint size,nuint count,void* file);extern int fclose(void* file);
                int allocations=0;
                namespace Memory{public replace void*? Allocate(nuint size){allocations++;return null;}public replace void Free(void* pointer){} }
                int main(){void* file=fopen({{Literal(path)}},"rb");defer{fclose(file);}
                    byte[20] record;byte[14] data;int records=0;
                    while(fread((void*)&record[0],1,20,file)==20){
                        bool wide=record[0]!=(byte)0;ulong expected=0ul;
                        for(nuint i=0;i<8;i++)expected=(expected<<8)|(ulong)record[i+1];
                        nuint length=(nuint)record[9];
                        for(nuint capacity=0;capacity<length;capacity++){
                            for(nuint i=0;i<14;i++)data[i]=(byte)204;
                            BinaryBufferWriter shortWriter=new(&data[1],capacity+1);shortWriter.TrySkip(1);
                            bool success=false;if(wide)success=shortWriter.TryWriteVarUInt64(expected);else success=shortWriter.TryWriteVarUInt32((uint)expected);
                            if(success||shortWriter.Position!=1)return 1;
                            for(nuint i=0;i<14;i++)if(data[i]!=(byte)204)return 2;
                        }
                        for(nuint i=0;i<14;i++)data[i]=(byte)204;
                        BinaryBufferWriter writer=new(&data[1],length+1);writer.TrySkip(1);
                        bool success=false;if(wide)success=writer.TryWriteVarUInt64(expected);else success=writer.TryWriteVarUInt32((uint)expected);
                        if(!success||writer.Position!=length+1||writer.Remaining!=0)return 3;
                        if(data[0]!=(byte)204||data[1]!=(byte)204)return 4;
                        for(nuint i=0;i<length;i++)if(data[i+2]!=record[i+10])return 5;
                        for(nuint i=length+2;i<14;i++)if(data[i]!=(byte)204)return 6;
                        BinaryBufferReader reader=new(&data[1],length+1);reader.TrySkip(1);uint small=77u;ulong big=77ul;
                        VarInt::Status status=VarInt::Status::Invalid;
                        if(wide)status=reader.ReadVarUInt64(ref big);else status=reader.ReadVarUInt32(ref small);
                        if(status!=VarInt::Status::Done||reader.Position!=length+1)return 7;
                        if(wide){if(big!=expected)return 8;}else if(small!=(uint)expected)return 9;
                        if(wide)status=reader.ReadVarUInt64(ref big);else status=reader.ReadVarUInt32(ref small);
                        if(status!=VarInt::Status::NeedMore||reader.Position!=length+1)return 10;
                        if(wide){if(big!=expected)return 14;}else if(small!=(uint)expected)return 15;
                        for(nuint cut=0;cut<length;cut++){
                            BinaryBufferReader prefix=new(&data[1],cut+1);prefix.TrySkip(1);small=77u;big=77ul;
                            if(wide)status=prefix.ReadVarUInt64(ref big);else status=prefix.ReadVarUInt32(ref small);
                            if(status!=VarInt::Status::NeedMore||prefix.Position!=1||small!=77u||big!=77ul)return 11;
                        }
                        for(nuint i=0;i<length;i++)if(data[i+2]!=record[i+10])return 12;
                        records++;
                    }
                    if(records!={{values.Count}}||allocations!=0)return 13;return 0;
                }
                """,optimization);
        }
        finally{Directory.Delete(directory,true);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyInvalidAndAliasedResultsRespectCommitRules(int optimization)
        => Run("""
            using std;
            int main(){
                BinaryBufferReader empty=new();BinaryBufferWriter writer=new();uint small=42u;ulong big=43ul;
                if(empty.ReadVarUInt32(ref small)!=VarInt::Status::NeedMore||small!=42u||empty.Position!=0)return 1;
                if(empty.ReadVarUInt64(ref big)!=VarInt::Status::NeedMore||big!=43ul||empty.Position!=0)return 2;
                if(writer.TryWriteVarUInt32(0u)||writer.TryWriteVarUInt64(0ul)||writer.Position!=0)return 3;
                BinaryBufferReader inaccessible=new((readonly(byte)*)1,0);BinaryBufferWriter noStorage=new((byte*)1,0);
                if(inaccessible.ReadVarUInt32(ref small)!=VarInt::Status::NeedMore||inaccessible.ReadVarUInt64(ref big)!=VarInt::Status::NeedMore)return 4;
                if(noStorage.TryWriteVarUInt32(1u)||noStorage.TryWriteVarUInt64(1ul))return 5;
                byte[11] invalid;invalid[0]=(byte)99;invalid[1]=(byte)128;invalid[2]=(byte)0;
                BinaryBufferReader overlong=new(invalid,3);overlong.TrySkip(1);
                if(overlong.ReadVarUInt32(ref small)!=VarInt::Status::Invalid||small!=42u||overlong.Position!=1)return 6;
                if(overlong.ReadVarUInt64(ref big)!=VarInt::Status::Invalid||big!=43ul||overlong.Position!=1)return 7;
                for(nuint i=1;i<11;i++)invalid[i]=(byte)255;invalid[5]=(byte)16;
                BinaryBufferReader overflow32=new(invalid,6);overflow32.TrySkip(1);
                if(overflow32.ReadVarUInt32(ref small)!=VarInt::Status::Invalid||small!=42u||overflow32.Position!=1)return 8;
                invalid[5]=(byte)255;invalid[10]=(byte)2;BinaryBufferReader overflow64=new(invalid,11);overflow64.TrySkip(1);
                if(overflow64.ReadVarUInt64(ref big)!=VarInt::Status::Invalid||big!=43ul||overflow64.Position!=1)return 9;
                uint[3] aligned;byte* bytes=(byte*)&aligned[0];for(nuint i=0;i<12;i++)bytes[i]=(byte)0;
                bytes[0]=(byte)172;bytes[1]=(byte)2;BinaryBufferReader input=new(bytes,2);
                if(input.ReadVarUInt32(ref aligned[0])!=VarInt::Status::Done||aligned[0]!=300u||input.Position!=2)return 10;
                bytes[0]=(byte)128;bytes[1]=(byte)0;uint before=aligned[0];input.Reset();
                if(input.ReadVarUInt32(ref aligned[0])!=VarInt::Status::Invalid||aligned[0]!=before||input.Position!=0)return 11;
                ulong[2] aligned64;byte* bytes64=(byte*)&aligned64[0];for(nuint i=0;i<16;i++)bytes64[i]=(byte)0;
                for(nuint i=0;i<9;i++)bytes64[i]=(byte)255;bytes64[9]=(byte)1;
                BinaryBufferReader wide=new(bytes64,10);
                if(wide.ReadVarUInt64(ref aligned64[0])!=VarInt::Status::Done||aligned64[0]!=18446744073709551615ul||wide.Position!=10)return 12;
                bytes64[0]=(byte)128;bytes64[1]=(byte)0;ulong previous=aligned64[0];BinaryBufferReader bad=new(bytes64,2);
                if(bad.ReadVarUInt64(ref aligned64[0])!=VarInt::Status::Invalid||aligned64[0]!=previous||bad.Position!=0)return 13;
                return 0;
            }
            """,optimization);

    [Fact]
    public void OptionalIntegrationIsNothrowWithoutAllocatorOrExceptionContracts()
    {
        string ir=Compiler.Emit(Sources("""
            using std;
            bool Roundtrip(ref BinaryBufferReader reader,ref BinaryBufferWriter writer,ref ulong value)nothrow{
                if(reader.ReadVarUInt64(ref value)!=VarInt::Status::Done)return false;
                return writer.TryWriteVarUInt64(value);
            }
            """),options:new(){NoStdLib=true,NoBuiltins=true});
        foreach(string forbidden in new[]{"@__gflat_eh_","personality","gflat$Memory$Allocate","gflat$Memory$Free","@malloc","@free"})Assert.DoesNotContain(forbidden,ir);
    }

    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static IEnumerable<SourceFile> Sources(string code)
        => new[]{"BinaryPrimitives.gf","BinaryBufferReader.gf","BinaryBufferWriter.gf","Memory/Bytes.gf","VarInt.gf","BinaryBufferVarInt.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("cursor-varint.gf",code));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}

using System.Buffers.Binary;
using System.Numerics;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardBinaryCursorZigZagTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SignedWireBytesAndAllCapacitiesMatchIndependentArithmeticOracle(int optimization)
    {
        var rows=new List<(bool Wide,long Value)>();
        foreach(long value in new long[]{int.MinValue,int.MinValue+1,-8193,-8192,-8191,-65,-64,-63,-2,-1,0,1,2,63,64,65,8191,8192,8193,int.MaxValue-1,int.MaxValue})
            rows.Add((false,value));
        foreach(long value in new long[]{long.MinValue,long.MinValue+1,int.MinValue,-8193,-8192,-65,-64,-1,0,1,63,64,8191,8192,int.MaxValue,long.MaxValue-1,long.MaxValue})
            rows.Add((true,value));
        for(int shift=6;shift<63;shift+=7)
            foreach(long delta in new long[]{-1,0,1})
            {
                long value=(1L<<shift)+delta;rows.Add((true,value));rows.Add((true,-value));
                if(value<=int.MaxValue){rows.Add((false,value));rows.Add((false,-value));}
            }
        var random=new Random(32764128);byte[] randomBytes=new byte[8];
        for(int sample=0;sample<80;sample++)
        {
            random.NextBytes(randomBytes);long value=BinaryPrimitives.ReadInt64LittleEndian(randomBytes);
            rows.Add((true,value));rows.Add((false,unchecked((int)value)));
        }
        await WithDirectory(directory=>
        {
            string manifest=Path.Combine(directory,"signed.bin");
            using(var writer=new BinaryWriter(File.Create(manifest)))
                foreach(var row in rows)
                {
                    byte[] encoded=Encode(row.Value);
                    writer.Write(row.Wide?(byte)1:(byte)0);writer.Write(row.Value);writer.Write((byte)encoded.Length);
                    writer.Write(encoded);writer.Write(new byte[10-encoded.Length]);
                }
            Run(directory,$$"""
                {{NoAllocation}}
                extern void* fopen(readonly(char)* path,readonly(char)* mode)nothrow;
                extern nuint fread(void* output,nuint size,nuint count,void* file)nothrow;
                extern int fclose(void* file)nothrow;
                int main(int argc,char** argv){
                    void* file=fopen(argv[1],"rb");defer{fclose(file);}
                    byte[20] record;byte[16] data;byte[14] source;int records=0;
                    while(fread((void*)&record[0],1,20,file)==20){
                        bool wide=record[0]!=0;ulong bits=0ul;
                        for(int i=7;i>=0;i--)bits=(bits<<8)|(ulong)record[i+1];
                        long expected=(long)bits;nuint length=(nuint)record[9];
                        for(nuint capacity=0;capacity<=12;capacity++){
                            for(nuint i=0;i<16;i++)data[i]=204;
                            BinaryBufferWriter writer=new(&data[1],capacity+1);writer.TrySkip(1);
                            bool success=false;
                            if(wide)success=writer.TryWriteZigZagInt64(expected);else success=writer.TryWriteZigZagInt32((int)expected);
                            if(success!=(capacity>=length))return 1;
                            if(!success){if(writer.Position!=1||writer.Remaining!=capacity)return 2;
                                for(nuint i=0;i<16;i++)if(data[i]!=204)return 3;}
                            else{if(writer.Position!=length+1||writer.Remaining!=capacity-length||data[0]!=204||data[1]!=204)return 4;
                                for(nuint i=0;i<length;i++)if(data[i+2]!=record[i+10])return 5;
                                for(nuint i=length+2;i<16;i++)if(data[i]!=204)return 6;}
                        }
                        for(nuint i=0;i<14;i++)source[i]=204;
                        for(nuint i=0;i<length;i++)source[i+2]=record[i+10];
                        BinaryBufferReader reader=new(&source[1],length+2);reader.TrySkip(1);int small=77;long big=77l;
                        VarInt::Status status=VarInt::Status::Invalid;
                        if(wide)status=reader.ReadZigZagInt64(ref big);else status=reader.ReadZigZagInt32(ref small);
                        if(status!=VarInt::Status::Done||reader.Position!=length+1||reader.Remaining!=1)return 7;
                        if(wide){if(big!=expected)return 8;}else if(small!=(int)expected)return 9;
                        for(nuint cut=0;cut<length;cut++){
                            BinaryBufferReader prefix=new(&source[1],cut+1);prefix.TrySkip(1);small=77;big=77l;
                            if(wide)status=prefix.ReadZigZagInt64(ref big);else status=prefix.ReadZigZagInt32(ref small);
                            if(status!=VarInt::Status::NeedMore||prefix.Position!=1||prefix.Remaining!=cut||small!=77||big!=77l)return 10;
                        }
                        if(source[0]!=204||source[1]!=204)return 11;
                        for(nuint i=0;i<length;i++)if(source[i+2]!=record[i+10])return 12;
                        for(nuint i=length+2;i<14;i++)if(source[i]!=204)return 13;
                        records++;
                    }
                    if(records!={{rows.Count}}||allocations!=0)return 14;return 0;
                }
                """,[manifest],optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EmptyTruncatedOverflowAndNoncanonicalInputsPreserveCursorAndDestination(int optimization)
        => await WithDirectory(directory=>Run(directory,NoAllocation+"""
            int main(){
                BinaryBufferReader empty=new();BinaryBufferWriter writer=new();int small=-73;long big=-79l;
                if(empty.ReadZigZagInt32(ref small)!=VarInt::Status::NeedMore||small!=-73||empty.Position!=0)return 1;
                if(empty.ReadZigZagInt64(ref big)!=VarInt::Status::NeedMore||big!=-79l||empty.Position!=0)return 2;
                if(writer.TryWriteZigZagInt32(0)||writer.TryWriteZigZagInt64(0l)||writer.Position!=0)return 3;
                BinaryBufferReader fake=new((readonly(byte)*)1,0);BinaryBufferWriter noStorage=new((byte*)1,0);
                if(fake.ReadZigZagInt32(ref small)!=VarInt::Status::NeedMore||fake.ReadZigZagInt64(ref big)!=VarInt::Status::NeedMore)return 4;
                if(small!=-73||big!=-79l||fake.Position!=0||noStorage.TryWriteZigZagInt32(-1)||noStorage.TryWriteZigZagInt64(-1l)||noStorage.Position!=0)return 5;
                byte[12] data;
                for(int kind=0;kind<6;kind++){
                    for(nuint i=0;i<12;i++)data[i]=204;data[1]=128;data[2]=0;nuint count=2;
                    if(kind==1)data[1]=129;
                    if(kind==2){for(nuint i=1;i<=5;i++)data[i]=128;data[5]=0;count=5;}
                    if(kind==3){for(nuint i=1;i<=5;i++)data[i]=255;data[5]=16;count=5;}
                    if(kind==4){for(nuint i=1;i<=5;i++)data[i]=255;count=5;}
                    if(kind==5){data[1]=128;data[2]=128;data[3]=128;count=3;}
                    BinaryBufferReader reader=new(data,count+1);reader.TrySkip(1);
                    VarInt::Status status=reader.ReadZigZagInt32(ref small);
                    if(kind==5){if(status!=VarInt::Status::NeedMore)return 6;}
                    else if(status!=VarInt::Status::Invalid)return 7;
                    if(reader.Position!=1||reader.Remaining!=count||small!=-73)return 8;
                }
                for(int kind=0;kind<6;kind++){
                    for(nuint i=0;i<12;i++)data[i]=204;data[1]=128;data[2]=0;nuint count=2;
                    if(kind==1)data[1]=129;
                    if(kind==2){for(nuint i=1;i<=10;i++)data[i]=128;data[10]=0;count=10;}
                    if(kind==3){for(nuint i=1;i<=10;i++)data[i]=255;data[10]=2;count=10;}
                    if(kind==4){for(nuint i=1;i<=10;i++)data[i]=255;count=10;}
                    if(kind==5){for(nuint i=1;i<=9;i++)data[i]=128;count=9;}
                    BinaryBufferReader reader=new(data,count+1);reader.TrySkip(1);
                    VarInt::Status status=reader.ReadZigZagInt64(ref big);
                    if(kind==5){if(status!=VarInt::Status::NeedMore)return 9;}
                    else if(status!=VarInt::Status::Invalid)return 10;
                    if(reader.Position!=1||reader.Remaining!=count||big!=-79l)return 11;
                }
                if(allocations!=0)return 12;return 0;
            }
            """,[],optimization));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AlignedValueAliasesBorrowedBytesWithSeparateCursorMetadata(int optimization)
        => await WithDirectory(directory=>Run(directory,NoAllocation+"""
            int main(){
                int[4] aligned;byte* bytes=(byte*)&aligned[0];byte[16] saved;
                for(nuint i=0;i<16;i++)bytes[i]=204;bytes[0]=173;bytes[1]=2;
                BinaryBufferReader input=new(bytes,2);
                if(input.ReadZigZagInt32(ref aligned[0])!=VarInt::Status::Done||aligned[0]!=-151||input.Position!=2)return 1;
                bytes[0]=128;bytes[1]=0;int before=aligned[0];input.Reset();
                if(input.ReadZigZagInt32(ref aligned[0])!=VarInt::Status::Invalid||aligned[0]!=before||input.Position!=0)return 2;
                aligned[0]=-151;for(nuint i=0;i<16;i++)saved[i]=bytes[i];
                BinaryBufferWriter output=new(bytes,5);
                if(!output.TryWriteZigZagInt32(aligned[0])||output.Position!=2||bytes[0]!=173||bytes[1]!=2)return 3;
                for(nuint i=2;i<16;i++)if(bytes[i]!=saved[i])return 4;
                long[2] aligned64;byte* wideBytes=(byte*)&aligned64[0];
                for(nuint i=0;i<16;i++)wideBytes[i]=204;
                for(nuint i=0;i<9;i++)wideBytes[i]=255;wideBytes[9]=1;
                long minimum=-9223372036854775807l-1l;BinaryBufferReader wide=new(wideBytes,10);
                if(wide.ReadZigZagInt64(ref aligned64[0])!=VarInt::Status::Done||aligned64[0]!=minimum||wide.Position!=10)return 5;
                wideBytes[0]=128;wideBytes[1]=0;long previous=aligned64[0];BinaryBufferReader bad=new(wideBytes,2);
                if(bad.ReadZigZagInt64(ref aligned64[0])!=VarInt::Status::Invalid||aligned64[0]!=previous||bad.Position!=0)return 6;
                aligned64[0]=minimum;for(nuint i=0;i<16;i++)saved[i]=wideBytes[i];
                BinaryBufferWriter wideOutput=new(wideBytes,10);
                if(!wideOutput.TryWriteZigZagInt64(aligned64[0])||wideOutput.Position!=10)return 7;
                for(nuint i=0;i<9;i++)if(wideBytes[i]!=255)return 8;if(wideBytes[9]!=1)return 9;
                for(nuint i=10;i<16;i++)if(wideBytes[i]!=saved[i])return 10;
                if(allocations!=0)return 11;return 0;
            }
            """,[],optimization));

    [Fact]
    public void SelectiveSignedCursorContractUsesNothrowCallsWithoutAllocatorOrExceptionServices()
    {
        string ir=Emit("""
            using std;
            bool Convert(ref BinaryBufferReader reader,ref BinaryBufferWriter writer,ref int small,ref long big)nothrow{
                VarInt::Status a=reader.ReadZigZagInt32(ref small);VarInt::Status b=reader.ReadZigZagInt64(ref big);
                bool c=writer.TryWriteZigZagInt32(small);bool d=writer.TryWriteZigZagInt64(big);
                return a==VarInt::Status::Done&&b==VarInt::Status::Done&&c&&d;
            }
            """);
        foreach(string forbidden in new[]{"@__gflat_eh_","personality","gflat$Memory$Allocate","gflat$Memory$Free","@malloc","@free"})
            Assert.DoesNotContain(forbidden,ir);
        string body=Regex.Match(ir,@"(?ms)^define [^\r\n]*@gflat\$Convert\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);Assert.DoesNotContain("invoke",body);
        foreach(string name in new[]{"ReadZigZagInt32","ReadZigZagInt64","TryWriteZigZagInt32","TryWriteZigZagInt64"})
        {
            string definition=Assert.Single(ir.Split('\n'),line=>line.StartsWith("define ",StringComparison.Ordinal)&&line.Contains("$"+name+"(",StringComparison.Ordinal));
            Assert.Contains(" nounwind",definition);Assert.Contains("\"no-builtins\"",definition);Assert.Contains("$"+name+"(",body);
        }
    }

    private static byte[] Encode(long signed)
    {
        // Arithmetic definition handles signed minima without overflowing a host signed integer.
        BigInteger value=signed;value=value.Sign<0?-value*2-1:value*2;var bytes=new List<byte>();
        do{value=BigInteger.DivRem(value,128,out BigInteger digit);bytes.Add((byte)((int)digit+(value.IsZero?0:128)));}while(!value.IsZero);
        return bytes.ToArray();
    }
    private const string NoAllocation="""
        using std;
        int allocations=0;
        namespace Memory{public void*? Allocate(nuint count){allocations++;return null;}public void Free(void* value)nothrow{}}
        """;
    private static string Emit(string program)
    {
        string[] names=["Binary/BinaryPrimitives.gf","Binary/BinaryBufferReader.gf","Binary/BinaryBufferWriter.gf","Memory/Bytes.gf","Binary/VarInt.gf","Binary/binary_buffer_var_int.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("signed-cursor.gf",program));
        return Compiler.Emit(sources,options:new(){NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
    }
    private static async Task WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-cursor-zigzag-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);try{action(directory);}finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }
    private static void Run(string directory,string program,string[] arguments,int optimization)
    {
        string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");File.WriteAllText(ir,Emit(program));
        var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
        var result=NativeToolchain.Run(executable,arguments);Assert.True(result.ExitCode==0,$"Signed cursor exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

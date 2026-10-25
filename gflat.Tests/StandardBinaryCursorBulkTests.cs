namespace gflat.Tests;

public sealed class StandardBinaryCursorBulkTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverlappingCopiesMatchArrayCopyAcrossEveryOffsetAndLength(int optimization)
    {
        ulong expected=14695981039346656037ul;
        for(int mode=0;mode<2;mode++)for(int source=0;source<16;source++)for(int destination=0;destination<16;destination++)
        for(int count=0;count<=16-Math.Max(source,destination);count++)
        {
            byte[] bytes=Enumerable.Range(0,18).Select(i=>(byte)(i*13+7)).ToArray();
            Array.Copy(bytes,source+1,bytes,destination+1,count);
            foreach(byte value in bytes)expected=unchecked((expected^value)*1099511628211ul);
        }
        Run($$"""
            using std; using std::Binary;
            int main(){ulong hash=14695981039346656037ul;byte[18] bytes;byte* buffer=bytes;
                for(int mode=0;mode<2;mode++)for(nuint source=0;source<16;source++)for(nuint destination=0;destination<16;destination++){
                    nuint limit=source;if(destination>limit)limit=destination;
                    for(nuint count=0;count<=16-limit;count++){
                        for(nuint i=0;i<18;i++)bytes[i]=(byte)(i*13+7);
                        if(mode==0){BinaryBufferReader reader=new(buffer+1,16);
                            if(!reader.TrySkip(source)||!reader.TryReadBytes(buffer+1+destination,count)||reader.Position!=source+count)return 1;
                        }else{BinaryBufferWriter writer=new(buffer+1,16);
                            if(!writer.TrySkip(destination)||!writer.TryWriteBytes(buffer+1+source,count)||writer.Position!=destination+count)return 2;}
                        for(nuint i=0;i<18;i++)hash=(hash^(ulong)bytes[i])*1099511628211ul;
                    }
                }
                if(hash!={{expected}}ul)return 3;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BoundsFailuresAndZeroCountsDoNotAccessPointersOrChangeBytes(int optimization)
        => Run("""
            using std; using std::Binary;
            int main(){
                BinaryBufferReader emptyRead=new();BinaryBufferWriter emptyWrite=new();
                if(!emptyRead.TryReadBytes((byte*)1,0)||!emptyWrite.TryWriteBytes((readonly(byte)*)1,0))return 1;
                nuint maximum=(nuint)0-(nuint)1;
                if(emptyRead.TryReadBytes((byte*)1,maximum)||emptyWrite.TryWriteBytes((readonly(byte)*)1,maximum))return 2;
                byte[6] data=[(byte)10,(byte)20,(byte)30,(byte)40,(byte)50,(byte)60];
                BinaryBufferReader reader=new(data,6);BinaryBufferWriter writer=new(data,6);
                if(!reader.TrySkip(4)||!writer.TrySkip(4))return 3;
                if(reader.TryReadBytes((byte*)1,3)||writer.TryWriteBytes((readonly(byte)*)1,3))return 4;
                if(reader.TryReadBytes((byte*)1,maximum)||writer.TryWriteBytes((readonly(byte)*)1,maximum))return 5;
                if(reader.Position!=4||writer.Position!=4)return 6;
                for(nuint i=0;i<6;i++)if(data[i]!=(byte)((i+1)*10))return 7;
                BinaryBufferReader copiedRead=reader;BinaryBufferWriter copiedWrite=writer;
                byte[2] output;if(!copiedRead.TryReadBytes(output,2)||output[0]!=(byte)50||output[1]!=(byte)60)return 8;
                if(!copiedWrite.TryWriteBytes(output,2)||reader.Position!=4||writer.Position!=4)return 9;
                if(copiedRead.Position!=6||copiedWrite.Position!=6)return 10;
                if(!copiedRead.TryReadBytes((byte*)1,0)||!copiedWrite.TryWriteBytes((readonly(byte)*)1,0))return 11;
                reader.Reset();writer.Reset();if(reader.Position!=0||writer.Position!=0)return 12;
                return 0;
            }
            """,optimization);

    private static void Run(string code,int optimization)
    {
        var sources=new[]{"Binary/BinaryPrimitives.gf","Binary/BinaryBufferReader.gf","Binary/BinaryBufferWriter.gf","Memory/Bytes.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("main.gf",code)).ToArray();
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,NoBuiltins=true});
        foreach(string forbidden in new[]{"@__gflat_eh_","personality","gflat$Memory$Allocate","gflat$Memory$Free","@malloc","@free"})
            Assert.DoesNotContain(forbidden,ir);
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}

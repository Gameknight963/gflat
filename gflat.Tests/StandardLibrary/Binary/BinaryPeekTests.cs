using System.Text.RegularExpressions;

namespace gflat.Tests.Std.Binary;

public sealed class BinaryPeekTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EveryByteAndCursorPositionMatchesHostBytesWithoutAdvancingOrChangingSharedInput(int optimization)
    {
        byte[] values=Enumerable.Range(0,256).Select(value=>(byte)value).ToArray();
        new Random(71942).Shuffle(values.AsSpan());
        string oracle=string.Join(',',values.Select(value=>"(byte)"+value));
        await Run($$"""
            using std; using std::Binary;
            int main(){
                byte[256] oracle=[{{oracle}}];byte[258] data;
                data[0]=(byte)165;data[257]=(byte)90;
                for(nuint i=0;i<256;i++)data[i+1]=oracle[i];
                readonly(byte)* shared=&data[1];
                BinaryBufferReader reader=new(shared,256);BinaryBufferReader view=new(shared,256);
                for(nuint i=0;i<256;i++){
                    for(int iteration=0;iteration<3;iteration++){
                        byte value=(byte)99;
                        if(!reader.TryPeekByte(ref value)||value!=oracle[i])return 1;
                        if(reader.Position!=i||reader.Length!=256||reader.Remaining!=256-i||reader.Data!=shared)return 2;
                    }
                    BinaryBufferReader copy=reader;byte copied=(byte)99;
                    if(!copy.TryReadByte(ref copied)||copied!=oracle[i]||copy.Position!=i+1||reader.Position!=i)return 3;
                    if(!view.TrySkip(i-view.Position))return 4;byte viewed=(byte)99;
                    if(!view.TryPeekByte(ref viewed)||viewed!=oracle[i]||view.Position!=i)return 5;
                    if(i%3==0){byte read=(byte)99;if(!reader.TryReadByte(ref read)||read!=oracle[i])return 6;}
                    else if(!reader.TrySkip(1))return 7;
                }
                byte value=(byte)99;
                if(reader.TryPeekByte(ref value)||value!=(byte)0||reader.Position!=256||reader.Remaining!=0)return 8;
                value=(byte)99;if(reader.TryPeekByte(ref value)||value!=(byte)0||reader.Position!=256)return 9;
                reader.Reset();if(!reader.TryPeekByte(ref value)||value!=oracle[0]||reader.Position!=0)return 10;
                int[5] starts=[0,1,127,255,256];
                for(nuint row=0;row<5;row++){
                    nuint start=(nuint)starts[row];reader.Reset();if(!reader.TrySkip(start))return 11;
                    nuint length=256-start;if(length>3)length=3;BinaryBufferReader slice=new();
                    if(!reader.TryReadSlice(length,ref slice)||reader.Position!=start+length)return 12;
                    for(nuint i=0;i<length;i++){
                        value=(byte)99;if(!slice.TryPeekByte(ref value)||value!=oracle[start+i]||slice.Position!=i)return 13;
                        value=(byte)99;if(!slice.TryReadByte(ref value)||value!=oracle[start+i])return 14;
                    }
                    value=(byte)99;if(slice.TryPeekByte(ref value)||value!=(byte)0||slice.Position!=length)return 15;
                    if(reader.Position!=start+length)return 16;
                }
                for(nuint i=0;i<256;i++)if(data[i+1]!=oracle[i])return 17;
                if(data[0]!=(byte)165||data[257]!=(byte)90)return 18;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EmptyAndEndZeroOutputWithoutReadingAndProtectedReceiversRemainValidObservers(int optimization)
        =>await Run("""
            using std; using std::Binary;
            bool ReadOnly(readonly(BinaryBufferReader)* reader,ref byte value) nothrow=>reader.TryPeekByte(ref value);
            bool DeepImmutable(immutable(BinaryBufferReader)* reader,ref byte value) nothrow=>reader.TryPeekByte(ref value);
            int main(){
                BinaryBufferReader empty=new();byte value=(byte)99;
                if(empty.TryPeekByte(ref value)||value!=(byte)0||empty.Data!=null||empty.Position!=0||empty.Remaining!=0)return 1;
                BinaryBufferReader fake=new((readonly(byte)*)1,0);value=(byte)99;
                if(fake.TryPeekByte(ref value)||value!=(byte)0||fake.Position!=0||fake.Length!=0||fake.Data!=(readonly(byte)*)1)return 2;
                if(!fake.TrySkip(0)||fake.TrySkip((nuint)0-1))return 3;
                fake.Reset();value=(byte)99;if(fake.TryPeekByte(ref value)||value!=(byte)0)return 4;
                byte[2] data=[(byte)255,(byte)0];BinaryBufferReader reader=new(data,2);
                readonly(BinaryBufferReader) readOnly=reader;immutable(BinaryBufferReader) immutableView=reader;
                value=(byte)99;if(!ReadOnly(&readOnly,ref value)||value!=(byte)255||readOnly.Position!=0)return 5;
                value=(byte)99;if(!DeepImmutable(&immutableView,ref value)||value!=(byte)255||immutableView.Position!=0)return 6;
                BinaryBufferReader copied=reader;if(!copied.TrySkip(1))return 7;
                value=(byte)99;if(!DeepImmutable(&copied,ref value)||value!=(byte)0||copied.Position!=1||reader.Position!=0)return 8;
                if(!copied.TrySkip(1))return 9;value=(byte)99;
                if(ReadOnly(&copied,ref value)||value!=(byte)0||copied.Position!=2)return 10;
                value=(byte)99;if(!reader.TryPeekByte(ref value)||value!=(byte)255||reader.Position!=0)return 11;
                if(data[0]!=(byte)255||data[1]!=(byte)0)return 12;return 0;
            }
            """,optimization);

    private static async Task Run(string program,int optimization)
    {
        SourceFile[] sources=new[]{"Binary/BinaryPrimitives.gf","Binary/BinaryBufferReader.gf","Memory/bytes.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("binary-peek.gf",program)).ToArray();
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
        foreach(string forbidden in new[]{"@__gflat_eh_","personality","gflat$Memory$Allocate","gflat$Memory$Free","@malloc","@free"})
            Assert.DoesNotContain(forbidden,ir);
        Match method=Regex.Match(ir,@"(?m)^define [^\r\n]*@[^\r\n]*TryPeekByte[^\r\n]*\{");
        Assert.True(method.Success);Assert.Contains("nounwind",method.Value);
        string directory=Path.Combine(Path.GetTempPath(),"gflat-binary-peek-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string input=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");File.WriteAllText(input,ir);
            var compiled=NativeToolchain.Compile(input,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"Binary peek exit {result.ExitCode}: {result.StandardError}");
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }
}

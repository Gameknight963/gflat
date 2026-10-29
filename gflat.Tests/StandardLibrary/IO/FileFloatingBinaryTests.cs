using System.Buffers.Binary;
using System.Globalization;

namespace gflat.Tests.Std.IO;

public sealed class FileFloatingBinaryTests
{
    [Theory]
    [InlineData(0,true)]
    [InlineData(0,false)]
    [InlineData(2,true)]
    [InlineData(2,false)]
    public void FloatingRecordsPreserveExactIeeeBitsAndStreamPosition(int optimization,bool little)
    {
        uint[] floats=[0,0x80000000,1,0x80000001,0x007fffff,0x00800000,0x3f800000,0xbf800000,0x7f7fffff,0x7f800000,0xff800000,0x7fc01234,0x7f801234,0xffc05678];
        ulong[] doubles=[0,0x8000000000000000,1,0x8000000000000001,0x000fffffffffffff,0x0010000000000000,0x3ff0000000000000,0xbff0000000000000,0x7fefffffffffffff,0x7ff0000000000000,0xfff0000000000000,0x7ff8000012345678,0x7ff0000012345678,0xfff8000056789abc];
        byte[] input=new byte[1+floats.Length*14];input[0]=0x43;
        for(int i=0;i<floats.Length;i++){
            int start=1+i*14;input[start]=0xa5;input[start+13]=0x5a;
            if(little){BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(start+1),floats[i]);BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(start+5),doubles[i]);}
            else{BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(start+1),floats[i]);BinaryPrimitives.WriteUInt64BigEndian(input.AsSpan(start+5),doubles[i]);}
        }
        string order=little?"LittleEndian":"BigEndian";
        WithDirectory(directory=>{
            string source=Path.Combine(directory,"input.bin"),target=Path.Combine(directory,"output.bin");File.WriteAllBytes(source,input);
            Run($$"""
                using std; using std::IO;
                int main(){
                    uint[{{floats.Length}}] floatBits=[{{string.Join(",",floats.Select(v=>v.ToString(CultureInfo.InvariantCulture)+"u"))}}];
                    ulong[{{doubles.Length}}] doubleBits=[{{string.Join(",",doubles.Select(v=>v.ToString(CultureInfo.InvariantCulture)+"ul"))}}];
                    File input=File::OpenRead({{Literal(source)}});File output=File::OpenWrite({{Literal(target)}});
                    if(input.ReadByte()!=67)return 1;output.WriteByte('C');
                    for(nuint i=0;i<{{floats.Length}};i++){
                        if(input.ReadByte()!=165)return 2;output.WriteByte((char)165);
                        float first=FileBinary::ReadFloat{{order}}(&input);
                        double second=FileBinary::ReadDouble{{order}}(&input);
                        if(*((uint*)&first)!=floatBits[i]||*((ulong*)&second)!=doubleBits[i])return 3;
                        FileBinary::WriteFloat{{order}}(&output,first);FileBinary::WriteDouble{{order}}(&output,second);
                        if(input.ReadByte()!=90)return 4;output.WriteByte('Z');
                    }
                    if(input.ReadByte()!=-1||!input.IsOpen||!output.IsOpen)return 5;
                    input.Close();output.Close();return 0;
                }
                """,optimization);
            Assert.Equal(input,File.ReadAllBytes(target));
        });
    }

    [Theory]
    [InlineData(0,true)]
    [InlineData(0,false)]
    [InlineData(2,true)]
    [InlineData(2,false)]
    public void TruncationConsumesAvailablePrefixAndErrorsDoNotCloseBorrowedFiles(int optimization,bool little)
    {
        string order=little?"LittleEndian":"BigEndian";
        WithDirectory(directory=>{
            string shortFloat=Path.Combine(directory,"short4.bin"),shortDouble=Path.Combine(directory,"short8.bin"),target=Path.Combine(directory,"output.bin");
            File.WriteAllBytes(shortFloat,[1,2,3]);File.WriteAllBytes(shortDouble,[1,2,3,4,5,6,7]);
            Run($$"""
                using std; using std::IO;
                int main(){
                    File first=File::OpenRead({{Literal(shortFloat)}});
                    try{FileBinary::ReadFloat{{order}}(&first);return 1;}catch(ref EndOfStreamException error){}
                    if(!first.IsOpen||!first.EndOfFile||first.ReadByte()!=-1)return 2;
                    File second=File::OpenRead({{Literal(shortDouble)}});
                    try{FileBinary::ReadDouble{{order}}(&second);return 3;}catch(ref EndOfStreamException error){}
                    if(!second.IsOpen||!second.EndOfFile||second.ReadByte()!=-1)return 4;
                    first.Close();second.Close();
                    int caught=0;
                    try{FileBinary::ReadFloat{{order}}(&first);}catch(ref EndOfStreamException error){return 5;}catch(ref IOException error){caught++;}
                    try{FileBinary::ReadDouble{{order}}(&first);}catch(ref EndOfStreamException error){return 6;}catch(ref IOException error){caught++;}
                    try{FileBinary::WriteFloat{{order}}(&first,1.0f);}catch(ref IOException error){caught++;}
                    try{FileBinary::WriteDouble{{order}}(&first,1.0);}catch(ref IOException error){caught++;}
                    if(caught!=4||first.IsOpen)return 7;
                    File output=File::OpenWrite({{Literal(target)}});
                    try{FileBinary::ReadFloat{{order}}(&output);return 8;}catch(ref EndOfStreamException error){return 9;}catch(ref IOException error){}
                    if(!output.IsOpen||output.EndOfFile)return 10;
                    output.ClearError();FileBinary::WriteFloat{{order}}(&output,1.0f);
                    FileBinary::WriteDouble{{order}}(&output,1.0);output.Close();return 0;
                }
                """,optimization);
            byte[] expected=new byte[12];
            if(little){BinaryPrimitives.WriteSingleLittleEndian(expected,1.0f);BinaryPrimitives.WriteDoubleLittleEndian(expected.AsSpan(4),1.0);}
            else{BinaryPrimitives.WriteSingleBigEndian(expected,1.0f);BinaryPrimitives.WriteDoubleBigEndian(expected.AsSpan(4),1.0);}
            Assert.Equal(expected,File.ReadAllBytes(target));
        });
    }

    private static void Run(string program,int optimization)
    {
        // Preserve explicit-source consumers: no BinaryPrimitives or whole std dependency.
        var sources=new[]{"core/IO/IOException.gf","core/IO/EndOfStreamException.gf","core/IO/IByteReader.gf", "libc/IO/File.gf","libc/IO/FileBinary.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std",name))))
            .Append(new SourceFile("main.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
    private static void WithDirectory(Action<string> action){string path=Path.Combine(Path.GetTempPath(),"gflat-file-float-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);try{action(path);}finally{Directory.Delete(path,true);}}
    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
}

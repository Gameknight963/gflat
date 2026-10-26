using System.Text;

namespace gflat.Tests;

public sealed class StandardLinePrefixWriterTests
{
    private const string DenyAllocation = """
        using std; using std::IO;
        int attempts=0;
        extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes){attempts++;return null;}
            public replace void Free(void* storage) nothrow{free(storage);}
        }
        """;

    private static byte[] Prefix(byte[] input,byte[] prefix)
    {
        var result=new List<byte>();bool start=true;
        foreach(byte value in input){if(start)result.AddRange(prefix);result.Add(value);start=value==10;}
        return result.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ArbitraryChunksMatchIndependentByteOracleWithoutAllocation(int optimization)
        => WithDirectory(directory=>{
            string fixture=Path.Combine(directory,"input.bin"),target=Path.Combine(directory,"output.bin");
            int cases=0;var expected=new List<byte>();var random=new Random(62659);
            using(var packet=new BinaryWriter(File.Create(fixture)))
            {
                Write([],[],[]);
                foreach(byte[] data in new byte[][]{[10],[10,10],[13,10],[97,13,10,10,98,10],[0,128,255,10,0],Encoding.ASCII.GetBytes("a\rb\n")})
                for(int mask=0;mask<(1<<Math.Max(0,data.Length-1));mask++)
                {
                    var chunks=new List<int>();int start=0;
                    for(int i=1;i<data.Length;i++)if((mask&(1<<(i-1)))!=0){chunks.Add(i-start);start=i;}
                    chunks.Add(data.Length-start);Write(data,[62,32],chunks.ToArray());
                }
                foreach(byte[] prefix in new byte[][]{[],[0,255],[10,62],[13,10,0]})
                {
                    byte[] data=Enumerable.Range(0,256).Select(i=>(byte)i).ToArray();
                    Write(data,prefix,[256]);Write(data,prefix,Enumerable.Repeat(1,256).ToArray());
                }
                foreach(int length in new[]{4095,4096,4097,16385})
                {
                    byte[] data=Enumerable.Repeat((byte)'a',length).Concat(new byte[]{13,10,10,0,255}).ToArray();
                    var chunks=new List<int>();int position=0;
                    while(position<data.Length){int amount=Math.Min(4095,data.Length-position);chunks.Add(amount);position+=amount;}
                    Write(data,[62],chunks.ToArray());
                }
                for(int sample=0;sample<60;sample++)
                {
                    byte[] data=new byte[random.Next(1,12001)];random.NextBytes(data);
                    byte[] prefix=new byte[random.Next(0,9)];random.NextBytes(prefix);
                    var chunks=new List<int>();int position=0;
                    while(position<data.Length){int count=Math.Min(data.Length-position,random.Next(1,1025));chunks.Add(count);position+=count;}
                    Write(data,prefix,chunks.ToArray());
                }
                void Write(byte[] data,byte[] prefix,int[] chunks)
                {
                    Assert.Equal(data.Length,chunks.Sum());packet.Write(prefix.Length);packet.Write(chunks.Length);packet.Write(prefix);
                    int offset=0;foreach(int count in chunks){packet.Write(count);packet.Write(data,offset,count);offset+=count;}
                    expected.AddRange(Prefix(data,prefix));cases++;
                }
            }
            RunFiles(directory,DenyAllocation+"""
                int main(int argc,char** argv){
                    File input=File::OpenRead(argv[1]);File output=File::OpenWrite(argv[2]);
                    char[8] prefix;char[8192] chunk;
                    for(int sample=0;sample<CASES;sample++){
                        nuint size=(nuint)FileBinary::ReadUInt32LittleEndian(&input);
                        nuint chunks=(nuint)FileBinary::ReadUInt32LittleEndian(&input);
                        if(size>8)return 1;input.ReadExactly(prefix,size);
                        LinePrefixWriter writer=new(&output,prefix,size);ITextWriter* sink=&writer;
                        sink.Write((readonly(char)*)1,0);
                        for(nuint part=0;part<chunks;part++){
                            nuint count=(nuint)FileBinary::ReadUInt32LittleEndian(&input);
                            if(count>8192)return 2;input.ReadExactly(chunk,count);
                            sink.Write(chunk,count);sink.Write((readonly(char)*)1,0);
                        }
                    }
                    if(input.ReadByte()!=-1||attempts!=0)return 3;
                    input.Close();output.Close();return 0;
                }
                """.Replace("CASES",cases.ToString()),optimization,[fixture,target]);
            Assert.Equal(expected,File.ReadAllBytes(target));
        });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedPrefixCopiesMovesAndScopeHaveNoHiddenOwnership(int optimization)
        => Run(DenyAllocation+"""
            int main(){
                char[128] bytes;BufferWriter sink=new(bytes,128);
                char[3] prefix=['[','\0',']'];
                {
                    LinePrefixWriter writer=new(&sink,prefix,3);
                    writer.Write((readonly(char)*)1,0);if(sink.Length!=0)return 1;
                    writer.Write("a",1);prefix[0]='{';writer.Write("\nb",2);
                    if(sink.Length!=9||bytes[0]!='['||bytes[1]!='\0'||bytes[2]!=']'||bytes[3]!='a'||bytes[4]!='\n')return 2;
                    if(bytes[5]!='{'||bytes[6]!='\0'||bytes[7]!=']'||bytes[8]!='b')return 3;
                }
                // Scope exit emits nothing and leaves the borrowed sink/prefix usable.
                if(sink.Length!=9)return 4;sink.Clear();prefix[0]='p';
                LinePrefixWriter first=new(&sink,prefix,1);first.Write("a",1);
                LinePrefixWriter copied=first;sink.Clear();
                copied.Write("b\n",2);if(sink.Length!=2||bytes[0]!='b'||bytes[1]!='\n')return 5;
                // Discard each test stream before using the independent copied state.
                sink.Clear();first.Write("c",1);if(sink.Length!=1||bytes[0]!='c')return 6;
                sink.Clear();LinePrefixWriter moved=move copied;moved.Write("d",1);
                if(sink.Length!=2||bytes[0]!='p'||bytes[1]!='d')return 7;
                sink.Clear();LinePrefixWriter empty=new(&sink,(readonly(char)*)1,0);
                empty.Write("\0\n",2);if(sink.Length!=2||bytes[0]!='\0'||bytes[1]!='\n')return 8;
                if(attempts!=0)return 9;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PartialPrefixAndRunFailuresPreserveOriginalErrorAndPoisonWriter(int optimization)
        => Run("""
            using std; using std::IO;
            struct Sink:ITextWriter {
                public int Calls;public nuint Count;private nuint limit;private char* bytes;
                public Sink(char* storage,nuint budget){Calls=0;Count=0;limit=budget;bytes=storage;}
                public void Write(readonly(char)* data,nuint count){
                    Calls++;nuint written=0;
                    while(written<count&&Count<limit){bytes[Count]=data[written];Count++;written++;}
                    if(written!=count)throw new Exception("Original sink failure",77);
                }
            }
            int main(){
                readonly(char)* expected="p:a\np:\np:b";
                for(nuint limit=0;limit<10;limit++){
                    char[16] bytes;Sink sink=new(bytes,limit);LinePrefixWriter writer=new(&sink,"p:",2);
                    int errors=0;
                    try{writer.Write("a\n\nb",4);}catch(Exception* error){if(error.GetCode()!=77)return 1;errors++;}
                    if(errors!=1||sink.Count!=limit)return 2;
                    for(nuint i=0;i<limit;i++)if(bytes[i]!=expected[i])return 3;
                    int calls=sink.Calls;
                    try{writer.Write("x",1);}catch(Exception* error){errors++;}
                    try{writer.Write((readonly(char)*)1,0);}catch(Exception* error){errors++;}
                    if(errors!=3||sink.Calls!=calls||sink.Count!=limit)return 4;
                    LinePrefixWriter copied=writer;
                    try{copied.Write("y",1);return 5;}catch(Exception* error){}
                    if(sink.Calls!=calls)return 6;
                }
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FileCopyToPrefixesBinaryLinesAcrossReadBoundaries(int optimization)
        => WithDirectory(directory=>{
            string input=Path.Combine(directory,"input.bin"),output=Path.Combine(directory,"output.bin");
            byte[] data=Encoding.ASCII.GetBytes("\nfirst\r\n\n").Concat(new byte[]{0,255,13})
                .Concat(Enumerable.Repeat((byte)'x',8193)).Concat(new byte[]{13,10,10}).ToArray();
            File.WriteAllBytes(input,data);
            RunFiles(directory,DenyAllocation+"""
                int main(int argc,char** argv){
                    File input=File::OpenRead(argv[1]);File output=File::OpenWrite(argv[2]);
                    LinePrefixWriter writer=new(&output,"> ",2);char[257] buffer;
                    if(input.CopyTo(&writer,buffer,257)!=SIZE||attempts!=0)return 1;
                    input.Close();output.Close();return 0;
                }
                """.Replace("SIZE",data.Length.ToString()),optimization,[input,output]);
            Assert.Equal(Prefix(data,[62,32]),File.ReadAllBytes(output));
            Assert.Equal(data,File.ReadAllBytes(input));
        });

    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("line-prefix-writer-test.gf",program));

    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }

    private static void RunFiles(string directory,string program,int optimization,string[] arguments)
    {
        string ir=Path.Combine(directory,"main.ll"),exe=Path.Combine(directory,"main.exe");
        File.WriteAllText(ir,Compiler.Emit(Sources(program),options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(ir,exe,optimizationLevel:optimization);
        Assert.True(compiled.ExitCode==0,compiled.StandardError);
        var result=NativeToolchain.Run(exe,arguments,30000);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }

    private static void WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-line-prefix-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);try{action(directory);}finally{Directory.Delete(directory,true);}
    }
}

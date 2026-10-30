using gflat.CompileExceptions;

namespace gflat.Tests.Std.IO;

public sealed class BufferedReaderTests
{
    private const string MemoryHooks = """
        int allocations=0;int releases=0;bool deny=false;
        namespace Memory{
            namespace Imports{private extern void*? malloc(nuint count);private extern void free(void* address);}
            public replace void*? Allocate(nuint count){if(deny)return null;void*? result=Imports::malloc(count);if(result!=null)allocations++;return result;}
            public replace void Free(void* address)nothrow{releases++;Imports::free(address);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndependentBytesAcrossRefillsAndShortReadsUseOneOwnedBufferAndDelegateZero(int optimization)
    {
        byte[] bytes = Enumerable.Range(0, 257).Select(index => (byte)(index * 71 + 23)).ToArray();
        Run($$"""
            using std;using std::IO;
            struct Source:IByteReader{
                private readonly(char)* data;private nuint length;private nuint chunk;
                public nuint Position;public nuint Reads;public nuint ZeroReads;public nuint Maximum;
                public Source(readonly(char)* input,nuint count,nuint cap){data=input;length=count;chunk=cap;Position=0;Reads=0;ZeroReads=0;Maximum=0;}
                public nuint Read(char* output,nuint count){
                    if(count==0){ZeroReads++;return 0;}Reads++;if(count>Maximum)Maximum=count;
                    nuint take=length-Position;if(take>count)take=count;if(take>chunk)take=chunk;
                    for(nuint i=0;i<take;i++)output[i]=data[Position+i];Position+=take;return take;
                }
            }
            int main(){
                char[257] expected=[{{string.Join(",", bytes.Select(value => "(char)" + value))}}];
                nuint[7] lengths=[0,1,3,4,7,17,257];nuint[3] capacities=[1,4,16];nuint[3] chunks=[1,3,257];nuint[3] requests=[1,2,9];
                for(nuint n=0;n<7;n++)for(nuint c=0;c<3;c++)for(nuint b=0;b<3;b++)for(nuint r=0;r<3;r++){
                    nuint count=lengths[n];nuint capacity=capacities[c];Source source=new(expected,count,chunks[b]);
                    int allocated=allocations;int freed=releases;
                    {
                        BufferedReader reader=new(&source,capacity);
                        if(reader.Capacity!=capacity||reader.BufferedCount!=0||allocations!=allocated+1||source.Reads!=0)return 1;
                        if(reader.Read(((nuint)1).reinterpret<char*>(),0)!=0||source.ZeroReads!=1||source.Reads!=0)return 2;nuint zeroExpected=1;
                        char[270] output;for(nuint i=0;i<270;i++)output[i]='?';nuint position=0;
                        while(true){
                            nuint received=reader.Read(&output[1]+position,requests[r]);
                            if(received>requests[r]||reader.BufferedCount>capacity)return 3;
                            if(received==0)break;position+=received;if(position>count)return 4;
                            if(position==received){
                                nuint buffered=reader.BufferedCount;nuint physical=source.Position;nuint calls=source.Reads;zeroExpected++;
                                if(reader.Read(((nuint)1).reinterpret<char*>(),0)!=0||reader.BufferedCount!=buffered||source.Position!=physical||source.Reads!=calls||source.ZeroReads!=zeroExpected)return 12;
                            }
                        }
                        if(position!=count||source.Position!=count||output[0]!='?'||output[count+1]!='?')return 5;
                        for(nuint i=0;i<count;i++)if(output[i+1]!=expected[i])return 6;
                        nuint block=capacity;if(block>chunks[b])block=chunks[b];nuint expectedCalls=count/block;if(count%block!=0)expectedCalls++;
                        if(source.Reads!=expectedCalls+1||source.Maximum!=capacity||reader.BufferedCount!=0)return 7;
                        zeroExpected++;if(reader.Read(((nuint)1).reinterpret<char*>(),0)!=0||source.ZeroReads!=zeroExpected||source.Reads!=expectedCalls+1)return 8;
                        if(reader.Read(&output[1],1)!=0||source.Reads!=expectedCalls+2)return 9;
                        if(allocations!=allocated+1||releases!=freed)return 10;
                    }
                    if(allocations!=allocated+1||releases!=freed+1)return 11;
                }
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnknownConsumedPrefixesAndBadCountsPoisonWithoutReplayAndConstructorFailureDoesNotRead(int optimization)
        => Run("""
            using std;using std::IO;
            class ReadFailure:IOException{public ReadFailure():base("buffered source failure"){code=81;}}
            struct Source:IByteReader{
                public nuint Reads;public nuint ZeroReads;public nuint Position;public int Mode;
                public Source(){Reads=0;ZeroReads=0;Position=0;Mode=0;}
                public nuint Read(char* output,nuint count){
                    if(count==0){ZeroReads++;if(Mode==3)return 1;if(Mode==4)throw new ReadFailure();return 0;}
                    Reads++;if(Mode==1){output[0]='X';Position++;throw new ReadFailure();}if(Mode==2)return count+1;
                    for(nuint i=0;i<count;i++)output[i]=(char)(Position+i);Position+=count;return count;
                }
            }
            int main(){
                Source source=new();bool caught=false;deny=true;
                try{BufferedReader absent=new(&source,8);}catch(ref BadAllocException error){caught=true;}
                deny=false;if(!caught||source.Reads!=0||source.ZeroReads!=0||source.Position!=0)return 21;
                caught=false;try{BufferedReader invalid=new(&source,0);}catch{caught=true;}
                if(!caught||source.Reads!=0||source.ZeroReads!=0)return 22;
                for(int mode=1;mode<=4;mode++){
                    Source input=new();BufferedReader reader=new(&input,4);char[6] output;output[0]='Q';output[5]='R';
                    if(reader.Read(&output[1],1)!=1||reader.BufferedCount!=3||input.Reads!=1)return 23;
                    input.Mode=mode;
                    if(mode<=2){if(reader.Read(&output[1],3)!=3||input.Reads!=1||reader.BufferedCount!=0)return 24;}
                    caught=false;
                    try{if(mode<=2)reader.Read(&output[1],1);else reader.Read(((nuint)1).reinterpret<char*>(),0);}
                    catch(ref IOException error){caught=true;if((mode==1||mode==4)&&error.GetCode()!=81)return 25;}
                    if(!caught||output[0]!='Q'||output[5]!='R')return 26;
                    nuint reads=input.Reads;nuint zero=input.ZeroReads;nuint position=input.Position;
                    int rejected=0;try{reader.Read(&output[1],1);}catch{rejected++;}try{reader.Read(((nuint)1).reinterpret<char*>(),0);}catch{rejected++;}
                    if(rejected!=2||input.Reads!=reads||input.ZeroReads!=zero||input.Position!=position)return 27;
                    if(mode==1&&(input.Position!=5||input.Reads!=2))return 28;
                    if(mode==2&&(input.Position!=4||input.Reads!=2))return 29;
                    if(mode>=3&&(input.Position!=4||input.Reads!=1||input.ZeroReads!=1))return 30;
                }
                if(allocations!=releases)return 31;return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MovingPreservesPrefetchedSuffixAndFileVarIntsReduceUnderlyingRequests(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-buffered-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        bool passed = false;
        try
        {
            var packet = new List<byte>();
            uint[] values = Enumerable.Range(0, 64).Select(index => (uint)(index * 17)).ToArray();
            foreach (uint value in values)
            {
                uint remaining = value;
                do
                {
                    byte next = (byte)(remaining & 127); remaining >>= 7;if (remaining != 0) next |= 128;packet.Add(next);
                } while (remaining != 0);
            }
            Assert.Equal(120, packet.Count);
            string path = Path.Combine(directory, "varints.bin");
            File.WriteAllBytes(path, [..packet, 90]);
            Run($$"""
                using std;using std::IO;
                struct CountedFile:IByteReader{
                    private File* file;public nuint Reads;
                    public CountedFile(File* input){file=input;Reads=0;}
                    public nuint Read(char* output,nuint count){if(count!=0)Reads++;return file.Read(output,count);}
                }
                struct Source:IByteReader{
                    public nuint Position;public nuint Calls;
                    public Source(){Position=0;Calls=0;}
                    public nuint Read(char* output,nuint count){Calls++;nuint take=10-Position;if(take>count)take=count;for(nuint i=0;i<take;i++)output[i]=(char)(Position+i);Position+=take;return take;}
                }
                int main(){
                    Source source=new();int allocated=allocations;int freed=releases;
                    {
                        BufferedReader first=new(&source,8);char[12] output;output[0]='Q';output[11]='R';
                        if(first.Read(&output[1],1)!=1||source.Position!=8||first.BufferedCount!=7)return 41;
                        BufferedReader second=move first;IByteReader* input=&second;
                        if(input.Read(&output[2],7)!=7||source.Calls!=1||second.BufferedCount!=0)return 42;
                        if(input.Read(&output[9],2)!=2||source.Calls!=2||source.Position!=10)return 43;
                        for(nuint i=0;i<10;i++)if(output[i+1]!=(char)i)return 44;
                        if(output[0]!='Q'||output[11]!='R'||allocations!=allocated+1||releases!=freed)return 45;
                    }
                    if(releases!=freed+1)return 46;
                    Source defaultSource=new();allocated=allocations;freed=releases;
                    {BufferedReader defaults=new(&defaultSource);if(defaults.Capacity!=4096||defaults.BufferedCount!=0||defaultSource.Calls!=0||allocations!=allocated+1)return 55;}
                    if(releases!=freed+1)return 56;
                    File direct=File::OpenRead("PATH");CountedFile directSource=new(&direct);
                    uint[64] expected=[{{string.Join(",", values.Select(value => value + "u"))}}];
                    for(nuint i=0;i<64;i++)if(std::IO::ReadVarUInt32(&directSource)!=expected[i])return 47;
                    if(directSource.Reads!=120||!direct.IsOpen)return 48;direct.Close();
                    File file=File::OpenRead("PATH");CountedFile counted=new(&file);allocated=allocations;freed=releases;
                    {
                        BufferedReader reader=new(&counted,17);
                        for(nuint i=0;i<64;i++)if(std::IO::ReadVarUInt32(&reader)!=expected[i])return 49;
                        if(counted.Reads!=8||reader.BufferedCount!=1||!file.IsOpen)return 50;
                        char marker='\0';if(reader.Read(&marker,1)!=1||marker!='Z'||counted.Reads!=8)return 51;
                        if(reader.Read(&marker,1)!=0||counted.Reads!=9)return 52;
                        if(allocations!=allocated+1||releases!=freed)return 53;
                    }
                    if(!file.IsOpen||releases!=freed+1)return 54;file.Close();return 0;
                }
                """.Replace("PATH", path.Replace("\\", "/")), optimization);
            passed = true;
        }
        catch (Exception error)
        {
            error.Data["BufferedReaderFileDirectory"] = directory;
            throw;
        }
        finally
        {
            if (passed) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void VectorOwnedReaderCannotBeCopiedImplicitly()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using std::IO;
            struct Source:IByteReader{public nuint Read(char* data,nuint count){return 0;}}
            void Probe(){Source source=new();BufferedReader first=new(&source,4);BufferedReader duplicate=first;}
            """), options: new() { NoStdLib = true }));
        Assert.Contains("copy", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("buffered-reader-tests.gf", program));

    private static void Run(string program, int optimization)
    {
        string ir = Compiler.Emit(Sources(program + "\n" + MemoryHooks), options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}

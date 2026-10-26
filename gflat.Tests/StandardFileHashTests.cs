using CryptoSHA256 = System.Security.Cryptography.SHA256;

namespace gflat.Tests;

public sealed class StandardFileHashTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("file-hash.gf",program));

    private const string DenyAllocation="""
        using std; using std::IO; using std::Hashing;int attempts=0;
        extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes){attempts++;return null;}
            public replace void Free(void* storage) nothrow{free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PathAndBorrowedSuffixDigestsMatchDotNetAcrossPaddingAndIoBoundaries(int optimization)
    {
        WithDirectory(directory=>
        {
            string executable=Compile(directory,DenyAllocation+"""
                bool Equal(readonly(byte)* left,readonly(byte)* right) nothrow {
                    for(nuint i=0;i<32;i++)if(left[i]!=right[i])return false;return true;
                }
                int main(int argc,char** argv){
                    File expected=File::OpenRead(argv[2]);byte[32] wanted;byte[32] empty;
                    expected.ReadExactly((char*)(byte*)wanted,32);expected.ReadExactly((char*)(byte*)empty,32);
                    byte[32] pathHash=FileHash::SHA256(argv[1]);
                    if(!Equal(pathHash,wanted))return 1;
                    pathHash[0]=(byte)(pathHash[0]^(byte)255);
                    byte[32] independent=FileHash::SHA256(argv[1]);if(!Equal(independent,wanted))return 2;
                    {
                        File input=File::OpenRead(argv[1]);byte[32] digest=FileHash::SHA256(&input);
                        if(!Equal(digest,wanted)||!input.IsOpen||!input.EndOfFile||input.ReadByte()!=-1)return 3;
                        byte[32] second=FileHash::SHA256(&input);if(!Equal(second,empty)||!input.IsOpen)return 4;
                        input.Close();
                    }
                    int positions=FileBinary::ReadInt32LittleEndian(&expected);
                    int[6] capacities=[1,7,63,64,65,4096];char[4098] scratch;
                    for(int position=0;position<positions;position++){
                        int offset=FileBinary::ReadInt32LittleEndian(&expected);
                        expected.ReadExactly((char*)(byte*)wanted,32);
                        for(nuint choice=0;choice<6;choice++){
                            nuint capacity=(nuint)capacities[choice];
                            File input=File::OpenRead(argv[1]);
                            for(int skipped=0;skipped<offset;skipped++)if(input.ReadByte()==-1)return 5;
                            for(nuint i=0;i<4098;i++)scratch[i]='?';
                            byte[32] digest=FileHash::SHA256(&input,&scratch[1],capacity);
                            if(!Equal(digest,wanted)||!input.IsOpen||!input.EndOfFile||input.ReadByte()!=-1)return 6;
                            if(scratch[0]!='?')return 7;
                            for(nuint i=capacity+1;i<4098;i++)if(scratch[i]!='?')return 8;
                            byte[32] second=FileHash::SHA256(&input,&scratch[1],capacity);
                            if(!Equal(second,empty)||!input.IsOpen)return 9;input.Close();
                        }
                    }
                    expected.Close();if(attempts!=0)return 10;return 0;
                }
                """,optimization);
            var random=new Random(51071);var samples=new List<byte[]>();
            foreach(int length in new[]{0,1,2,3,55,56,63,64,65,127,128,129,4095,4096,4097,65537})
            {
                byte[] data=new byte[length];random.NextBytes(data);samples.Add(data);
            }
            samples.Add(Enumerable.Range(0,256).Select(value=>(byte)value).ToArray());
            samples.Add(new byte[4097]);samples.Add(Enumerable.Repeat((byte)255,129).ToArray());
            string input=Path.Combine(directory,"input.bin"),expected=Path.Combine(directory,"expected.bin");
            foreach(byte[] data in samples)
            {
                File.WriteAllBytes(input,data);
                int[] offsets=new[]{0,Math.Min(1,data.Length),data.Length/2,data.Length}.Distinct().ToArray();
                using(var fixture=new BinaryWriter(File.Create(expected)))
                {
                    fixture.Write(CryptoSHA256.HashData(data));fixture.Write(CryptoSHA256.HashData([]));
                    fixture.Write(offsets.Length);
                    foreach(int offset in offsets){fixture.Write(offset);fixture.Write(CryptoSHA256.HashData(data.AsSpan(offset)));}
                }
                Run(executable,[input,expected]);Assert.Equal(data,File.ReadAllBytes(input));
            }
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidBuffersAndReadErrorsNeverTakeOwnershipOfBorrowedFiles(int optimization)
    {
        WithDirectory(directory=>
        {
            string executable=Compile(directory,"""
                using std; using std::IO; using std::Hashing;
                int main(int argc,char** argv){
                    File input=File::OpenRead(argv[1]);int caught=0;char[3] scratch=['?','?','?'];
                    try{FileHash::SHA256(&input,(char*)1,0);}catch(IOException* error){caught++;}
                    if(caught!=1||!input.IsOpen||input.ReadByte()!='a')return 1;
                    try{FileHash::SHA256(&input,&scratch[1],0);}catch(IOException* error){caught++;}
                    if(caught!=2||scratch[0]!='?'||scratch[1]!='?'||scratch[2]!='?'||input.ReadByte()!='b')return 2;
                    input.Close();
                    try{FileHash::SHA256(&input);}catch(IOException* error){caught++;}
                    try{FileHash::SHA256(&input,scratch,3);}catch(IOException* error){caught++;}
                    if(caught!=4||input.IsOpen)return 3;
                    File output=File::OpenWrite(argv[2]);
                    try{FileHash::SHA256(&output,scratch,3);}catch(EndOfStreamException* error){return 4;}
                    catch(IOException* error){caught++;}
                    if(caught!=5||!output.IsOpen||output.EndOfFile)return 5;
                    output.ClearError();output.Write("ok",2);output.Close();
                    try{FileHash::SHA256(argv[3]);}catch(IOException* error){caught++;}
                    if(caught!=6)return 6;return 0;
                }
                """,optimization);
            string input=Path.Combine(directory,"input.bin"),output=Path.Combine(directory,"output.bin");
            File.WriteAllBytes(input,"abc"u8.ToArray());
            Run(executable,[input,output,Path.Combine(directory,"missing.bin")]);
            Assert.Equal("abc"u8.ToArray(),File.ReadAllBytes(input));Assert.Equal("ok"u8.ToArray(),File.ReadAllBytes(output));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwnedPathOverloadClosesBeforeReturningAnIndependentDigest(int optimization)
    {
        WithDirectory(directory=>
        {
            string executable=Compile(directory,DenyAllocation+"""
                int main(int argc,char** argv){
                    byte[32] first=FileHash::SHA256(argv[1]);byte[32] second=FileHash::SHA256(argv[1]);
                    // Remove while this process still runs: Windows rejects an open CRT file.
                    // On Unix unlink alone is not proof that a descriptor was closed.
                    File::Remove(argv[1]);
                    first[0]=(byte)(first[0]^(byte)255);
                    if(first[0]==second[0]||second[0]!=(byte)0xba||second[31]!=(byte)0xad||attempts!=0)return 1;
                    return 0;
                }
                """,optimization);
            string input=Path.Combine(directory,"owned.bin");File.WriteAllBytes(input,"abc"u8.ToArray());
            Run(executable,[input]);Assert.False(File.Exists(input));
        });
    }

    private static void WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-file-hash-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try{action(directory);}finally{Directory.Delete(directory,true);}
    }

    private static string Compile(string directory,string program,int optimization)
    {
        string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");
        File.WriteAllText(ir,Compiler.Emit(Sources(program),options:new(){InternalizeFunctions=true}));
        var result=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
        Assert.True(result.ExitCode==0,result.StandardError);return executable;
    }

    private static void Run(string executable,string[] arguments)
    {
        var result=NativeToolchain.Run(executable,arguments,30000);
        Assert.True(result.ExitCode==0,$"File hash exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        Assert.Empty(result.StandardOutput);Assert.Empty(result.StandardError);
    }
}

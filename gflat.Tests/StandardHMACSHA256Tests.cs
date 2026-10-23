using System.Security.Cryptography;
using System.Text;

namespace gflat.Tests;

public sealed class StandardHMACSHA256Tests
{
    private const string DenyAllocation = """
        using std;
        int attempts=0;
        extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes){attempts++;return null;}
            public replace void Free(void* storage){free(storage);}
        }
        """;

    // Published RFC4231 section4 test vectors. Case5 specifies a128-bit prefix.
    private static IEnumerable<(byte[] Key,byte[] Data,string Hex)> PublishedVectors()
    {
        yield return (Repeat(0x0b,20),Encoding.ASCII.GetBytes("Hi There"),
            "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7");
        yield return (Encoding.ASCII.GetBytes("Jefe"),Encoding.ASCII.GetBytes("what do ya want for nothing?"),
            "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843");
        yield return (Repeat(0xaa,20),Repeat(0xdd,50),
            "773ea91e36800e46854db8ebd09181a72959098b3ef8c122d9635514ced565fe");
        yield return (Enumerable.Range(1,25).Select(i=>(byte)i).ToArray(),Repeat(0xcd,50),
            "82558a389a443c0ea4cc819899f2083a85f0faa3e578f8077a2e3ff46729665b");
        yield return (Repeat(0x0c,20),Encoding.ASCII.GetBytes("Test With Truncation"),
            "a3b6167473100ee06e0c796c2955552b");
        yield return (Repeat(0xaa,131),Encoding.ASCII.GetBytes("Test Using Larger Than Block-Size Key - Hash Key First"),
            "60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54");
        yield return (Repeat(0xaa,131),Encoding.ASCII.GetBytes("This is a test using a larger than block-size key and a larger than block-size data. The key needs to be hashed before being used by the HMAC algorithm."),
            "9b09ffa71b942fcb27635fbcd5b0e944bfdc63644f0713938a7f51535c3a35e2");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PublishedAndIndependentDigestsMatchAcrossKeysPaddingChunksAndResets(int optimization)
        => WithDirectory(directory=>{
            string path=Path.Combine(directory,"vectors.bin");int cases=0;var random=new Random(63083);
            using(var fixture=new BinaryWriter(File.Create(path)))
            {
                foreach(var (key,data,hex) in PublishedVectors())
                {
                    byte[] published=Convert.FromHexString(hex),oracle=HMACSHA256.HashData(key,data);
                    Assert.Equal(published,oracle[..published.Length]);
                    Write(key,data,[data.Length]);
                    Write(key,data,Enumerable.Repeat(1,data.Length).ToArray());
                }
                foreach(int keyLength in new[]{0,1,20,31,32,63,64,65,127,128,129,131,256,4097})
                foreach(int length in new[]{0,1,55,56,63,64,65,119,120,127,128,129,4095,4096,4097})
                {
                    byte[] key=RandomBytes(random,keyLength),data=RandomBytes(random,length);
                    var chunks=new List<int>();int offset=0;
                    while(offset<length){int count=Math.Min(length-offset,random.Next(1,130));chunks.Add(count);offset+=count;}
                    Write(key,data,chunks.ToArray());
                }
                for(int length=1;length<=7;length++)
                for(int mask=0;mask<(1<<(length-1));mask++)
                {
                    byte[] key=RandomBytes(random,65),data=Enumerable.Range(0,length).Select(i=>(byte)(i*71)).ToArray();
                    var chunks=new List<int>();int start=0;
                    for(int i=1;i<length;i++)if((mask&(1<<(i-1)))!=0){chunks.Add(i-start);start=i;}
                    chunks.Add(length-start);Write(key,data,chunks.ToArray());
                }
                byte[] all=Enumerable.Range(0,256).Select(i=>(byte)i).ToArray();
                Write(all,all,[1,63,64,127,1]);
                for(int sample=0;sample<50;sample++)
                {
                    byte[] key=RandomBytes(random,random.Next(0,4098)),data=RandomBytes(random,random.Next(0,8194));
                    int split=random.Next(data.Length+1);Write(key,data,[split,data.Length-split]);
                }
                void Write(byte[] key,byte[] data,int[] chunks)
                {
                    Assert.Equal(data.Length,chunks.Sum());
                    fixture.Write(key.Length);fixture.Write(data.Length);fixture.Write(chunks.Length);
                    foreach(int count in chunks)fixture.Write(count);
                    fixture.Write(key);fixture.Write(data);fixture.Write(HMACSHA256.HashData(key,data));
                    fixture.Write(HMACSHA256.HashData(key,Array.Empty<byte>()));cases++;
                }
            }
            RunFiles(directory,DenyAllocation+"""
                int main(int argc,char** argv){
                    File input=File::OpenRead(argv[1]);
                    char[4097] key;char[8193] data;int[8193] chunks;char[32] expected;char[32] empty;
                    for(int sample=0;sample<CASES;sample++){
                        nuint keyCount=(nuint)FileBinary::ReadUInt32LittleEndian(&input);
                        nuint count=(nuint)FileBinary::ReadUInt32LittleEndian(&input);
                        nuint parts=(nuint)FileBinary::ReadUInt32LittleEndian(&input);
                        if(keyCount>4097||count>8193||parts>8193)return 1;
                        for(nuint i=0;i<parts;i++)chunks[i]=(int)FileBinary::ReadUInt32LittleEndian(&input);
                        input.ReadExactly(key,keyCount);input.ReadExactly(data,count);
                        input.ReadExactly(expected,32);input.ReadExactly(empty,32);
                        HMACSHA256 hash=new(key,keyCount);
                        for(nuint i=0;i<keyCount;i++)key[i]='?';
                        nuint offset=0;
                        for(nuint part=0;part<parts;part++){
                            nuint amount=(nuint)chunks[part];if(amount>count-offset)return 2;
                            if(!hash.TryAppend((readonly(char)*)data+offset,amount)||!hash.TryAppend((readonly(char)*)1,0))return 3;
                            for(nuint i=0;i<amount;i++)data[offset+i]='!';offset+=amount;
                        }
                        if(offset!=count)return 4;
                        byte[32] first=hash.GetHash();byte[32] second=hash.GetHash();
                        for(nuint i=0;i<32;i++)if(first[i]!=(byte)expected[i]||second[i]!=first[i])return 5;
                        first[0]=(byte)(first[0]^(byte)255);
                        byte[32] third=hash.GetHash();for(nuint i=0;i<32;i++)if(third[i]!=(byte)expected[i])return 6;
                        hash.Reset();byte[32] reset=hash.GetHash();for(nuint i=0;i<32;i++)if(reset[i]!=(byte)empty[i])return 7;
                    }
                    input.Close();if(attempts!=0)return 8;return 0;
                }
                """.Replace("CASES",cases.ToString()),optimization,[path]);
        });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopiedSnapshotsDivergeAndResetRetainsKeyWithoutAllocation(int optimization)
    {
        byte[] key=Encoding.ASCII.GetBytes("secret");
        string[] messages=["prefix","prefixA","prefixB","again",""];
        string declarations=string.Join("\n",messages.Select((message,index)=>
            $"byte[32] expected{index}=[{Bytes(HMACSHA256.HashData(key,Encoding.ASCII.GetBytes(message)))}];"));
        Run(DenyAllocation+"""
            bool Same(readonly(byte)* a,readonly(byte)* b) nothrow {for(nuint i=0;i<32;i++)if(a[i]!=b[i])return false;return true;}
            int main(){
                EXPECTED
                HMACSHA256 hash=new("secret",6);hash.TryAppend("prefix",6);
                byte[32] before=hash.GetHash();if(!Same(before,expected0))return 1;
                HMACSHA256 copy=hash;hash.TryAppend("A",1);copy.TryAppend("B",1);
                byte[32] first=hash.GetHash();byte[32] second=copy.GetHash();
                if(!Same(first,expected1)||!Same(second,expected2)||!Same(before,expected0))return 2;
                hash.Reset();byte[32] empty=hash.GetHash();if(!Same(empty,expected4))return 3;
                hash.TryAppend("again",5);byte[32] again=hash.GetHash();if(!Same(again,expected3))return 4;
                HMACSHA256 moved=move copy;byte[32] transferred=moved.GetHash();if(!Same(transferred,expected2))return 5;
                if(attempts!=0)return 6;return 0;
            }
            """.Replace("EXPECTED",declarations),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImpossibleAppendIsRejectedBeforeReadingAndPreservesMessage(int optimization)
        => Run(DenyAllocation+"""
            int main(){
                HMACSHA256 hash=new((readonly(char)*)1,0);
                byte[32] before=hash.GetHash();
                if(sizeof(nuint)==8){
                    if(hash.TryAppend((readonly(char)*)1,(nuint)2305843009213693888ul))return 1;
                    byte[32] after=hash.GetHash();for(nuint i=0;i<32;i++)if(before[i]!=after[i])return 2;
                }
                if(!hash.TryAppend("abc",3))return 3;byte[32] prefix=hash.GetHash();
                if(sizeof(nuint)==8){
                    if(hash.TryAppend((readonly(char)*)1,(nuint)2305843009213693885ul))return 4;
                    byte[32] after=hash.GetHash();for(nuint i=0;i<32;i++)if(prefix[i]!=after[i])return 5;
                }
                if(!hash.TryAppend((readonly(char)*)1,0)||!hash.TryAppend("d",1)||attempts!=0)return 6;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImpossibleKeyLengthThrowsBeforeReadingKey(int optimization)
        => Run("""
            using std;
            int main(){
                if(sizeof(nuint)!=8)return 0;
                try{HMACSHA256 invalid=new((readonly(char)*)1,(nuint)2305843009213693952ul);return 1;}
                catch(Exception* error){}
                HMACSHA256 valid=new((readonly(char)*)1,0);valid.TryAppend("x",1);
                byte[32] hash=valid.GetHash();return 0;
            }
            """,optimization);

    [Fact]
    public void ConstructionRequiresAnExplicitKey()
    {
        var error=Assert.Throws<gflat.CompileExceptions.TypeCheckException>(()=>Compiler.Emit(Sources("""
            using std;int main(){HMACSHA256 missing=new();return 0;}
            """)));
        Assert.Contains("constructor",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    private static string Bytes(byte[] bytes)=>string.Join(",",bytes.Select(value=>"(byte)"+value));
    private static byte[] Repeat(byte value,int count)=>Enumerable.Repeat(value,count).ToArray();
    private static byte[] RandomBytes(Random random,int count){byte[] bytes=new byte[count];random.NextBytes(bytes);return bytes;}
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("hmac-sha256-test.gf",program));
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
        string directory=Path.Combine(Path.GetTempPath(),"gflat-hmac-sha256-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);try{action(directory);}finally{Directory.Delete(directory,true);}
    }
}

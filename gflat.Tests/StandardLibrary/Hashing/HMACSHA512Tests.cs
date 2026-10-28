using System.Security.Cryptography;
using System.Text;

namespace gflat.Tests.Std.Hashing;

public sealed class HMACSHA512Tests
{
    // RFC 4231 sections 4.2-4.8. Case 5 publishes only a 128-bit prefix.
    private static IEnumerable<(byte[] Key,byte[] Data,string Hex)> PublishedVectors()
    {
        yield return (Repeat(0x0b,20),"Hi There"u8.ToArray(),
            "87aa7cdea5ef619d4ff0b4241a1d6cb02379f4e2ce4ec2787ad0b30545e17cdedaa833b7d6b8a702038b274eaea3f4e4be9d914eeb61f1702e696c203a126854");
        yield return ("Jefe"u8.ToArray(),"what do ya want for nothing?"u8.ToArray(),
            "164b7a7bfcf819e2e395fbe73b56e0a387bd64222e831fd610270cd7ea2505549758bf75c05a994a6d034f65f8f0e6fdcaeab1a34d4a6b4b636e070a38bce737");
        yield return (Repeat(0xaa,20),Repeat(0xdd,50),
            "fa73b0089d56a284efb0f0756c890be9b1b5dbdd8ee81a3655f83e33b2279d39bf3e848279a722c806b485a47e67c807b946a337bee8942674278859e13292fb");
        yield return (Enumerable.Range(1,25).Select(i=>(byte)i).ToArray(),Repeat(0xcd,50),
            "b0ba465637458c6990e5a8c5f61d4af7e576d97ff94b872de76f8050361ee3dba91ca5c11aa25eb4d679275cc5788063a5f19741120c4f2de2adebeb10a298dd");
        yield return (Repeat(0x0c,20),"Test With Truncation"u8.ToArray(),"415fad6271580a531d4179bc891d87a6");
        yield return (Repeat(0xaa,131),"Test Using Larger Than Block-Size Key - Hash Key First"u8.ToArray(),
            "80b24263c7c1a3ebb71493c1dd7be8b49b46d1f41b4aeec1121b013783f8f3526b56d037e05f2598bd0fd2215d6a1e5295e64f73f63f0aec8b915a985d786598");
        yield return (Repeat(0xaa,131),Encoding.ASCII.GetBytes("This is a test using a larger than block-size key and a larger than block-size data. The key needs to be hashed before being used by the HMAC algorithm."),
            "e37b6a775dc87dbaa4dfa9f96e5e3ffddebd71f8867289865df5a32d20cdc944b6022cac3c4982b10d5eeb55c3e4de15134676fb6de0446065c97440fa8c6a58");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task PublishedAndDotNetDigestsMatchUnalignedKeysBinaryDataChunksAndReset(int optimization)
    {
        var cases=new List<(byte[] Key,byte[] Data)>();
        foreach(var (key,data,hex) in PublishedVectors()) {
            byte[] published=Convert.FromHexString(hex),oracle=HMACSHA512.HashData(key,data);
            Assert.Equal(published,oracle[..published.Length]);cases.Add((key,data));
        }
        var random=new Random(75419);
        foreach(int keyLength in new[]{0,1,63,64,65,127,128,129,131,256,4097})
        foreach(int length in new[]{0,1,111,112,113,127,128,129,255,256,257,4097})
            cases.Add((RandomBytes(random,keyLength),RandomBytes(random,length)));
        byte[] all=Enumerable.Range(0,256).Select(i=>(byte)i).ToArray();cases.Add((all,all));
        for(int i=0;i<8;i++)cases.Add((RandomBytes(random,random.Next(4098)),RandomBytes(random,random.Next(4098))));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_hmac_sha512_vectors_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            string path=Path.Combine(directory,"vectors.bin");
            using(var output=new BinaryWriter(File.Create(path)))foreach(var (key,data) in cases) {
                output.Write((uint)key.Length);output.Write((uint)data.Length);output.Write(key);output.Write(data);
                output.Write(HMACSHA512.HashData(key,data));output.Write(HMACSHA512.HashData(key,Array.Empty<byte>()));
            }
            await Run($$"""
                using std::Hashing;
                extern void*? fopen(readonly(char)* path,readonly(char)* mode)nothrow;
                extern nuint fread(void* data,nuint size,nuint count,void* file)nothrow;
                extern int fclose(void* file)nothrow;
                bool Same(byte[64] digest,readonly(byte)* expected)nothrow{
                    for(nuint i=0;i<64;i++)if(digest[i]!=expected[i])return false;return true;
                }
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;
                    char[4099] key;char[4099] data;byte[64] expected;byte[64] empty;
                    for(int sample=0;sample<{{cases.Count}};sample++){
                        uint keySize=0;uint size=0;
                        if(fread((void*)&keySize,1,4,file!)!=4||fread((void*)&size,1,4,file!)!=4||keySize>4097u||size>4097u)return 2;
                        nuint keyCount=(nuint)keySize;nuint count=(nuint)size;
                        for(nuint i=0;i<4099;i++){key[i]='?';data[i]='!';}
                        if(fread((void*)&key[1],1,keyCount,file!)!=keyCount||fread((void*)&data[1],1,count,file!)!=count
                            ||fread((void*)(byte*)expected,1,64,file!)!=64||fread((void*)(byte*)empty,1,64,file!)!=64)return 3;
                        char[4099] savedKey=key;char[4099] savedData=data;
                        HMACSHA512 seed=new(&key[1],keyCount);
                        for(nuint i=0;i<4099;i++)if(key[i]!=savedKey[i])return 4;
                        for(nuint i=0;i<keyCount;i++)key[i+1]='X';
                        for(int mode=0;mode<3;mode++){
                            data=savedData;HMACSHA512 hash=seed;nuint position=0;
                            while(position<count){
                                nuint take=count-position;if(mode==1)take=1;if(mode==2)take=position%193+1;
                                if(take>count-position)take=count-position;
                                if(!hash.TryAppend(&data[position+1],take))return 5;
                                for(nuint i=0;i<take;i++){
                                    if(data[position+i+1]!=savedData[position+i+1])return 6;
                                    data[position+i+1]='Y';
                                }
                                position+=take;
                            }
                            if(!hash.TryAppend((readonly(char)*)1,0)||!Same(hash.GetHash(),expected)||!Same(hash.GetHash(),expected))return 7;
                            byte[64] changed=hash.GetHash();changed[0]=(byte)(changed[0]^(byte)255);
                            if(Same(changed,expected)||!Same(hash.GetHash(),expected))return 8;
                            hash.Reset();if(!Same(hash.GetHash(),empty))return 9;
                        }
                        if(key[0]!='?'||key[keyCount+1]!='?'||data[0]!='!'||data[count+1]!='!')return 10;
                    }
                    char extra='?';if(fread((void*)&extra,1,1,file!)!=0)return 11;return fclose(file!);
                }
                """,optimization);
        }catch(Exception error){error.Data["HMACSHA512OracleDirectory"]=directory;throw;}
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task IndependentCopiesSnapshotsAndResetKeepTheOriginalKey(int optimization)
    {
        byte[] key=RandomBytes(new Random(17833),129),prefix=RandomBytes(new Random(9928),139);
        byte[] messageA=[..prefix,(byte)'A'],messageB=[..prefix,(byte)'B'];
        byte[] before=HMACSHA512.HashData(key,prefix),a=HMACSHA512.HashData(key,messageA);
        byte[] b=HMACSHA512.HashData(key,messageB),empty=HMACSHA512.HashData(key,Array.Empty<byte>());
        byte[] again=HMACSHA512.HashData(key,"again"u8);
        await Run($$"""
            using std::Hashing;
            bool Same(byte[64] a,byte[64] b)nothrow{for(nuint i=0;i<64;i++)if(a[i]!=b[i])return false;return true;}
            int main(){
                byte[129] key={{Bytes(key)}};byte[139] input={{Bytes(prefix)}};
                byte[64] before={{Bytes(before)}};byte[64] a={{Bytes(a)}};byte[64] b={{Bytes(b)}};
                byte[64] empty={{Bytes(empty)}};byte[64] again={{Bytes(again)}};
                HMACSHA512 hash=new((readonly(char)*)(byte*)key,129);
                for(nuint i=0;i<129;i++)key[i]=(byte)0;
                if(!hash.TryAppend((readonly(char)*)(byte*)input,139))return 1;
                for(nuint i=0;i<139;i++)input[i]=(byte)0;
                immutable(HMACSHA512) frozen=hash;HMACSHA512 copy=hash;
                if(!Same(frozen.GetHash(),before)||!Same(hash.GetHash(),before))return 2;
                if(!hash.TryAppend("A",1)||!copy.TryAppend("B",1))return 3;
                if(!Same(hash.GetHash(),a)||!Same(copy.GetHash(),b)||!Same(frozen.GetHash(),before))return 4;
                HMACSHA512 moved=move copy;if(!Same(moved.GetHash(),b))return 5;
                hash.Reset();if(!Same(hash.GetHash(),empty)||!hash.TryAppend("again",5)||!Same(hash.GetHash(),again))return 6;
                HMACSHA512 emptyKey=new((readonly(char)*)1,0);
                if(!emptyKey.TryAppend((readonly(char)*)1,0))return 7;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SyntheticInnerLengthLimitRejectsAtomicallyAndResetRestoresPad(int optimization)
    {
        // Synthetic private counters test preflight, not huge-message execution.
        await Run("""
            using std::Hashing;
            bool SameState(readonly(HMACSHA512)* a,readonly(HMACSHA512)* b)nothrow{
                readonly(byte)* first=(readonly(byte)*)a;readonly(byte)* second=(readonly(byte)*)b;
                for(nuint i=0;i<(nuint)sizeof(HMACSHA512);i++)if(first[i]!=second[i])return false;return true;
            }
            bool Same(byte[64] a,byte[64] b)nothrow{for(nuint i=0;i<64;i++)if(a[i]!=b[i])return false;return true;}
            int main(){
                HMACSHA512 hash=new("key",3);byte[64] empty=hash.GetHash();
                if(hash.running.lengthLow!=128ul||hash.running.lengthHigh!=0ul)return 1;
                char[127] input;for(nuint i=0;i<127;i++)input[i]=(char)i;
                if(!hash.TryAppend(input,127))return 2;
                // 128+127 pending-byte congruence is 127, matching the seeded maximum.
                hash.running.lengthHigh=0x1ffffffffffffffful;hash.running.lengthLow=0xfffffffffffffffful;
                HMACSHA512 before=hash;byte[64] digest=hash.GetHash();
                if(hash.TryAppend((readonly(char)*)1,1)||!SameState(&hash,&before)||!Same(hash.GetHash(),digest))return 3;
                if(!hash.TryAppend((readonly(char)*)1,0)||!SameState(&hash,&before))return 4;
                hash.Reset();if(hash.running.lengthLow!=128ul||hash.running.lengthHigh!=0ul||!Same(hash.GetHash(),empty))return 5;
                // Low-word carry also passes through the adapter with congruent pending input.
                if(!hash.TryAppend(input,124))return 6;
                hash.running.lengthLow=0xfffffffffffffffcul;hash.running.lengthHigh=7ul;
                if(!hash.TryAppend(input,4)||hash.running.lengthLow!=0ul||hash.running.lengthHigh!=8ul)return 7;
                hash.Reset();return 0;
            }
            """,optimization,exposeCounters:true);
    }

    [Fact]
    public void ConstructionRequiresAnExplicitCountedKey()
    {
        var error=Assert.Throws<gflat.CompileExceptions.TypeCheckException>(()=>Compiler.Emit(Sources("using std::Hashing;int main(){HMACSHA512 missing=new();return 0;}",false)));
        Assert.Contains("constructor",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Repeat(byte value,int count)=>Enumerable.Repeat(value,count).ToArray();
    private static byte[] RandomBytes(Random random,int count){byte[] result=new byte[count];random.NextBytes(result);return result;}
    private static string Bytes(byte[] bytes)=>"["+string.Join(",",bytes.Select(b=>"(byte)"+b))+"]";
    private static string Literal(string text)=>"\""+text.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static IEnumerable<SourceFile> Sources(string code,bool exposeCounters)
    {
        foreach(string name in new[]{"Hashing/HMACSHA512.gf","Hashing/SHA512.gf","Numerics/BitOperations.gf"}) {
            string path=Path.Combine(AppContext.BaseDirectory,"Std/core",name),source=File.ReadAllText(path);
            if(exposeCounters&&name!="Numerics/BitOperations.gf") {
                string[] fields=name=="Hashing/HMACSHA512.gf"?["SHA512 running"]:["ulong lengthLow","ulong lengthHigh"];
                string fixture=source;
                foreach(string field in fields){string anchor="private "+field+";";Assert.Equal(2,fixture.Split(anchor).Length);fixture=fixture.Replace(anchor,"public "+field+";");}
                string restored=fixture;foreach(string field in fields)restored=restored.Replace("public "+field+";","private "+field+";");
                Assert.Equal(source,restored);source=fixture;
            }
            yield return new SourceFile(path,source);
        }
        yield return new SourceFile("hmac-sha512-test.gf",code);
    }
    private static async Task Run(string code,int optimization,bool exposeCounters=false)
    {
        string ir=Compiler.Emit(Sources(code,exposeCounters),options:new(){NoStdLib=true,NoBuiltins=true});
        Assert.DoesNotContain("@gflat$Memory$",ir);Assert.DoesNotContain("@__gflat_eh_",ir);
        string directory=Path.Combine(Path.GetTempPath(),"gflat_hmac_sha512_native_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            string input=Path.Combine(directory,"hash.ll"),output=Path.Combine(directory,"hash.exe");File.WriteAllText(input,ir);
            var compiled=NativeToolchain.Compile(input,output,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(output,[]);Assert.True(result.ExitCode==0,$"HMACSHA512 exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }catch(Exception error){error.Data["HMACSHA512NativeDirectory"]=directory;throw;}
        await TestDirectoryCleanup.DeleteAsync(directory);
    }
}

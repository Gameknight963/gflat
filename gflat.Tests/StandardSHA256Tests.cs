using System.Security.Cryptography;
using System.Text;

namespace gflat.Tests;

public sealed class StandardSHA256Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WholeAndArbitraryChunksMatchPublishedAndDotNetDigests(int optimization)
    {
        byte[][] published=[[],"abc"u8.ToArray(),"abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"u8.ToArray()];
        string[] expected=["E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            "248D6A61D20638B8E5C026930C3E6039A33CE45964FF2167F6ECEDD419DB06C1"];
        for(int i=0;i<published.Length;i++)Assert.Equal(expected[i],Convert.ToHexString(SHA256.HashData(published[i])));
        List<byte[]> values=[..published,Enumerable.Range(0,256).Select(i=>(byte)i).ToArray()];
        var random=new Random(48169);
        foreach(int length in Enumerable.Range(0,132).Concat([255,256,257,511,512,513,4095,4096,4097,8193])){
            byte[] value=new byte[length];random.NextBytes(value);values.Add(value);
        }
        for(int sample=0;sample<40;sample++){
            byte[] value=new byte[random.Next(8194)];random.NextBytes(value);values.Add(value);
        }
        string path=Path.Combine(Path.GetTempPath(),"gflat_sha256_"+Guid.NewGuid().ToString("N")+".bin");
        try{
            using(var output=new BinaryWriter(File.Create(path)))foreach(byte[] value in values){
                output.Write((uint)value.Length);output.Write(value);output.Write(SHA256.HashData(value));
            }
            Run($$"""
                extern void*? fopen(readonly(char)* path,readonly(char)* mode) nothrow;
                extern nuint fread(void* data,nuint size,nuint count,void* stream) nothrow;
                extern int fclose(void* stream) nothrow;extern int ferror(void* stream) nothrow;
                bool Matches(byte[32] digest,readonly(byte)* expected) nothrow{
                    for(nuint i=0;i<32;i++)if(digest[i]!=expected[i])return false;return true;
                }
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;
                    uint size=0;char[8195] input;byte[32] expected;int seen=0;
                    while(true){
                        nuint amount=fread((void*)&size,1,4,file!);if(amount==0)break;if(amount!=4)return 2;
                        nuint count=(nuint)size;if(count>8193)return 3;
                        for(nuint i=0;i<8195;i++)input[i]='?';
                        input[0]=(char)165;input[count+1]=(char)90;
                        if(fread((void*)&input[1],1,count,file!)!=count||fread((void*)&expected[0],1,32,file!)!=32)return 4;
                        char[8195] original=input;
                        std::SHA256 whole=new();
                        if(!whole.TryAppend((readonly(char)*)1,0)||!whole.TryAppend(&input[1],count))return 5;
                        if(!Matches(whole.GetHash(),expected)||!Matches(whole.GetHash(),expected))return 6;
                        // Try different boundaries, including one byte at a time and partial blocks.
                        for(int mode=0;mode<3;mode++){
                            std::SHA256 chunked=new();nuint position=0;
                            while(position<count){
                                nuint take=1;if(mode==1)take=position%97+1;if(mode==2)take=64;
                                if(take>count-position)take=count-position;
                                if(!chunked.TryAppend(&input[position+1],take)||!chunked.TryAppend((readonly(char)*)1,0))return 7;
                                position+=take;
                            }
                            if(!Matches(chunked.GetHash(),expected))return 8;
                        }
                        for(nuint i=0;i<8195;i++)if(input[i]!=original[i])return 9;
                        seen++;
                    }
                    if(ferror(file!)!=0||seen!={{values.Count}})return 10;return fclose(file!);
                }
                """,optimization);
        }finally{File.Delete(path);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopiesSnapshotsResetAndOverwrittenCarryKeepIndependentInlineState(int optimization)
    {
        byte[] first=SHA256.HashData("prefix-first"u8.ToArray()),second=SHA256.HashData("prefix-second"u8.ToArray());
        byte[] million=SHA256.HashData(Enumerable.Repeat((byte)'a',1000000).ToArray());
        Assert.Equal("CDC76E5C9914FB9281A1C7E284D73E67F1809A48A497200E046D39CCC7112CD0",Convert.ToHexString(million));
        Run($$"""
            bool Equal(byte[32] first,byte[32] second) nothrow{
                for(nuint i=0;i<32;i++)if(first[i]!=second[i])return false;return true;
            }
            int main(){
                const byte[32] firstExpected={{Bytes(first)}};const byte[32] secondExpected={{Bytes(second)}};
                const byte[32] millionExpected={{Bytes(million)}};
                std::SHA256 hash=new();char[7] prefix=['p','r','e','f','i','x','-'];
                if(!hash.TryAppend(prefix,7))return 1;
                for(nuint i=0;i<7;i++)prefix[i]='?';
                std::SHA256 branch=hash;immutable(std::SHA256) frozen=hash;
                byte[32] prefixHash=hash.GetHash();if(!Equal(prefixHash,frozen.GetHash()))return 2;
                byte[32] changed=prefixHash;changed[0]=(byte)(changed[0]^(byte)255);
                if(!Equal(prefixHash,hash.GetHash())||Equal(changed,hash.GetHash()))return 3;
                if(!hash.TryAppend("first",5)||!branch.TryAppend("second",6))return 4;
                if(!Equal(hash.GetHash(),firstExpected)||!Equal(branch.GetHash(),secondExpected)||!Equal(frozen.GetHash(),prefixHash))return 5;
                hash.Reset();char[1000] block;for(nuint i=0;i<1000;i++)block[i]='a';
                for(int i=0;i<1000;i++)if(!hash.TryAppend(block,1000))return 6;
                if(!Equal(hash.GetHash(),millionExpected))return 7;
                hash.Reset();std::SHA256 empty=new();
                if(!Equal(hash.GetHash(),empty.GetHash())||!hash.TryAppend((readonly(char)*)1,0))return 8;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverflowRejectsBeforeInputAccessAndPreservesDigest(int optimization)
    {
        string limitChecks=IntPtr.Size==8?"""
            if(hash.TryAppend((readonly(char)*)1,(nuint)2305843009213693952ul))return 1;
            if(!Same(hash.GetHash(),empty))return 2;
            if(!hash.TryAppend("abc",3))return 3;byte[32] before=hash.GetHash();
            if(hash.TryAppend((readonly(char)*)1,(nuint)2305843009213693949ul))return 4;
            if(!Same(hash.GetHash(),before))return 5;
            """:"";
        Run($$"""
            bool Same(byte[32] first,byte[32] second) nothrow{
                for(nuint i=0;i<32;i++)if(first[i]!=second[i])return false;return true;
            }
            int main(){
                std::SHA256 hash=new();byte[32] empty=hash.GetHash();
                {{limitChecks}}
                hash.Reset();if(!hash.TryAppend((readonly(char)*)1,0)||!Same(hash.GetHash(),empty))return 6;
                return 0;
            }
            """,optimization);
    }

    private static string Bytes(byte[] bytes)=>"["+string.Join(",",bytes.Select(value=>"(byte)"+value))+"]";
    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static IEnumerable<SourceFile> Sources(string code)
        =>new[]{"Hashing/SHA256.gf","Numerics/BitOperations.gf"}.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("sha256-test.gf",code));
    private static void Run(string code,int optimization)
    {
        string ir=Compiler.Emit(Sources(code),options:new(){NoStdLib=true,NoBuiltins=true});
        Assert.DoesNotContain("@gflat$Memory$",ir);Assert.DoesNotContain("@__gflat_eh_",ir);
        string directory=Path.Combine(Path.GetTempPath(),"gflat_sha256_native_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try{
            string input=Path.Combine(directory,"hash.ll"),output=Path.Combine(directory,"hash.exe");File.WriteAllText(input,ir);
            var compiled=NativeToolchain.Compile(input,output,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(output,[]);Assert.True(result.ExitCode==0,$"SHA256 exit{result.ExitCode}: {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}

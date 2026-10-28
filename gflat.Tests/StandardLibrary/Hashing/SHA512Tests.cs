using System.Security.Cryptography;

namespace gflat.Tests.Std.Hashing;

public sealed class SHA512Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task WholeAndArbitraryChunksMatchPublishedAndDotNetDigests(int optimization)
    {
        byte[][] published=[[],"abc"u8.ToArray(),
            "abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu"u8.ToArray()];
        string[] expected=["CF83E1357EEFB8BDF1542850D66D8007D620E4050B5715DC83F4A921D36CE9CE47D0D13C5D85F2B0FF8318D2877EEC2F63B931BD47417A81A538327AF927DA3E",
            "DDAF35A193617ABACC417349AE20413112E6FA4E89A97EA20A9EEEE64B55D39A2192992A274FC1A836BA3C23A3FEEBBD454D4423643CE80E2A9AC94FA54CA49F",
            "8E959B75DAE313DA8CF4F72814FC143F8F7779C6EB9F7FA17299AEADB6889018501D289E4900F7E4331B99DEC4B5433AC7D329EEB6DD26545E96E55B874BE909"];
        for(int i=0;i<published.Length;i++)Assert.Equal(expected[i],Convert.ToHexString(SHA512.HashData(published[i])));
        List<byte[]> values=[..published,Enumerable.Range(0,256).Select(i=>(byte)i).ToArray()];
        var random=new Random(48169);
        foreach(int length in Enumerable.Range(0,260).Concat([511,512,513,1023,1024,1025,4095,4096,4097,8193])){
            byte[] value=new byte[length];random.NextBytes(value);values.Add(value);
        }
        for(int sample=0;sample<12;sample++){
            byte[] value=new byte[random.Next(8194)];random.NextBytes(value);values.Add(value);
        }
        string directory=Path.Combine(Path.GetTempPath(),"gflat_sha512_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"vectors.bin");
        try{
            using(var output=new BinaryWriter(File.Create(path)))foreach(byte[] value in values){
                output.Write((uint)value.Length);output.Write(value);output.Write(SHA512.HashData(value));
            }
            await Run($$"""
                extern void*? fopen(readonly(char)* path,readonly(char)* mode) nothrow;
                extern nuint fread(void* data,nuint size,nuint count,void* stream) nothrow;
                extern int fclose(void* stream) nothrow;extern int ferror(void* stream) nothrow;
                bool Matches(byte[64] digest,readonly(byte)* expected) nothrow{
                    for(nuint i=0;i<64;i++)if(digest[i]!=expected[i])return false;return true;
                }
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;
                    uint size=0;char[8195] input;byte[64] expected;int seen=0;
                    while(true){
                        nuint amount=fread((void*)&size,1,4,file!);if(amount==0)break;if(amount!=4)return 2;
                        nuint count=(nuint)size;if(count>8193)return 3;
                        for(nuint i=0;i<8195;i++)input[i]='?';
                        input[0]=(char)165;input[count+1]=(char)90;
                        if(fread((void*)&input[1],1,count,file!)!=count||fread((void*)&expected[0],1,64,file!)!=64)return 4;
                        char[8195] original=input;
                        std::Hashing::SHA512 whole=new();
                        if(!whole.TryAppend((readonly(char)*)1,0)||!whole.TryAppend(&input[1],count))return 5;
                        if(!Matches(whole.GetHash(),expected)||!Matches(whole.GetHash(),expected))return 6;
                        // Try different boundaries, including one byte at a time and partial blocks.
                        for(int mode=0;mode<3;mode++){
                            std::Hashing::SHA512 chunked=new();nuint position=0;
                            while(position<count){
                                nuint take=1;if(mode==1)take=position%193+1;if(mode==2)take=128;
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
        }catch(Exception error){
            error.Data["SHA512OracleDirectory"]=directory;
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task CopiesSnapshotsResetAndOverwrittenCarryKeepIndependentInlineState(int optimization)
    {
        byte[] prefix=Enumerable.Range(0,139).Select(i=>(byte)(i*37)).ToArray();
        byte[] first=SHA512.HashData([..prefix,.."first"u8]),second=SHA512.HashData([..prefix,.."second"u8]);
        byte[] million=SHA512.HashData(Enumerable.Repeat((byte)'a',1000000).ToArray());
        Assert.Equal("E718483D0CE769644E2E42C7BC15B4638E1F98B13B2044285632A803AFA973EBDE0FF244877EA60A4CB0432CE577C31BEB009C5C2C49AA2E4EADB217AD8CC09B",Convert.ToHexString(million));
        await Run($$"""
            bool Equal(byte[64] first,byte[64] second) nothrow{
                for(nuint i=0;i<64;i++)if(first[i]!=second[i])return false;return true;
            }
            int main(){
                const byte[64] firstExpected={{Bytes(first)}};const byte[64] secondExpected={{Bytes(second)}};
                const byte[64] millionExpected={{Bytes(million)}};
                std::Hashing::SHA512 hash=new();byte[139] prefix={{Bytes(prefix)}};
                if(!hash.TryAppend((readonly(char)*)(byte*)prefix,139))return 1;
                for(nuint i=0;i<139;i++)prefix[i]=(byte)0;
                std::Hashing::SHA512 branch=hash;immutable(std::Hashing::SHA512) frozen=hash;
                byte[64] prefixHash=hash.GetHash();if(!Equal(prefixHash,frozen.GetHash()))return 2;
                byte[64] changed=prefixHash;changed[0]=(byte)(changed[0]^(byte)255);
                if(!Equal(prefixHash,hash.GetHash())||Equal(changed,hash.GetHash()))return 3;
                if(!hash.TryAppend("first",5)||!branch.TryAppend("second",6))return 4;
                if(!Equal(hash.GetHash(),firstExpected)||!Equal(branch.GetHash(),secondExpected)||!Equal(frozen.GetHash(),prefixHash))return 5;
                hash.Reset();char[1000] block;for(nuint i=0;i<1000;i++)block[i]='a';
                for(int i=0;i<1000;i++)if(!hash.TryAppend(block,1000))return 6;
                if(!Equal(hash.GetHash(),millionExpected))return 7;
                hash.Reset();std::Hashing::SHA512 empty=new();
                if(!Equal(hash.GetHash(),empty.GetHash())||!hash.TryAppend((readonly(char)*)1,0))return 8;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SyntheticCounterCarryAndOverflowPreserveStateBeforeInputAccess(int optimization)
    {
        // Exposing only counter access permits boundary preflight tests. These are
        // synthetic counters, not a claim to have hashed a huge real message.
        await Run("""
            bool Same(byte[64] first,byte[64] second) nothrow{
                for(nuint i=0;i<64;i++)if(first[i]!=second[i])return false;return true;
            }
            bool SameState(readonly(std::Hashing::SHA512)* first,readonly(std::Hashing::SHA512)* second) nothrow{
                readonly(byte)* a=(readonly(byte)*)first;readonly(byte)* b=(readonly(byte)*)second;
                for(nuint i=0;i<(nuint)sizeof(std::Hashing::SHA512);i++)if(a[i]!=b[i])return false;
                return true;
            }
            int main(){
                std::Hashing::SHA512 hash=new();char[127] input;
                for(nuint i=0;i<127;i++)input[i]=(char)i;
                if(!hash.TryAppend(input,124))return 1;
                hash.lengthLow=0xfffffffffffffffcul;hash.lengthHigh=7ul;
                if(!hash.TryAppend(input,4)||hash.lengthLow!=0ul||hash.lengthHigh!=8ul)return 2;
                if(!hash.TryAppend(input,3)||hash.lengthLow!=3ul||hash.lengthHigh!=8ul)return 3;
                hash.Reset();if(hash.lengthLow!=0ul||hash.lengthHigh!=0ul)return 4;
                if(!hash.TryAppend(input,127))return 5;
                hash.lengthLow=0xfffffffffffffffful;hash.lengthHigh=0x1ffffffffffffffful;
                std::Hashing::SHA512 before=hash;byte[64] digest=hash.GetHash();
                if(hash.TryAppend((readonly(char)*)1,1)||!SameState(&hash,&before)||!Same(hash.GetHash(),digest))return 6;
                if(!hash.TryAppend((readonly(char)*)1,0)||!SameState(&hash,&before))return 7;
                // Keep pending bytes consistent with the seeded length modulo 128.
                hash.Reset();if(!hash.TryAppend(input,126))return 8;
                hash.lengthHigh=0x1ffffffffffffffful;hash.lengthLow=0xfffffffffffffffeul;
                if(!hash.TryAppend(input,1)||hash.lengthLow!=0xfffffffffffffffful||hash.lengthHigh!=0x1ffffffffffffffful)return 8;
                before=hash;
                if(hash.TryAppend((readonly(char)*)1,2)||!SameState(&hash,&before))return 9;
                return 0;
            }
            """,optimization,exposeCounters:true);
    }

    private static string Bytes(byte[] bytes)=>"["+string.Join(",",bytes.Select(value=>"(byte)"+value))+"]";
    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static IEnumerable<SourceFile> Sources(string code,bool exposeCounters)
    {
        foreach(string name in new[]{"Hashing/SHA512.gf","Numerics/BitOperations.gf"}) {
            string path=Path.Combine(AppContext.BaseDirectory,"Std/core",name);
            string source=File.ReadAllText(path);
            if(exposeCounters&&name=="Hashing/SHA512.gf") {
                const string low="private ulong lengthLow;",high="private ulong lengthHigh;";
                Assert.Equal(2,source.Split(low).Length);Assert.Equal(2,source.Split(high).Length);
                string fixture=source.Replace(low,"public ulong lengthLow;").Replace(high,"public ulong lengthHigh;");
                Assert.Equal(source,fixture.Replace("public ulong lengthLow;",low).Replace("public ulong lengthHigh;",high));
                source=fixture;
            }
            yield return new SourceFile(path,source);
        }
        yield return new SourceFile("sha512-test.gf",code);
    }
    private static async Task Run(string code,int optimization,bool exposeCounters=false)
    {
        string ir=Compiler.Emit(Sources(code,exposeCounters),options:new(){NoStdLib=true,NoBuiltins=true});
        Assert.DoesNotContain("@gflat$Memory$",ir);Assert.DoesNotContain("@__gflat_eh_",ir);
        string directory=Path.Combine(Path.GetTempPath(),"gflat_sha512_native_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try{
            string input=Path.Combine(directory,"hash.ll"),output=Path.Combine(directory,"hash.exe");File.WriteAllText(input,ir);
            var compiled=NativeToolchain.Compile(input,output,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(output,[]);Assert.True(result.ExitCode==0,$"SHA512 exit{result.ExitCode}: {result.StandardError}");
        }catch(Exception error){
            error.Data["SHA512NativeDirectory"]=directory;
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }
}

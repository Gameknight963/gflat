using System.Security.Cryptography;

namespace gflat.Tests.Std.Hashing;

public sealed class HKDFSHA256Tests
{
    private const string DenyAllocation="""
        using Standard;using Standard::Hashing;using Standard::IO;
        int attempts=0;
        namespace Memory{
            public replace void*? Allocate(nuint bytes){attempts++;return null;}
            public replace void Free(void* storage)nothrow{}
        }
        """;

    private static IEnumerable<(byte[] Salt,byte[] Input,byte[] Info,int Length,string Prk,string Okm)> Vectors()
    {
        yield return (Range(0,13),Enumerable.Repeat((byte)11,22).ToArray(),Range(240,10),42,
            "077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5",
            "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865");
        yield return (Range(96,80),Range(0,80),Range(176,80),82,
            "06a6b88c5853361a06104c9ceb35b45cef760014904671014a193f40c15fc244",
            "b11e398dc80327a1c8e7f78c596a49344f012eda2d4efad8a050cc4c19afa97c59045a99cac7827271cb41c65e590e09da3275600c2f09b8367793a9aca3db71cc30c58179ec3e87c14c01d5c1f3434f1d87");
        yield return ([],Enumerable.Repeat((byte)11,22).ToArray(),[],42,
            "19ef24a32c717b167f33a91d6f648bdf96596776afdb6377ac434c1c293ccb04",
            "8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task RfcAndDotNetOraclesMatchBinaryUnalignedInputsAndAll255Blocks(int optimization)
    {
        var cases=new List<(byte[] Salt,byte[] Input,byte[] Info,int Length)>();
        foreach(var vector in Vectors()) {
            byte[] prk=HKDF.Extract(HashAlgorithmName.SHA256,vector.Input,vector.Salt);
            Assert.Equal(vector.Prk.ToUpperInvariant(),Convert.ToHexString(prk));
            Assert.Equal(vector.Okm.ToUpperInvariant(),Convert.ToHexString(HKDF.Expand(HashAlgorithmName.SHA256,prk,vector.Length,vector.Info)));
            cases.Add((vector.Salt,vector.Input,vector.Info,vector.Length));
        }
        int[] sizes=[0,1,31,32,33,63,64,65,80,256],lengths=[1,31,32,33,42,64,65,82,8159,8160];
        var random=new Random(217013);
        for(int i=0;i<30;i++)cases.Add((RandomBytes(random,sizes[i%sizes.Length]),
            RandomBytes(random,sizes[(i*3+1)%sizes.Length]),RandomBytes(random,sizes[(i*7+2)%sizes.Length]),lengths[i%lengths.Length]));
        cases.Add(([],[],[],32));cases.Add((Range(0,256),Range(0,256),Range(0,256),65));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_hkdf_sha256_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            string path=Path.Combine(directory,"vectors.bin");
            using(var file=new BinaryWriter(File.Create(path)))foreach(var (salt,input,info,length) in cases) {
                byte[] prk=HKDF.Extract(HashAlgorithmName.SHA256,input,salt);
                file.Write((uint)salt.Length);file.Write((uint)input.Length);file.Write((uint)info.Length);file.Write((uint)length);
                file.Write(salt);file.Write(input);file.Write(info);file.Write(prk);file.Write(HKDF.Expand(HashAlgorithmName.SHA256,prk,length,info));
            }
            Run(DenyAllocation+$$"""
                int main(){
                    File input=File::OpenRead({{Literal(path)}});
                    char[258] salt;char[258] material;char[258] info;byte[32] expectedPrk;byte[8160] expected;byte[8162] output;
                    for(int sample=0;sample<{{cases.Count}};sample++){
                        nuint saltCount=(nuint)FileBinary::ReadUInt32LittleEndian(&input);
                        nuint inputCount=(nuint)FileBinary::ReadUInt32LittleEndian(&input);
                        nuint infoCount=(nuint)FileBinary::ReadUInt32LittleEndian(&input);
                        nuint count=(nuint)FileBinary::ReadUInt32LittleEndian(&input);
                        if(saltCount>256||inputCount>256||infoCount>256||count==0||count>8160)return 1;
                        for(nuint i=0;i<258;i++){salt[i]='S';material[i]='M';info[i]='I';}
                        input.ReadExactly(&salt[1],saltCount);input.ReadExactly(&material[1],inputCount);input.ReadExactly(&info[1],infoCount);
                        input.ReadExactly(((byte*)expectedPrk).reinterpret<char*>(),32);input.ReadExactly(((byte*)expected).reinterpret<char*>(),count);
                        char[258] savedSalt=salt;char[258] savedInput=material;char[258] savedInfo=info;
                        byte[32] prk=HKDFSHA256::Extract(&salt[1],saltCount,&material[1],inputCount);
                        for(nuint i=0;i<32;i++)if(prk[i]!=expectedPrk[i])return 2;
                        for(nuint i=0;i<258;i++)if(salt[i]!=savedSalt[i]||material[i]!=savedInput[i])return 3;
                        for(nuint i=0;i<saltCount;i++)salt[i+1]='X';for(nuint i=0;i<inputCount;i++)material[i+1]='Y';
                        byte[32] original=prk;byte[32] changed=prk;changed[0]=(byte)(changed[0]^(byte)255);
                        if(changed[0]==prk[0])return 4;
                        for(nuint i=0;i<8162;i++)output[i]=(byte)165;
                        if(!HKDFSHA256::TryExpand(((byte*)prk).reinterpret<readonly(char)*>(),32,&info[1],infoCount,&output[1],count))return 5;
                        for(nuint i=0;i<count;i++)if(output[i+1]!=expected[i])return 6;
                        for(nuint i=count+1;i<8162;i++)if(output[i]!=(byte)165)return 7;
                        if(output[0]!=(byte)165)return 8;
                        for(nuint i=0;i<32;i++)if(prk[i]!=original[i])return 9;
                        for(nuint i=0;i<258;i++)if(info[i]!=savedInfo[i])return 10;
                    }
                    input.Close();if(attempts!=0)return 11;return 0;
                }
                """,optimization);
        }catch(Exception error){error.Data["HKDFSHA256OracleDirectory"]=directory;throw;}
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrkOutputAliasesAreSafeAndInfoAliasesRejectBeforeReadingPrk(int optimization)
    {
        byte[] prk=Range(1,32),info=[0,128,255];
        byte[] expected=HKDF.Expand(HashAlgorithmName.SHA256,prk,65,info);
        byte[] longer=Range(1,65),longExpected=HKDF.Expand(HashAlgorithmName.SHA256,longer,33,info);
        Run(DenyAllocation+$$"""
            int main(){
                byte[32] prk={{Bytes(prk)}};byte[3] info={{Bytes(info)}};byte[65] expected={{Bytes(expected)}};
                nuint[3] offsets=[8,40,48];
                for(nuint mode=0;mode<3;mode++){
                    byte[128] storage;for(nuint i=0;i<128;i++)storage[i]=(byte)165;
                    for(nuint i=0;i<32;i++)storage[i+40]=prk[i];byte[128] saved=storage;nuint offset=offsets[mode];
                    if(!HKDFSHA256::TryExpand((&storage[40]).reinterpret<readonly(char)*>(),32,((byte*)info).reinterpret<readonly(char)*>(),3,&storage[offset],65))return 1;
                    for(nuint i=0;i<128;i++){
                        if(i>=offset&&i-offset<65){if(storage[i]!=expected[i-offset])return 2;}
                        else if(storage[i]!=saved[i])return 3;
                    }
                }
                byte[65] longPrk={{Bytes(longer)}};byte[33] longExpected={{Bytes(longExpected)}};byte[33] longOutput;
                if(!HKDFSHA256::TryExpand(((byte*)longPrk).reinterpret<readonly(char)*>(),65,((byte*)info).reinterpret<readonly(char)*>(),3,longOutput,33))return 4;
                for(nuint i=0;i<33;i++)if(longOutput[i]!=longExpected[i])return 5;
                byte[80] longStorage;for(nuint i=0;i<80;i++)longStorage[i]=(byte)165;
                for(nuint i=0;i<65;i++)longStorage[i+8]=longPrk[i];byte[80] savedLong=longStorage;
                if(!HKDFSHA256::TryExpand((&longStorage[8]).reinterpret<readonly(char)*>(),65,((byte*)info).reinterpret<readonly(char)*>(),3,&longStorage[8],33))return 11;
                for(nuint i=0;i<80;i++){
                    if(i>=8&&i<41){if(longStorage[i]!=longExpected[i-8])return 12;}
                    else if(longStorage[i]!=savedLong[i])return 13;
                }
                byte[96] storage;for(nuint i=0;i<96;i++)storage[i]=(byte)i;byte[96] saved=storage;
                nuint[3] infoOffsets=[32,40,40];nuint[3] outputOffsets=[40,32,40];
                for(nuint i=0;i<3;i++){
                    if(HKDFSHA256::TryExpand(((nuint)1).reinterpret<readonly(char)*>(),32,(&storage[infoOffsets[i]]).reinterpret<readonly(char)*>(),10,&storage[outputOffsets[i]],33))return 6;
                    if(HKDFSHA256::TryExpand(((nuint)1).reinterpret<readonly(char)*>(),32,(&storage[infoOffsets[i]]).reinterpret<readonly(char)*>(),10,&storage[outputOffsets[i]],16))return 14;
                    for(nuint j=0;j<96;j++)if(storage[j]!=saved[j])return 7;
                }
                // Adjacent, disjoint intervals within one allocation remain accepted.
                byte[33] adjacentExpected={{Bytes(HKDF.Expand(HashAlgorithmName.SHA256,prk,33,Range(0,10)))}};
                for(nuint i=0;i<10;i++)storage[i]=(byte)i;
                if(!HKDFSHA256::TryExpand(((byte*)prk).reinterpret<readonly(char)*>(),32,((byte*)storage).reinterpret<readonly(char)*>(),10,&storage[10],33))return 8;
                for(nuint i=0;i<33;i++)if(storage[i+10]!=adjacentExpected[i])return 9;
                for(nuint i=0;i<10;i++)storage[i+33]=(byte)i;
                if(!HKDFSHA256::TryExpand(((byte*)prk).reinterpret<readonly(char)*>(),32,(&storage[33]).reinterpret<readonly(char)*>(),10,storage,33))return 15;
                for(nuint i=0;i<33;i++)if(storage[i]!=adjacentExpected[i])return 16;
                for(nuint i=0;i<10;i++)if(storage[i+33]!=(byte)i)return 17;
                if(attempts!=0)return 10;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidExpansionPreflightsLeaveOutputUntouchedWithoutInputReads(int optimization)
        => Run(DenyAllocation+"""
            int main(){
                byte[40] output;for(nuint i=0;i<40;i++)output[i]=(byte)165;
                if(HKDFSHA256::TryExpand(((nuint)1).reinterpret<readonly(char)*>(),32,((nuint)1).reinterpret<readonly(char)*>(),0,((nuint)1).reinterpret<byte*>(),0))return 1;
                if(HKDFSHA256::TryExpand(((nuint)1).reinterpret<readonly(char)*>(),32,((nuint)1).reinterpret<readonly(char)*>(),0,((nuint)1).reinterpret<byte*>(),8161))return 2;
                if(HKDFSHA256::TryExpand(((nuint)1).reinterpret<readonly(char)*>(),31,((nuint)1).reinterpret<readonly(char)*>(),0,output,33))return 3;
                if(sizeof(nuint)==8){
                    if(HKDFSHA256::TryExpand(((nuint)1).reinterpret<readonly(char)*>(),(nuint)2305843009213693952ul,((nuint)1).reinterpret<readonly(char)*>(),0,output,33))return 4;
                    if(HKDFSHA256::TryExpand(((nuint)1).reinterpret<readonly(char)*>(),32,((nuint)1).reinterpret<readonly(char)*>(),(nuint)2305843009213693887ul,output,32))return 5;
                    if(HKDFSHA256::TryExpand(((nuint)1).reinterpret<readonly(char)*>(),32,((nuint)1).reinterpret<readonly(char)*>(),(nuint)2305843009213693855ul,output,33))return 6;
                }
                for(nuint i=0;i<40;i++)if(output[i]!=(byte)165)return 7;
                byte[32] empty=HKDFSHA256::Extract(((nuint)1).reinterpret<readonly(char)*>(),0,((nuint)1).reinterpret<readonly(char)*>(),0);
                byte[1] result;
                if(!HKDFSHA256::TryExpand(((byte*)empty).reinterpret<readonly(char)*>(),32,((nuint)1).reinterpret<readonly(char)*>(),0,result,1))return 8;
                if(attempts!=0)return 9;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExcessiveExtractionLengthsThrowBeforeEitherInputAndPreserveAssignment(int optimization)
        => Run("""
            using Standard;using Standard::Hashing;
            int main(){
                byte[32] result;for(nuint i=0;i<32;i++)result[i]=(byte)165;
                if(sizeof(nuint)==8){
                    int caught=0;
                    try{result=HKDFSHA256::Extract(((nuint)1).reinterpret<readonly(char)*>(),(nuint)2305843009213693952ul,((nuint)1).reinterpret<readonly(char)*>(),0);}catch(ref Exception error){caught++;}
                    try{result=HKDFSHA256::Extract(((nuint)1).reinterpret<readonly(char)*>(),1,((nuint)1).reinterpret<readonly(char)*>(),(nuint)2305843009213693888ul);}catch(ref Exception error){caught++;}
                    if(caught!=2)return 1;
                    for(nuint i=0;i<32;i++)if(result[i]!=(byte)165)return 2;
                }
                return 0;
            }
            """,optimization);

    private static byte[] Range(int start,int count)=>Enumerable.Range(start,count).Select(i=>(byte)i).ToArray();
    private static byte[] RandomBytes(Random random,int count){byte[] result=new byte[count];random.NextBytes(result);return result;}
    private static string Bytes(byte[] bytes)=>"["+string.Join(",",bytes.Select(value=>"(byte)"+value))+"]";
    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static void Run(string code,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("hkdf-sha256.gf",code));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"HKDFSHA256 exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

namespace gflat.Tests.Std.Text;

public sealed class StringTextEqualityTests
{
    // Deliberately excludes CString.gf: String text equality needs no new source dependency.
    private static IEnumerable<SourceFile> Sources(string program)
        => new[] { "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "Collections/HashMap.gf",
            "IStringConvertable.gf", "Text/String.gf", "Text/ByteSearch.gf" }
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Standard/core",name))))
            .Append(new SourceFile("string-text-equality.gf",program));

    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedAndCStringComparisonsMatchIndependentByteOracle(int optimization)
    {
        var cases=new List<(byte[] Owned,byte[] Input)> {
            ([],[]),([],[1]),([0],[]),([97,0,98],[97,0,98]),([97,0,98],[97]),
            ([128,255],[128,255]),([128,255],[128,254]),([97],[97,0,98])
        };
        var random=new Random(64019);
        foreach(int length in new[]{0,1,2,31,32,63,64,65})
        {
            byte[] bytes=new byte[length];random.NextBytes(bytes);
            if(length>0)bytes[length/2]=0;
            AddVariants(bytes);
        }
        AddVariants(Enumerable.Range(0,256).Select(i=>(byte)i).ToArray());
        AddVariants(Enumerable.Repeat((byte)'x',257).ToArray());
        for(int sample=0;sample<16;sample++)
        {
            byte[] bytes=new byte[random.Next(1,33)];random.NextBytes(bytes);
            for(int i=0;i<bytes.Length;i++)if(bytes[i]==0)bytes[i]=1;
            AddVariants(bytes);
        }
        void AddVariants(byte[] bytes)
        {
            cases.Add((bytes,bytes));cases.Add((bytes,[..bytes,1]));
            cases.Add((bytes,bytes.Length==0?[1]:bytes[..^1]));
            if(bytes.Length>0){byte[] changed=(byte[])bytes.Clone();changed[bytes.Length/2]^=255;cases.Add((bytes,changed));}
        }
        var ownerBytes=new List<byte>();var inputBytes=new List<byte>();
        var ownerOffsets=new List<int>();var inputOffsets=new List<int>();
        foreach(var (owned,input) in cases)
        {
            ownerOffsets.Add(ownerBytes.Count);ownerBytes.AddRange(owned);ownerBytes.Add(0);
            inputOffsets.Add(inputBytes.Count);inputBytes.AddRange(input);inputBytes.Add(0);
        }
        string Nuints(IEnumerable<int> values)=>string.Join(",",values.Select(value=>"(nuint)"+value));
        string Chars(IEnumerable<byte> values)=>string.Join(",",values.Select(value=>"(char)"+value));
        string Bools(IEnumerable<bool> values)=>string.Join(",",values.Select(value=>value?"true":"false"));
        string code=$$"""
            using Standard; using Standard::Text; using Standard::Collections;
            int main(){
                char[{{ownerBytes.Count}}] owners=[{{Chars(ownerBytes)}}];
                char[{{inputBytes.Count}}] inputs=[{{Chars(inputBytes)}}];
                const char[{{inputBytes.Count}}] originalInputs=[{{Chars(inputBytes)}}];
                nuint[{{cases.Count}}] ownerOffsets=[{{Nuints(ownerOffsets)}}];
                nuint[{{cases.Count}}] inputOffsets=[{{Nuints(inputOffsets)}}];
                nuint[{{cases.Count}}] ownerLengths=[{{Nuints(cases.Select(item=>item.Owned.Length))}}];
                nuint[{{cases.Count}}] inputLengths=[{{Nuints(cases.Select(item=>item.Input.Length))}}];
                bool[{{cases.Count}}] counted=[{{Bools(cases.Select(item=>item.Owned.AsSpan().SequenceEqual(item.Input)))}}];
                bool[{{cases.Count}}] terminated=[{{Bools(cases.Select(item=>item.Owned.SequenceEqual(item.Input.TakeWhile(value=>value!=0))))}}];
                for(nuint i=0;i<{{cases.Count}};i++){
                    readonly(char)* source=&owners[ownerOffsets[i]];readonly(char)* text=&inputs[inputOffsets[i]];
                    String owned=String::From(source,ownerLengths[i]);immutable(String)* view=&owned;
                    if(owned.Equals(text,inputLengths[i])!=counted[i]||view.Equals(text,inputLengths[i])!=counted[i])return 1;
                    if(owned.Equals(text)!=terminated[i]||view.Equals(text)!=terminated[i])return 2;
                    if(owned.Length!=ownerLengths[i]||owned.Data[owned.Length]!='\0')return 3;
                    for(nuint j=0;j<owned.Length;j++)if(owned.Data[j]!=source[j])return 4;
                }
                for(nuint i=0;i<{{inputBytes.Count}};i++)if(inputs[i]!=originalInputs[i])return 5;
                return 0;
            }
            """;
        Run(code,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyMismatchAliasedStorageAndExistingInterfacesNeedNoComparisonAllocation(int optimization)
        => Run("""
            using Standard; using Standard::Text; using Standard::Collections;
            int attempts=0;bool deny=false;
            extern void*? malloc(nuint bytes);extern void free(void* storage);
            namespace Memory {
                public replace void*? Allocate(nuint bytes){attempts++;if(deny)return null;return malloc(bytes);}
                public replace void Free(void* storage) nothrow {free(storage);}
            }
            bool Contract<T:IEquatable<T>>(immutable(T)* left,immutable(T)* right)=>left.Equals(right);
            int main(){
                String empty=new();String value=s"a\0b";String same=s"a\0b";String prefix=s"a";
                String original=s"abcd";String suffix=s"bcd";
                HashMap<String,int> map=new();map.Add(value,42);
                immutable(String)* view=&value;IEquatable<String>* equality=&value;
                int baseline=attempts;deny=true;
                if(!empty.Equals(((nuint)1).reinterpret<readonly(char)*>(),0)||empty.Equals(((nuint)1).reinterpret<readonly(char)*>(),1))return 1;
                if(value.Equals(((nuint)1).reinterpret<readonly(char)*>(),0)||value.Equals(((nuint)1).reinterpret<readonly(char)*>(),2))return 2;
                if(value.Equals(((nuint)1).reinterpret<readonly(char)*>(),(nuint)0-(nuint)1))return 3;
                if(!empty.Equals("")||value.Equals(value.Data)||!value.Equals(value.Data,value.Length))return 4;
                if(!prefix.Equals(value.Data)||!prefix.Equals(value.Data,1)||prefix.Equals(value.Data,3))return 5;
                if(!original.Equals(original.Data)||!suffix.Equals(original.Data+1,3)||!suffix.Equals(original.Data+1))return 6;
                if(!view.Equals(value.Data,3)||view.Equals(value.Data)||!view.Equals(&same))return 7;
                if(!equality.Equals(&same)||!Contract<String>(&value,&same)||Contract<String>(&value,&prefix))return 8;
                if(map[&same]!=42||!map.ContainsKey(&value)||map.ContainsKey(&prefix))return 9;
                if(attempts!=baseline||value.Length!=3||value.Data[1]!='\0'||value.Data[2]!='b')return 10;
                deny=false;return 0;
            }
            """,optimization);
}

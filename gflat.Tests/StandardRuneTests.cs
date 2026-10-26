namespace gflat.Tests;

public sealed class StandardRuneTests
{
    private const string Allocator="""
        using std; using std::Text; using std::Queries; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int allocations=0;int releases=0;int attempts=0;int failAt=0;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(failAt!=0&&attempts==failAt)return null;void*? value=malloc(count);if(value!=null)allocations++;return value;}
            public replace void Free(void* storage)nothrow{releases++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidScalarsLengthsOrderingAndBorrowedUtf8FormattingMatchDotNetWithoutAllocation(int optimization)
    {
        uint[] scalars=Scalars(300);var encoded=Bytes(scalars);
        Run($$"""
            using std; using std::Text; using std::Queries; using std::Collections;int allocations=0;
            namespace Memory{public replace void*? Allocate(nuint count){allocations++;return null;}public replace void Free(void* storage)nothrow{} }
            int main(){
                uint[{{scalars.Length}}] scalars=[{{string.Join(',',scalars.Select(value=>value+"u"))}}];
                int[{{scalars.Length}}] utf8=[{{string.Join(',',scalars.Select(value=>new System.Text.Rune((int)value).Utf8SequenceLength))}}];
                int[{{scalars.Length}}] utf16=[{{string.Join(',',scalars.Select(value=>new System.Text.Rune((int)value).Utf16SequenceLength))}}];
                byte[{{encoded.Length}}] expected=[{{string.Join(',',encoded.Select(value=>"(byte)"+value))}}];char[8] output;
                Rune zero=new();if(zero.Value!=0u||!zero.IsAscii||zero.Utf8Length!=1||zero.Utf16Length!=1)return 1;
                if(sizeof(Rune)!=4||alignof(Rune)!=alignof(uint))return 2;
                for(nuint i=0;i<{{scalars.Length}};i++){
                    immutable(Rune) value=new(scalars[i]);Rune equal=new(scalars[i]);
                    if(value.Value!=scalars[i]||value.IsAscii!=(scalars[i]<128u)||value.Utf8Length!=(nuint)utf8[i]||value.Utf16Length!=(nuint)utf16[i])return 3;
                    if(!value.Equals(&equal)||value.CompareTo(&equal)!=0||value.GetHashCode()!=equal.GetHashCode())return 4;
                    if(i+1<{{scalars.Length}}){Rune next=new(scalars[i+1]);if(value.CompareTo(&next)!=-1||next.CompareTo(&value)!=1)return 5;}
                    for(nuint at=0;at<8;at++)output[at]='Z';BufferWriter writer=new(&output[1],5);value.Format(&writer);
                    if(writer.Length!=value.Utf8Length||output[0]!='Z'||output[6]!='Z'||output[7]!='Z'||output[1+writer.Length]!='\0')return 6;
                    for(nuint at=0;at<writer.Length;at++)if((byte)output[1+at]!=expected[i*4+at])return 7;
                }
                if(allocations!=0)return 8;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningStringsInterpolationAndHashedKeysPreserveScalarGlyphsIncludingNul(int optimization)
    {
        uint[] scalars=Scalars(24);byte[] expected=Bytes(scalars);
        Run(Allocator+$$"""
            Rune Wrap(uint value)=>new Rune(value);
            int main(){
                uint[{{scalars.Length}}] scalars=[{{string.Join(',',scalars.Select(value=>value+"u"))}}];byte[{{expected.Length}}] expected=[{{string.Join(',',expected.Select(value=>"(byte)"+value))}}];
                {
                    HashMap<Rune,int> map=new();HashSet<Rune> set=new();
                    for(nuint i=0;i<{{scalars.Length}};i++){
                        Rune value=new(scalars[i]);map.Add(value,(int)i);if(!set.Add(value)||set.Add(value))return 1;
                        String first=value.ToString();String second=value.ToString();String interpolation=s$"[{value}]";
                        if(first.Length!=value.Utf8Length||second.Length!=first.Length||first.Data==second.Data||interpolation.Length!=first.Length+2)return 2;
                        if(interpolation[0]!='['||interpolation[interpolation.Length-1]!=']')return 3;
                        for(nuint at=0;at<first.Length;at++)if((byte)first[at]!=expected[i*4+at]||second[at]!=first[at]||interpolation[1+at]!=first[at])return 4;
                        char original=second[0];first[0]='X';if(second[0]!=original||value.Value!=scalars[i])return 5;
                        Rune same=new(scalars[i]);if(map[&same]!=(int)i||!set.Contains(&same))return 6;
                    }
                    if(map.Count!={{scalars.Length}}||set.Count!={{scalars.Length}})return 7;
                    Rune zero=new();String nul=zero.ToString();if(nul.Length!=1||nul[0]!='\0'||nul.Data[1]!='\0')return 8;
                    byte[6] input=[(byte)65,(byte)0,(byte)240,(byte)159,(byte)152,(byte)128];Utf8Scalars decoded=new((char*)&input[0],6);
                    uint[3] expectedScalars=[65u,0u,128512u];nuint seen=0;
                    foreach(Rune value in decoded.AsEnumerable().Select(&Wrap)){
                        if(seen>=3||value.Value!=expectedScalars[seen])return 9;seen++;
                    }
                    if(seen!=3)return 10;
                }
                if(allocations!=releases)return 11;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidConstructionWriterFailuresAndEveryStringAllocationFailurePreserveTheValue(int optimization)
        =>Run(Allocator+"""
            struct FailWriter:ITextWriter{
                public int Calls;public nuint Count;public byte First;
                public FailWriter(){Calls=0;Count=0;First=0;}
                public void Write(readonly(char)* data,nuint count){Calls++;Count=count;First=(byte)data[0];throw new Exception("writer",73);}
            }
            int main(){
                uint[8] invalid=[55296u,55297u,56319u,56320u,57342u,57343u,1114112u,4294967295u];Rune value=new(128578u);
                for(nuint i=0;i<8;i++){
                    bool caught=false;try{value=new Rune(invalid[i]);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Invalid Unicode scalar value"))return 1;caught=true;}
                    if(!caught||value.Value!=128578u)return 2;
                }
                uint[10] boundaries=[0u,127u,128u,2047u,2048u,55295u,57344u,65535u,65536u,1114111u];char[8] output;
                for(nuint i=0;i<10;i++)for(nuint capacity=0;capacity<=5;capacity++){
                    Rune scalar=new(boundaries[i]);for(nuint at=0;at<8;at++)output[at]='Z';BufferWriter writer=new(&output[1],capacity);bool caught=false;
                    try{scalar.Format(&writer);}catch(ref BufferTooSmallException error){caught=true;}
                    bool fits=capacity>scalar.Utf8Length;
                    if(caught==fits||output[0]!='Z'||output[6]!='Z'||output[7]!='Z'||scalar.Value!=boundaries[i])return 3;
                    if(!fits){if(writer.Length!=0)return 4;for(nuint at=1;at<6;at++){char expected='Z';if(capacity!=0&&at==1)expected='\0';if(output[at]!=expected)return 5;}}
                    else if(writer.Length!=scalar.Utf8Length||output[1+writer.Length]!='\0')return 6;
                }
                FailWriter sink=new();bool caught=false;try{value.Format(&sink);}catch(ref Exception error){if(error.GetCode()!=73)return 7;caught=true;}
                if(!caught||sink.Calls!=1||sink.Count!=4||sink.First!=240||value.Value!=128578u)return 8;
                {
                    String destination=s"unchanged";readonly(char)* identity=destination.Data;int baseline=allocations-releases;int points=0;
                    attempts=0;{String measured=value.ToString();points=attempts;if(measured.Length!=4)return 9;}
                    if(points==0||allocations-releases!=baseline)return 10;
                    for(int failure=1;failure<=points;failure++){
                        attempts=0;failAt=failure;caught=false;
                        try{destination=value.ToString();}catch(ref BadAllocException error){caught=true;}failAt=0;
                        if(!caught||value.Value!=128578u||destination.Data!=identity||!CString::Equals(destination.Data,"unchanged")||allocations-releases!=baseline)return 11;
                    }
                }
                if(allocations!=releases)return 12;return 0;
            }
            """,optimization);

    private static uint[] Scalars(int count)
    {
        var values=new SortedSet<uint>{0,1,0x7f,0x80,0x7ff,0x800,0xd7ff,0xe000,0xffff,0x10000,0x1f600,0x10ffff};
        var random=new Random(519823);while(count-->0){int scalar;do{scalar=random.Next(0x110000);}while(!System.Text.Rune.IsValid(scalar));values.Add((uint)scalar);}
        return values.ToArray();
    }
    private static byte[] Bytes(uint[] scalars)
    {
        byte[] result=new byte[scalars.Length*4];
        for(int i=0;i<scalars.Length;i++)new System.Text.Rune((int)scalars[i]).EncodeToUtf8(result.AsSpan(i*4,4));
        return result;
    }
    private static void Run(string source,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("rune.gf",source));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Rune exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

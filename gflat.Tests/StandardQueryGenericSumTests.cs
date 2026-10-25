using System.Text;

namespace gflat.Tests;

public sealed class StandardQueryGenericSumTests
{
    private const string Allocator="""
        using std; using std::Queries; using std::Collections;extern void*? malloc(nuint size);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint size){attempts++;if(deny)return null;void*? result=malloc(size);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImplicitIntegerValuesMatchIndependentSequentialCheckedLongFold(int optimization)
    {
        var rows=new List<(bool Ints,long[] Values)> {
            (true,[]),(false,[]),(true,[int.MaxValue,1]),(true,[int.MinValue,-1]),
            (true,[int.MaxValue,int.MaxValue,int.MinValue]),(true,[int.MinValue,int.MinValue,int.MaxValue]),
            (false,[long.MaxValue]),(false,[long.MinValue]),(false,[long.MaxValue,1,-1]),
            (false,[long.MinValue,-1,1]),(false,[long.MinValue,long.MaxValue]),
            (false,[long.MaxValue,-1,1]),(false,[long.MinValue,1,-1]),
            (false,[0,long.MinValue]),(false,[long.MaxValue,long.MinValue,1])
        };
        var random=new Random(641610);
        for(int row=0;row<40;row++){
            bool ints=row%2==0;long[] values=Enumerable.Range(0,random.Next(0,24))
                .Select(_=>ints?random.NextInt64(int.MinValue,(long)int.MaxValue+1):random.NextInt64()).ToArray();
            if(!ints)for(int i=1;i<values.Length;i+=2)values[i]=-values[i];rows.Add((ints,values));
        }
        using var stream=new MemoryStream();
        using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){
            writer.Write((ulong)rows.Count);
            foreach(var row in rows){
                long expected=0;bool valid=true;
                try{foreach(long value in row.Values)expected=checked(expected+value);}catch(OverflowException){valid=false;}
                writer.Write((ulong)(row.Ints?1:0));writer.Write((ulong)row.Values.Length);
                foreach(long value in row.Values)writer.Write(value);writer.Write((ulong)(valid?1:0));writer.Write(expected);
            }
        }
        string packed=Convert.ToHexString(stream.ToArray());
        Run(Allocator+$$"""
            uint Hex(char value)nothrow{if(value<='9')return(uint)(value-'0');return(uint)(value-'A')+10u;}
            ulong Read(readonly(char)* data,ref nuint offset)nothrow{
                ulong value=0ul;for(nuint i=0;i<8;i++){ulong next=(ulong)(Hex(data[offset])*16u+Hex(data[offset+1]));offset+=2;value=value|(next<<(i*8));}return value;
            }
            int Tag(int value)=>2;int Tag(long value)=>1;
            int main(){readonly(char)* packed="{{packed}}";nuint offset=0;nuint rows=(nuint)Read(packed,ref offset);
                int[24] ints;long[24] longs;
                for(nuint row=0;row<rows;row++){
                    bool narrow=Read(packed,ref offset)!=0ul;nuint count=(nuint)Read(packed,ref offset);
                    for(nuint i=0;i<count;i++){long value=(long)Read(packed,ref offset);longs[i]=value;if(narrow)ints[i]=(int)value;}
                    bool valid=Read(packed,ref offset)!=0ul;long expected=(long)Read(packed,ref offset);
                    Span<int> first=new(ints,count);Span<long> second=new(longs,count);
                    if(valid){
                        int before=attempts;deny=true;long result=0l;
                        if(narrow){
                            result=first.AsEnumerable().Sum();if(Tag(result)!=1||Tag(first.AsEnumerable().Sum())!=1)return 1;
                            if(std::Queries::Sum<int,Span<int>,Span<int>::Enumerator>(first.AsEnumerable())!=expected)return 2;
                        }else{
                            result=second.AsEnumerable().Sum();if(Tag(second.AsEnumerable().Sum())!=1)return 3;
                            if(std::Queries::Sum<long,Span<long>,Span<long>::Enumerator>(second.AsEnumerable())!=expected)return 4;
                        }
                        deny=false;if(result!=expected||attempts!=before)return 5;
                    }else{
                        bool caught=false;try{second.AsEnumerable().Sum();}
                        catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Sequence sum overflow"))return 6;caught=true;}
                        if(!caught||allocated!=released)return 7;
                    }
                }
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CompositionAndExistingRangeSumsAgreeAcrossIntegerBoundaries(int optimization)
        =>Run(Allocator+$$"""
            bool Even(int value)nothrow=>value%2==0;
            long Widen(int value)nothrow=>(long)value;
            int main(){
                int[5] starts=[-2147483647-1,-4,0,10,2147483644];
                int before=attempts;deny=true;
                {{QualifiedCases()}}
                for(nuint row=0;row<5;row++)for(int count=0;count<=4;count++){
                    IntRange range=new(starts[row],count);long expected=0l;long filteredExpected=0l;
                    for(int i=0;i<count;i++){int value=starts[row]+i;expected+=(long)value;if(Even(value))filteredExpected+=(long)value;}
                    if(range.Sum()!=expected||range.AsEnumerable().Sum()!=expected)return 1;
                    FilteredIntRange filtered=range.Where(&Even);
                    if(filtered.Sum()!=filteredExpected||filtered.AsEnumerable().Sum()!=filteredExpected)return 2;
                    if(range.AsEnumerable().Where(&Even).Select(&Widen).Take(4).Sum()!=filteredExpected)return 3;
                    if(range.AsEnumerable().Take(0).Sum()!=0l)return 4;
                }
                int[4] values=[2147483647,2147483647,-1,2];Span<int> view=new(values,4);
                if(view.AsEnumerable().Take(2).Sum()!=4294967294l)return 5;
                if(view.AsEnumerable().Skip(1).Take(2).Sum()!=2147483646l)return 6;
                deny=false;if(attempts!=before)return 7;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SourceCopyAndNoncopyableCursorFailuresCleanUpAndReadEachCurrentOnce(int optimization)
        =>Run(Allocator+"""
            int mode=0;int live=0;int starts=0;int ends=0;int moves=0;int reads=0;
            struct Source{
                private readonly(long)* values;private String token;
                public Source(readonly(long)* input){values=input;token=s"source";live++;}
                public copy Source(immutable(Source)* other){values=other.values;token=other.token;if(mode==1)throw new Exception("copy",71);live++;}
                ~Source(){live--;}
                public struct Cursor{
                    private readonly(long)* values;private int position;private String token;
                    public Cursor(readonly(long)* input){values=input;position=0;token=s"cursor";if(mode==2)throw new Exception("construct",72);starts++;}
                    public bool MoveNext(){moves++;if(mode==3&&position==1)throw new Exception("move",73);if(position==3)return false;position++;return true;}
                    public long Current{get{reads++;if(mode==4&&position==2)throw new Exception("current",74);return values[position-1];}}
                    ~Cursor(){ends++;}
                }
                public Cursor GetEnumerator()=>new Cursor(values);
            }
            int main(){
                long[3] values=[3l,1l,4l];
                {
                    var query=new Enumerable<long,Source,Source::Cursor>(new Source(values));int baseline=allocated-released;
                    if(query.Sum()!=8l||moves!=4||reads!=3||starts!=ends||live!=1||allocated-released!=baseline)return 1;
                    for(int failure=1;failure<=5;failure++){
                        mode=failure;moves=0;reads=0;bool caught=false;
                        if(failure==5){values[0]=9223372036854775807l;values[1]=1l;values[2]=-1l;}
                        try{query.Sum();}
                        catch(ref Exception error){
                            if(failure<5&&error.GetCode()!=70+failure)return 2;
                            if(failure==5&&!CString::Equals(error.GetMessage(),"Sequence sum overflow"))return 3;caught=true;
                        }
                        mode=0;
                        if(!caught||starts!=ends||live!=1||allocated-released!=baseline)return 4;
                        if(failure<=2&&(moves!=0||reads!=0))return 5;
                        if(failure==3&&(moves!=2||reads!=1))return 6;
                        if(failure>=4&&(moves!=2||reads!=2))return 7;
                        if(failure==5&&(values[0]!=9223372036854775807l||values[1]!=1l||values[2]!=-1l))return 8;
                        values[0]=3l;values[1]=1l;values[2]=4l;moves=0;reads=0;
                        if(query.Sum()!=8l||moves!=4||reads!=3||starts!=ends||live!=1||allocated-released!=baseline)return 9;
                    }
                }
                if(live!=0||allocated!=released)return 10;return 0;
            }
            """,optimization);

    private static string QualifiedCases()
    {
        (string Type,string First,string Second,string Expected)[] cases=[
            ("byte","255","1","256l"),("sbyte","127","1","128l"),
            ("short","32767","1","32768l"),("ushort","65535","1","65536l"),
            ("char","(char)127","(char)1","128l"),("int","2147483647","1","2147483648l"),
            ("uint","4294967295u","1u","4294967296l"),
            ("long","9223372036854775807l","-1l","9223372036854775806l"),
            ("nint","(nint)2147483647","(nint)1","2147483648l")];
        return string.Join('\n',cases.Select(row=>$$"""
            { {{row.Type}}[2] input=[{{row.First}},{{row.Second}}];Span<{{row.Type}}> samples=new(input,2);
              Span<readonly({{row.Type}})> shallow=samples.AsReadOnly();Span<immutable({{row.Type}})> deep=samples.AsImmutable();
              if(samples.AsEnumerable().Sum()!={{row.Expected}}||shallow.AsEnumerable().Sum()!={{row.Expected}}||deep.AsEnumerable().Sum()!={{row.Expected}})return 20;
              if(std::Queries::Sum<readonly({{row.Type}}),Span<readonly({{row.Type}})>,Span<readonly({{row.Type}})>::Enumerator>(shallow.AsEnumerable())!={{row.Expected}})return 21;
              if(deep.AsEnumerable().Take(0).Sum()!=0l)return 22; }
            """));
    }

    [Fact]
    public void UnsupportedElementsFailOrdinaryImplicitLongConversionDuringSpecialization()
    {
        string[] dependencies =
        [
            "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Queries/EnumerableSum.gf",
            "Span.gf", "IStringConvertable.gf", "IHashable.gf", "IEquatable.gf", "IComparable.gf",
            "Hashing.gf", "String.gf", "ByteSearch.gf"
        ];
        SourceFile[] library = dependencies.Select(relative =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Std", "core", relative);
            return new SourceFile(path, File.ReadAllText(path));
        }).ToArray();

        Compiler.Check(Inputs("int", "1"));
        (string Type, string Value)[] cases =
        [
            ("float", "1.0f"), ("double", "1.0"), ("ulong", "1ul"), ("nuint", "(nuint)1"),
            ("extralong", "(extralong)1"), ("bool", "true"), ("Code", "Code::One"), ("String", "s\"text\"")
        ];
        foreach (var row in cases)
        {
            var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(
                () => Compiler.Check(Inputs(row.Type, row.Value)));
            Assert.Contains("Cannot assign", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("long", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        IEnumerable<SourceFile> Inputs(string type, string value)
            => library.Append(new SourceFile("query-sum.gf", $$"""
                using std; using std::Queries; using std::Collections;
                enum Code : int { One = 1 }
                int main() {
                    {{type}}[1] values = [{{value}}];
                    Span<{{type}}> view = new(values, 1);
                    view.AsEnumerable().Sum();
                    return 0;
                }
                """));
    }

    private static IEnumerable<SourceFile> Sources(string code)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-sum.gf",code));
    private static void Run(string code,int optimization)=>Assert.Equal(0,CompilerTestHelper.Run(Sources(code),optimization).ExitCode);
}


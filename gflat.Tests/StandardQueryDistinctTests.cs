using System.Text;

namespace gflat.Tests;

public sealed class StandardQueryDistinctTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("query-distinct.gf",program));

    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Distinct exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    private const string Allocator="""
        using Standard; using Standard::Text; using Standard::IO; using Standard::Memory; using Standard::Queries; using Standard::Collections;extern void*? malloc(nuint bytes);extern void free(void* storage);
        int allocations=0;int releases=0;int attempts=0;int failAt=0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes){
                attempts++;if(failAt!=0&&attempts==failAt)return null;
                void*? result=malloc(bytes);if(result!=null)allocations++;return result;
            }
            public replace void Free(void* storage) nothrow{releases++;free(storage);}
        }
        """;

    private const string OwningKeys="""
        int mode=0;int live=0;int created=0;int destroyed=0;int active=0;
        int copies=0;int hashes=0;int comparisons=0;int projections=0;
        struct Key:IHashable,IEquatable<Key> {
            public int Id;private String label;
            public Key(int id,String text){Id=id;label=move text;live++;}
            public copy Key(immutable(Key)* source){
                Id=source.Id;label=source.label;copies++;
                if(mode==7&&copies==3)throw new Exception("copy",77);
                live++;
            }
            public readonly(char)* LabelData {immutable get=>label.Data;}
            public nuint LabelLength {immutable get=>label.Length;}
            public void ChangeFirst(char value){label[0]=value;}
            public immutable nuint GetHashCode(){hashes++;if(mode==5&&hashes==3)throw new Exception("hash",75);return 0;}
            public immutable bool Equals(immutable(Key)* other){comparisons++;if(mode==6&&comparisons==3)throw new Exception("equality",76);return Id==other.Id;}
            ~Key(){live--;}
        }
        struct Source {
            private Span<Key> values;
            public Source(Span<Key> source){values=source;}
            public struct Iterator {
                private Span<Key> values;private nuint position;private String token;
                public Iterator(Span<Key> source){
                    values=source;position=0;token=s"iterator";
                    if(mode==1)throw new Exception("iterator construction",71);
                    created++;active++;
                }
                public bool MoveNext(){
                    if(mode==2&&position==2)throw new Exception("advance",72);
                    if(position==values.Length)return false;position++;return true;
                }
                public ref Key Current {get{if(mode==3&&position==3)throw new Exception("current",73);return values[position-1];}}
                ~Iterator(){destroyed++;active--;}
            }
            public Iterator GetEnumerator()=>new Iterator(values);
        }
        Key Project(Key value){projections++;if(mode==4&&projections==3)throw new Exception("selector",74);return move value;}
        """;

    [Fact]
    public void UnusedDistinctDefinitionsDoNotRequireHashImplementationInSelectiveCollectionFixtures()
    {
        string root=Path.Combine(AppContext.BaseDirectory,"Standard/core");
        string[] files=["Memory/span_copy.gf","Memory/span_fill.gf","Memory/Span.gf","Text/String.gf","Text/ByteSearch.gf","IStringConvertable.gf",
            "IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf"];
        var sources=Directory.GetFiles(Path.Combine(root,"Collections"),"*.gf")
            .Concat(Directory.GetFiles(Path.Combine(root,"Queries"),"*.gf"))
            .Concat(files.Select(file=>Path.Combine(root,file)))
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("selective.gf","""
                using Standard; using Standard::Text; using Standard::IO; using Standard::Memory; using Standard::Queries; using Standard::Collections;int Project(int value)=>value+1;
                int main(){int[3] data=[1,2,3];Span<int> source=new(data,3);return (int)source.AsEnumerable().Select(Project).Take(1).Count();}
                """));
        string ir=Compiler.Emit(sources);
        Assert.Contains("@main",ir);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningInputIteratorAndSeenSetCleanUpForEarlyAndEmptyTerminals(int optimization)
        =>Run(Allocator+"""
            int created=0;int destroyed=0;int active=0;int moves=0;int reads=0;
            struct Source {
                public struct Iterator {
                    private String token;private int index;
                    public Iterator(){token=s"iterator";index=0;created++;active++;}
                    public bool MoveNext(){moves++;if(index==5)return false;index++;return true;}
                    public int Current {get{reads++;if(index==1||index==3)return 3;if(index==2||index==5)return 1;return 2;}}
                    ~Iterator(){destroyed++;active--;}
                }
                public Iterator GetEnumerator()=>new Iterator();
            }
            int main(){
                var query=new Enumerable<int,Source,Source::Iterator>(new Source()).Distinct();
                if(allocations!=0||created!=0)return 1;
                if(query.Take(0).Any()||query.Take(0).Count()!=0)return 2;
                if(moves!=0||reads!=0||active!=0||created!=destroyed||allocations!=releases)return 3;
                if(!query.Any()||active!=0||created!=destroyed||allocations!=releases)return 4;
                if(query.Take(2).Count()!=2||active!=0||created!=destroyed||allocations!=releases)return 5;
                if(query.Count()!=3||active!=0||created!=destroyed||allocations!=releases)return 6;
                {
                    var iterator=query.GetEnumerator();
                    if(active!=1)return 7;
                    if(!iterator.MoveNext()||iterator.Current!=3)return 8;
                    var moved=move iterator;
                    if(!moved.MoveNext()||moved.Current!=1)return 9;
                }
                if(active!=0||created!=destroyed||allocations!=releases)return 10;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StableProjectedValuesMatchIndependentFirstSeenOrderAcrossRepeatedEnumerations(int optimization)
    {
        string path=Path.Combine(Path.GetTempPath(),"gflat-distinct-"+Guid.NewGuid().ToString("N")+".bin");
        try
        {
            var random=new Random(64003);
            using(var fixture=new BinaryWriter(File.Create(path)))
                for(int sample=0;sample<160;sample++)
                {
                    int[] values=Enumerable.Range(0,sample%129).Select(_=>random.Next(-100,101)).ToArray();
                    int skip=sample%5,take=sample%13;
                    int[] expected=values.Where(value=>value%2==0).Select(value=>value%17).Distinct().Skip(skip).Take(take).ToArray();
                    fixture.Write(values.Length);fixture.Write(skip);fixture.Write(take);fixture.Write(expected.Length);
                    foreach(int value in values)fixture.Write(value);foreach(int value in expected)fixture.Write(value);
                }
            Run(Allocator+"""
                extern void* fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);
                bool Keep(int value)=>value%2==0;int Project(int value)=>value%17;
                int main(){
                    void* stream=fopen("PATH","rb");int[4] header;int[128] values;int[128] expected;
                    for(int sample=0;sample<160;sample++){
                        if(fread((void*)(int*)header,16,1,stream)!=1)return 1;
                        for(nuint i=0;i<128;i++)values[i]=0;
                        nuint count=(nuint)header[0];nuint expectedCount=(nuint)header[3];
                        if(fread((void*)(int*)values,4,count,stream)!=count)return 2;
                        if(fread((void*)(int*)expected,4,expectedCount,stream)!=expectedCount)return 3;
                        int[128] original=values;
                        {
                            Span<int> input=new(values,count);int before=attempts;
                            var query=input.AsEnumerable().Where(Keep).Select(Project).Distinct().Skip((nuint)header[1]).Take((nuint)header[2]);
                            if(attempts!=before)return 4;
                            Vector<int> first=query.ToVector();Vector<int> second=query.ToVector();
                            if(first.Count!=expectedCount||second.Count!=expectedCount||query.Count()!=expectedCount)return 5;
                            for(nuint i=0;i<expectedCount;i++)if(first[i]!=expected[i]||second[i]!=expected[i])return 6;
                            if(expectedCount!=0){first[0]=999;if(second[0]!=expected[0])return 7;}
                        }
                        for(nuint i=0;i<count;i++)if(values[i]!=original[i])return 8;
                        if(allocations!=releases)return 9;
                    }
                    return fclose(stream);
                }
                """.Replace("PATH",path.Replace("\\","/")),optimization);
        }
        finally{File.Delete(path);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CurrentRemainsUncachedAndEndStopsFurtherSourceReads(int optimization)
        =>Run(Allocator+"""
            int calls=0;int Project(int value){calls++;return value*2;}
            int main(){
                int[3] data=[1,1,2];Span<int> input=new(data,3);
                {
                    var query=input.AsEnumerable().Select(Project).Distinct();var iterator=query.GetEnumerator();
                    if(calls!=0||attempts!=0)return 1;
                    try{iterator.Current;return 2;}catch(Exception* error){}
                    if(calls!=0)return 3;
                    if(!iterator.MoveNext()||calls!=1)return 4;
                    if(iterator.Current!=2||iterator.Current!=2||calls!=3)return 5;
                    if(!iterator.MoveNext()||calls!=5||iterator.Current!=4||calls!=6)return 6;
                    if(iterator.MoveNext()||iterator.MoveNext()||calls!=6)return 7;
                    try{iterator.Current;return 8;}catch(Exception* error){}
                    if(calls!=6)return 9;
                }
                if(allocations!=releases)return 10;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningKeysUseEqualityAndRetainFirstPayloadWhileReturnedValuesStayIndependent(int optimization)
        =>Run(Allocator+OwningKeys+"""
            int main(){
                {
                    Vector<Key> values=new();values.Add(new Key(0,s"first"));values.Add(new Key(1,s"second"));
                    values.Add(new Key(0,s"duplicate"));values.Add(new Key(2,s"third"));
                    int balance=allocations-releases;
                    var query=values.AsEnumerable().Distinct();
                    {
                        var iterator=query.GetEnumerator();
                        if(!iterator.MoveNext())return 1;
                        Key first=iterator.Current;Key again=iterator.Current;
                        if(first.Id!=0||again.Id!=0||first.LabelLength!=5||again.LabelLength!=5)return 2;
                        if(first.LabelData==again.LabelData||first.LabelData==values[0].LabelData||again.LabelData==values[0].LabelData)return 3;
                        first.ChangeFirst('X');if(again.LabelData[0]!='f'||values[0].LabelData[0]!='f')return 4;
                    }
                    if(live!=4||allocations-releases!=balance)return 5;
                    {
                        Vector<Key> result=query.ToVector();
                        if(result.Count!=3||result[0].Id!=0||result[1].Id!=1||result[2].Id!=2||live!=7)return 6;
                        if(result[0].LabelLength!=5||result[1].LabelLength!=6||result[2].LabelLength!=5)return 7;
                        if(result[0].LabelData==values[0].LabelData)return 8;
                        result[0].ChangeFirst('Z');if(values[0].LabelData[0]!='f')return 9;
                    }
                    if(live!=4||allocations-releases!=balance||values.Count!=4||values[2].LabelLength!=9)return 10;
                }
                if(live!=0||allocations!=releases)return 11;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopyHashEqualityIteratorAndEveryAllocationFailureReleaseAllDistinctOwners(int optimization)
        =>Run(Allocator+OwningKeys+"""
            int main(){
                {
                    Vector<Key> values=new();values.Add(new Key(0,s"zero"));values.Add(new Key(1,s"one"));
                    values.Add(new Key(0,s"zero"));values.Add(new Key(2,s"two"));
                    var query=new Enumerable<Key,Source,Source::Iterator>(new Source(values.AsSpan())).Select(Project).Distinct();
                    int balance=allocations-releases;
                    for(int failure=1;failure<=7;failure++){
                        copies=0;hashes=0;comparisons=0;projections=0;mode=failure;bool caught=false;
                        try{query.ToVector();}catch(Exception* error){if(error.GetCode()!=70+failure)return 1;caught=true;}
                        mode=0;
                        if(!caught||live!=4||active!=0||created!=destroyed||allocations-releases!=balance)return 2;
                        if(values.Count!=4||values[0].Id!=0||values[1].Id!=1||values[2].Id!=0||values[3].Id!=2)return 3;
                    }
                    int allocationPoints=0;
                    {attempts=0;Vector<Key> result=query.ToVector();allocationPoints=attempts;if(result.Count!=3)return 4;}
                    if(allocationPoints==0||live!=4||active!=0||allocations-releases!=balance)return 5;
                    for(int failure=1;failure<=allocationPoints;failure++){
                        attempts=0;failAt=failure;bool caught=false;
                        try{query.ToVector();}catch(BadAllocException* error){caught=true;}
                        failAt=0;
                        if(!caught||live!=4||active!=0||created!=destroyed||allocations-releases!=balance)return 6;
                    }
                }
                if(live!=0||active!=0||created!=destroyed||allocations!=releases)return 7;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedFileLinesKeepDistinctSnapshotsWhileReusableCurrentIsReplaced(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-distinct-lines-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string longLine=new string('x',4097)+"\0tail";
            string[] lines=["alpha","","A\0B",longLine,"alpha","","tail",longLine,"A\0B","last\r"];
            string input=Path.Combine(directory,"input.txt"),output=Path.Combine(directory,"output.txt");
            string document=string.Concat(lines.Select((line,index)=>line+(index==lines.Length-1?"":index%2==0?"\n":"\r\n")));
            File.WriteAllBytes(input,Encoding.UTF8.GetBytes(document));
            Run(Allocator+"""
                int main(){
                    {
                        File input=File::OpenRead("INPUT");File output=File::OpenWrite("OUTPUT");
                        File* source=&input;var query=source.Lines().AsEnumerable().Distinct();
                        foreach(String line in query){output.Write(line.Data,line.Length);output.Write("\n",1);}
                        if(!input.IsOpen||query.Count()!=0)return 1;
                        input.Close();output.Close();
                    }
                    if(allocations!=releases)return 2;return 0;
                }
                """.Replace("INPUT",input.Replace("\\","/")).Replace("OUTPUT",output.Replace("\\","/")),optimization);
            byte[] expected=Encoding.UTF8.GetBytes(string.Concat(lines.Distinct().Select(line=>line+"\n")));
            Assert.Equal(expected,File.ReadAllBytes(output));
        }
        finally{Directory.Delete(directory,true);}
    }
}

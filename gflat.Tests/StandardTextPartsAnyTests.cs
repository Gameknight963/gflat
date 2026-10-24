using gflat.CompileExceptions;
using System.Text;

namespace gflat.Tests;

public sealed class StandardTextPartsAnyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedByteSetsMatchLatin1SplitIncludingEmptyAndEveryByte(int optimization)
    {
        var cases = new List<(byte[] Text, byte[] Separators)>();
        foreach (string text in new[] { "", ",", ",,,", "plain", ",a;;b,", "a,b;c", "\0x\0" })
        foreach (string separators in new[] { "", ",;", ",,;", "\0" })
            cases.Add((Encoding.Latin1.GetBytes(text), Encoding.Latin1.GetBytes(separators)));
        for (int value = 0; value < 256; value++)
            cases.Add((new byte[] { (byte)'a', (byte)value, (byte)'z' }, new[] { (byte)value }));
        byte[] all = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        cases.Add((all, all));
        var random = new Random(71183);
        for (int row = 0; row < 48; row++)
        {
            byte[] text = new byte[random.Next(257)], separators = new byte[random.Next(257)];
            random.NextBytes(text); random.NextBytes(separators); cases.Add((text, separators));
        }
        using var manifest = new MemoryStream();
        using (var writer = new BinaryWriter(manifest, Encoding.UTF8, leaveOpen: true))
        foreach (var row in cases)
        foreach (bool removeEmpty in new[] { false, true })
        {
            string text = Encoding.Latin1.GetString(row.Text);
            string[] parts = row.Separators.Length == 0
                ? (removeEmpty && text.Length == 0 ? Array.Empty<string>() : new[] { text })
                : text.Split(Encoding.Latin1.GetString(row.Separators).ToCharArray(),
                    removeEmpty ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None);
            writer.Write((ushort)row.Text.Length); writer.Write((ushort)row.Separators.Length);
            writer.Write((byte)(removeEmpty ? 1 : 0)); writer.Write((ushort)parts.Length);
            writer.Write(row.Text); writer.Write(row.Separators);
            foreach (string part in parts)
            { writer.Write((ushort)part.Length); writer.Write(Encoding.Latin1.GetBytes(part)); }
        }
        string hex = Convert.ToHexString(manifest.ToArray());
        Run($$"""
            using std;
            int attempts=0;
            namespace Memory {
                public replace void*? Allocate(nuint count){attempts++;return null;}
                public replace void Free(void* value)nothrow{}
            }
            int Digit(char value){if(value<='9')return value-'0';return value-'A'+10;}
            uint ReadByte(readonly(char)* data,ref nuint at){uint value=(uint)(Digit(data[at])*16+Digit(data[at+1]));at+=2;return value;}
            nuint ReadSize(readonly(char)* data,ref nuint at){uint low=ReadByte(data,ref at);return (nuint)(low+256u*ReadByte(data,ref at));}
            int main(){
                readonly(char)* data="{{hex}}";nuint at=0;
                char[256] text;char[256] separators;
                for(int row=0;row<{{cases.Count * 2}};row++){
                    nuint count=ReadSize(data,ref at);nuint separatorCount=ReadSize(data,ref at);
                    bool removeEmpty=ReadByte(data,ref at)!=0u;nuint expectedParts=ReadSize(data,ref at);
                    for(nuint i=0;i<count;i++)text[i]=(char)ReadByte(data,ref at);
                    for(nuint i=0;i<separatorCount;i++)separators[i]=(char)ReadByte(data,ref at);
                    TextPartsAny parts=new(new Span<readonly(char)>(text,count),new Span<readonly(char)>(separators,separatorCount),removeEmpty);
                    TextPartsAny::Enumerator iterator=parts.GetEnumerator();
                    for(nuint part=0;part<expectedParts;part++){
                        nuint expectedLength=ReadSize(data,ref at);
                        if(!iterator.MoveNext())return 1;
                        Span<readonly(char)> value=iterator.Current;Span<readonly(char)> repeated=iterator.Current;
                        if(value.Length!=expectedLength||repeated.Length!=expectedLength)return 2;
                        for(nuint i=0;i<expectedLength;i++){
                            char expected=(char)ReadByte(data,ref at);
                            if(value[i]!=expected||repeated[i]!=expected)return 3;
                        }
                    }
                    if(iterator.MoveNext()||iterator.MoveNext())return 4;
                }
                if(at!={{hex.Length}}||attempts!=0)return 5;return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SnapshotsIndependentCursorsBorrowSourceAndComposeWithoutAllocation(int optimization)
    {
        Run("""
            using std;
            extern void*? malloc(nuint count);extern void free(void* value);
            int attempts=0;bool deny=false;
            namespace Memory {
                public replace void*? Allocate(nuint count){attempts++;if(deny)return null;return malloc(count);}
                public replace void Free(void* value)nothrow{free(value);}
            }
            TextPartsAny Snapshot(Span<readonly(char)> source){
                String separators=s",;";
                return new TextPartsAny(source,new Span<readonly(char)>(separators.Data,separators.Length));
            }
            bool NonEmpty(Span<readonly(char)> part)=>part.Length!=0;
            int main(){
                char[5] data=['a',',','b',';','c'];Span<char> mutable=new(data,5);
                TextPartsAny saved=Snapshot(mutable.AsImmutable());
                char[2] separators=[',',';'];Span<readonly(char)> delimiterView=new(separators,2);
                TextPartsAny parts=mutable.SplitAny(delimiterView);
                separators[0]='x';separators[1]='y';
                String owner=s"a,b;c";
                int before=attempts;deny=true;
                immutable(TextPartsAny)* deep=&parts;readonly(TextPartsAny)* shallow=&parts;
                TextPartsAny::Enumerator first=deep.GetEnumerator();TextPartsAny::Enumerator second=shallow.GetEnumerator();
                if(!first.MoveNext()||!second.MoveNext())return 1;
                immutable(TextPartsAny::Enumerator)* observer=&first;
                Span<readonly(char)> borrowed=observer.Current;data[0]='z';
                if(borrowed[0]!='z'||second.Current[0]!='z')return 2;
                data[3]='x';
                if(!first.MoveNext()||first.Current.Length!=3||first.Current[0]!='b'||first.Current[1]!='x'||second.Current[0]!='z')return 3;
                if(saved.AsEnumerable().Count(NonEmpty)!=2||deep.AsEnumerable().Where(NonEmpty).Count()!=2)return 4;
                if(owner.SplitAny(",;").AsEnumerable().Count()!=3)return 5;
                if(mutable.AsImmutable().SplitAny(",;").AsEnumerable().Count()!=2)return 6;
                Span<readonly(char)> empty=new();
                TextPartsAny nothing=new(empty,empty);TextPartsAny noParts=new(empty,empty,true);
                if(nothing.AsEnumerable().Count()!=1||noParts.AsEnumerable().Any())return 7;
                TextPartsAny noDelimiters=mutable.SplitAny(empty);
                if(noDelimiters.AsEnumerable().Count()!=1)return 8;
                char[4] cSeparators=[',','\0',';','\0'];
                TextPartsAny cStringSet=new(new Span<readonly(char)>("a;b,c",5),cSeparators);
                if(cStringSet.AsEnumerable().Count()!=2)return 9;
                if(attempts!=before)return 10;deny=false;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CurrentStateAndReadonlyPartsFollowTextPartsContract(int optimization)
    {
        Run("""
            using std;
            int main(){
                TextPartsAny parts=new(new Span<readonly(char)>("a",1),",");
                TextPartsAny::Enumerator iterator=parts.GetEnumerator();bool caught=false;
                try{Span<readonly(char)> invalid=iterator.Current;}
                catch(Exception* error){caught=CString::Equals(error.GetMessage(),"Enumerator has no current element");}
                if(!caught||!iterator.MoveNext()||iterator.Current[0]!='a'||iterator.MoveNext()||iterator.MoveNext())return 1;
                caught=false;
                try{Span<readonly(char)> invalid=iterator.Current;}
                catch(Exception* error){caught=CString::Equals(error.GetMessage(),"Enumerator has no current element");}
                if(!caught)return 2;return 0;
            }
            """, optimization);
        const string prefix="""
            using std;
            void Inspect(immutable(TextPartsAny::Enumerator)* value){Span<readonly(char)> part=value.Current;
            """;
        Compiler.Check(Sources(prefix+"char byteValue=part[0];}"));
        Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(prefix+"part[0]='x';}")));
    }

    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("text-parts-any.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"TextPartsAny exit {result.ExitCode}: {result.StandardError}");
    }
}

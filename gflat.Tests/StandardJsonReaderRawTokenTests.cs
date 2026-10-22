using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class StandardJsonReaderRawTokenTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RawLexemesMatchUtf8JsonReaderWithoutDecodingOrAllocating(int optimization)
    {
        byte[] document=Encoding.UTF8.GetBytes(" { \"na\\u006de\" : [\"€\\u0000\\n\", -0.10E+002, true, false, null, {}, []] } \n");
        var oracle=new Utf8JsonReader(document);
        var rows=new List<string>();
        while(oracle.Read()) {
            long size=oracle.TokenType switch {
                JsonTokenType.PropertyName or JsonTokenType.String => oracle.ValueSpan.Length+2,
                JsonTokenType.Number=>oracle.ValueSpan.Length,
                JsonTokenType.True or JsonTokenType.Null=>4,
                JsonTokenType.False=>5,
                _=>1
            };
            rows.Add($"(nuint){oracle.TokenStartIndex},(nuint){size}");
        }
        Run($$"""
            using std;
            extern void*? malloc(nuint size);extern void free(void* storage);
            int allocations=0;
            namespace Memory {
                public replace void*? Allocate(nuint size){allocations++;return malloc(size);}
                public replace void Free(void* storage){free(storage);}
            }
            int main(){
                char[{{document.Length}}] input=[{{string.Join(",",document.Select(b=>$"(char){b}"))}}];
                nuint[{{rows.Count*2}}] rows=[{{string.Join(",",rows)}}];
                JsonReader reader=new(input,{{document.Length}});
                if(!reader.Read())return 1;
                reader.Reset();int before=allocations;nuint row=0;
                while(reader.Read()){
                    Span<readonly(char)> raw=reader.RawToken;
                    readonly(JsonReader)* shallow=&reader;immutable(JsonReader)* deep=&reader;
                    Span<readonly(char)> shallowRaw=shallow.RawToken;
                    Span<readonly(char)> deepRaw=deep.RawToken;
                    if(raw.Length!=rows[row+1]||raw.Length!=reader.TokenLength)return 2;
                    if(raw.Data!=&input[0]+rows[row]||shallowRaw.Data!=raw.Data||deepRaw.Data!=raw.Data)return 3;
                    for(nuint i=0;i<raw.Length;i++)if(raw[i]!=input[rows[row]+i])return 4;
                    row+=2;
                }
                if(row!={{rows.Count*2}}||allocations!=before)return 5;
                Span<readonly(char)> empty=reader.RawToken;
                if(empty.Length!=0||empty.Data!=null)return 6;
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SavedViewsBorrowInputAcrossAdvanceResetMoveAndReaderDestruction(int optimization)
        => Run("""
            using std;
            Span<readonly(char)> Save(readonly(char)* input,nuint count){
                JsonReader reader=new(input,count);
                reader.Read();reader.Read();
                Span<readonly(char)> saved=reader.RawToken;
                if(saved.Length!=4)throw new Exception("length");
                reader.Read();reader.Reset();
                JsonReader transferred=move reader;
                transferred.Read();transferred.Read();
                transferred.Reset();
                return saved;
            }
            int main(){
                String input=s"[true,false]";
                Span<readonly(char)> saved=Save(input.Data!,input.Length);
                if(saved.Data!=input.Data!+1||saved.Length!=4)return 1;
                readonly(char)* expected="true";
                for(nuint i=0;i<4;i++)if(saved[i]!=expected[i])return 2;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MissingAndFailedTokensReturnCanonicalEmptyWithoutInputAccess(int optimization)
        => Run("""
            using std;
            bool Empty(ref immutable(JsonReader) reader){Span<readonly(char)> raw=reader.RawToken;return raw.Length==0&&raw.Data==null;}
            int main(){
                JsonReader unread=new((readonly(char)*)1,100);
                if(!Empty(ref unread))return 1;
                unread.Reset();if(!Empty(ref unread))return 2;
                JsonReader reader=new("[true,?]",8);
                if(!Empty(ref reader))return 3;
                reader.Read();reader.Read();
                Span<readonly(char)> retained=reader.RawToken;
                try{reader.Read();return 4;}catch(ref JsonParseException error){}
                if(!Empty(ref reader)||retained.Length!=4||retained[0]!='t')return 5;
                try{reader.Read();return 6;}catch(ref JsonParseException error){}
                if(!Empty(ref reader))return 7;
                reader.Reset();if(!Empty(ref reader))return 8;
                JsonReader empty=new((readonly(char)*)1,0);
                try{empty.Read();return 9;}catch(ref JsonParseException error){}
                if(!Empty(ref empty))return 10;
                return 0;
            }
            """,optimization);

    [Fact]
    public void RawTokenElementsCannotBeModified()
    {
        var error=Assert.Throws<gflat.CompileExceptions.TypeCheckException>(()=>Compiler.Emit(Sources("""
            using std;
            int main(){JsonReader reader=new("true",4);reader.Read();Span<readonly(char)> raw=reader.RawToken;raw[0]='X';return 0;}
            """)));
        Assert.Contains("readonly",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",code));
    private static void Run(string code,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(code),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}

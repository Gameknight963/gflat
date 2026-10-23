using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class StandardJsonReaderSkipTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SkipAndFollowingReadMatchDotNetAtEveryToken(int optimization)
    {
        string[] documents = ["{\"unknown\":[1,{\"x\":[true,null]}],\"known\":42}",
            "[{},[],\"text\",false,null,-1.5e2]", "{\"a\\u0062\":\"x\\ny\",\"empty\":{}}", "42", "true", " \"abc\" \n"];
        var code = new StringBuilder("""
            using std;
            bool Matches(ref JsonReader reader,int* row) {
                return (int)reader.Kind==row[0] && reader.TokenOffset==(nuint)row[1]
                    && reader.TokenLength==(nuint)row[2] && reader.Depth==(nuint)row[3];
            }
            int Check(readonly(char)* data,nuint length,int* rows,int count) {
                JsonReader reader=new(data,length);
                for(int n=0;n<count;n++) {
                    reader.Reset();int* row=rows+n*10;
                    for(int i=0;i<row[0];i++)if(!reader.Read())return 1;
                    reader.Skip();if(!Matches(ref reader,row+1))return 2;
                    reader.Skip();if(!Matches(ref reader,row+1))return 3;
                    bool next=reader.Read();if(next!=(row[5]!=0))return 4;
                    if(next && !Matches(ref reader,row+6))return 5;
                    while(reader.Read()){}
                    reader.Skip();if(reader.Kind!=JsonTokenKind::None||reader.TokenLength!=0||reader.Depth!=0)return 6;
                    if(reader.Read())return 7;
                }
                return 0;
            }
            int main(){
            """);
        foreach (string document in documents)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(document);
            var tokens = new Utf8JsonReader(bytes);
            int count = 0;
            while (tokens.Read()) count++;
            var rows = new List<int>();
            for (int readCount = 0; readCount <= count; readCount++)
            {
                var reader = new Utf8JsonReader(bytes);
                for (int i = 0; i < readCount; i++) Assert.True(reader.Read());
                reader.Skip();
                rows.Add(readCount);
                rows.AddRange(Snapshot(ref reader));
                bool next = reader.Read();
                rows.Add(next ? 1 : 0);
                rows.AddRange(next ? Snapshot(ref reader) : [0, 0, 0, 0]);
            }
            code.AppendLine($"{{char[{bytes.Length}] data;int[{rows.Count}] rows;");
            for (int i = 0; i < bytes.Length; i++) code.AppendLine($"data[{i}]=(char){bytes[i]};");
            for (int i = 0; i < rows.Count; i++) code.AppendLine($"rows[{i}]={rows[i]};");
            code.AppendLine($"int status=Check(data,{bytes.Length},rows,{count + 1});if(status!=0)return status;}}");
        }
        code.AppendLine("return 0;}");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidSkippedSubtreesPreserveReadErrorsAndLatchUntilReset(int optimization)
    {
        string[] invalid = ["{\"x\":}", "{\"x\" 1}", "{\"x\":[1,]}", "[{\"x\":}]", "[1,[2]", "[\"\\uD800\"]", "[1e+]", "[true,falseX]"];
        var code = new StringBuilder("using std;int main(){\n");
        foreach (string document in invalid)
        {
            code.AppendLine($$"""
                {readonly(char)* text={{Literal(document)}};
                 JsonReader baseline=new(text,{{document.Length}});nuint offset=0;
                 try{while(baseline.Read()){}return 1;}catch(ref JsonParseException error){offset=error.Offset;}
                 JsonReader reader=new(text,{{document.Length}});if(!reader.Read())return 2;
                 try{reader.Skip();return 3;}catch(ref JsonParseException error){if(error.Offset!=offset)return 4;}
                 if(reader.Kind!=JsonTokenKind::None||reader.TokenLength!=0)return 5;
                 try{reader.Skip();return 6;}catch(ref JsonParseException error){}
                 try{reader.Read();return 7;}catch(ref JsonParseException error){}
                 reader.Reset();if(!reader.Read())return 8;
                 try{reader.Skip();return 9;}catch(ref JsonParseException error){if(error.Offset!=offset)return 10;} }
                """);
        }
        code.AppendLine("return 0;}");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SkipDoesNotConsumeTrailingDataAndNoOpDoesNotReadInitialInput(int optimization)
        => Run("""
            using std;
            int main(){
                JsonReader initial=new("invalid",7);initial.Skip();
                if(initial.Kind!=JsonTokenKind::None||initial.TokenOffset!=0)return 1;
                try{initial.Read();return 2;}catch(ref JsonParseException error){}
                try{initial.Skip();return 3;}catch(ref JsonParseException error){}
                JsonReader container=new("{}[]",4);container.Read();container.Skip();
                if(container.Kind!=JsonTokenKind::EndObject||container.TokenOffset!=1)return 4;
                try{container.Read();return 5;}catch(ref JsonParseException error){}
                JsonReader scalar=new("true false",10);scalar.Read();scalar.Skip();
                if(scalar.Kind!=JsonTokenKind::True||scalar.TokenOffset!=0)return 6;
                try{scalar.Read();return 7;}catch(ref JsonParseException error){}
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StackFailureRequiresResetAndDeepSkipReusesStorage(int optimization)
        => Run("""
            using std;
            extern void*? malloc(nuint size);extern void free(void* storage);
            bool deny=false;int allocations=0;int releases=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){if(deny)return null;allocations++;return malloc(size);}
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            int main(){
                {JsonReader scalar=new("42",2);deny=true;scalar.Skip();scalar.Read();scalar.Skip();
                 if(scalar.Read())return 1;scalar.Skip();deny=false;if(allocations!=0)return 2;}
                {JsonReader reader=new("[[[[[]]]]]",10);
                 for(int i=0;i<4;i++)if(!reader.Read())return 3;
                 deny=true;try{reader.Skip();return 4;}catch(ref BadAllocException error){}deny=false;
                 try{reader.Skip();return 5;}catch(ref JsonParseException error){}
                 try{reader.Read();return 6;}catch(ref JsonParseException error){}
                 reader.Reset();reader.Read();reader.Skip();
                 if(reader.Kind!=JsonTokenKind::EndArray||reader.Depth!=0||reader.Read())return 7;}
                {char[2049] text;
                 for(nuint i=0;i<1024;i++){text[i]='[';text[2048-i]=']';}text[1024]='0';
                 JsonReader reader=new(text,2049);reader.Read();reader.Skip();
                 if(reader.Kind!=JsonTokenKind::EndArray||reader.Depth!=0||reader.TokenOffset!=2048)return 8;
                 if(reader.Read())return 9;
                 int before=allocations;reader.Reset();deny=true;reader.Read();reader.Skip();deny=false;
                 if(allocations!=before||reader.Read())return 10;}
                if(allocations!=releases)return 11;return 0;
            }
            """, optimization);

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static int[] Snapshot(ref Utf8JsonReader reader)
    {
        int kind = reader.TokenType switch
        {
            JsonTokenType.None => 0, JsonTokenType.StartObject => 1, JsonTokenType.EndObject => 2,
            JsonTokenType.StartArray => 3, JsonTokenType.EndArray => 4, JsonTokenType.PropertyName => 5,
            JsonTokenType.String => 6, JsonTokenType.Number => 7, JsonTokenType.True => 8,
            JsonTokenType.False => 9, JsonTokenType.Null => 10, _ => throw new InvalidOperationException()
        };
        int length = reader.TokenType switch
        {
            JsonTokenType.None => 0,
            JsonTokenType.PropertyName or JsonTokenType.String => reader.ValueSpan.Length + 2,
            JsonTokenType.Number => reader.ValueSpan.Length,
            JsonTokenType.True or JsonTokenType.Null => 4, JsonTokenType.False => 5, _ => 1
        };
        return [kind, (int)reader.TokenStartIndex, length, reader.CurrentDepth];
    }

    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("main.gf", code));
        Assert.Equal(0, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }
}

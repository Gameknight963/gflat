using System.Text;

namespace gflat.Tests;

public sealed class StandardJsonReaderTests
{
    [Fact]
    public void OwningReaderCannotBeImplicitlyCopied()
    {
        var error=Assert.Throws<gflat.CompileExceptions.TypeCheckException>(()=>Compiler.Emit(Sources("""
            using Standard; using Standard::Json;int main(){JsonReader first=new("[]",2);JsonReader second=first;return 0;}
            """)));
        Assert.Contains("copy",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidDocumentsLatchUntilResetAndReportBoundedOffsets(int optimization)
    {
        byte[][] invalid = [[], " "u8.ToArray(), "["u8.ToArray(), "{"u8.ToArray(), "[1,]"u8.ToArray(),
            "{\"x\":}"u8.ToArray(), "{\"x\" 1}"u8.ToArray(), "[1 2]"u8.ToArray(), "true false"u8.ToArray(),
            "01"u8.ToArray(), "-"u8.ToArray(), "1."u8.ToArray(), "1e+"u8.ToArray(), "NaN"u8.ToArray(),
            "/*x*/0"u8.ToArray(), "\"\\uD800\""u8.ToArray(), "\"\\uDC00\""u8.ToArray(),
            [34, 0, 34], [34, 0xff, 34], [0xef, 0xbb, 0xbf, 48], "{\"x\":1,}"u8.ToArray()];
        var code = new StringBuilder("using Standard; using Standard::Json;int main(){char[128] bytes;\n");
        foreach (byte[] bytes in invalid)
        {
            for (int i=0;i<bytes.Length;i++) code.AppendLine($"bytes[{i}]=(char){bytes[i]};");
            code.AppendLine($$"""
                { JsonReader reader=new(bytes,{{bytes.Length}});int caught=0;
                  try {while(reader.Read()){}return 1;}
                  catch(ref JsonParseException error){if(error.Offset>{{bytes.Length}})return 2;caught++;}
                  try {reader.Read();return 3;}catch(ref JsonParseException error){caught++;}
                  reader.Reset();
                  try {while(reader.Read()){}return 4;}catch(ref JsonParseException error){caught++;}
                  if(caught!=3)return 5; }
                """);
        }
        code.AppendLine("return 0;}");
        Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StringsDecodeUsingExistingHelpersAndMovedReaderRetainsIndependentTokenOffsets(int optimization)
        => Run("""
            using Standard; using Standard::Json;
            int main(){
                readonly(char)* input="{\"name\":\"a\\u0000\\uD83D\\uDE00\",\"name\":false}";
                JsonReader original=new(input,43);
                if(!original.Read()||original.Kind!=JsonTokenKind::StartObject)return 1;
                JsonReader reader=move original;
                if(!reader.Read()||reader.Kind!=JsonTokenKind::PropertyName)return 2;
                if(!reader.Read()||reader.Kind!=JsonTokenKind::String||reader.Depth!=1)return 3;
                char[6] decoded; nuint written=0;
                if(!JsonString::TryUnquote(input+reader.TokenOffset,reader.TokenLength,decoded,6,ref written)||written!=6)return 4;
                if(decoded[0]!='a'||decoded[1]!='\0'||(byte)decoded[2]!=(byte)240||(byte)decoded[5]!=(byte)128)return 5;
                if(!reader.Read()||reader.Kind!=JsonTokenKind::PropertyName)return 6;
                if(!reader.Read()||reader.Kind!=JsonTokenKind::False)return 7;
                if(!reader.Read()||reader.Kind!=JsonTokenKind::EndObject||reader.Depth!=0)return 8;
                if(reader.Read()||reader.Read()||reader.Kind!=JsonTokenKind::None)return 9;
                reader.Reset();int tokens=0;while(reader.Read())tokens++;if(tokens!=6)return 10;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StackGrowthFailureRequiresResetAndAllOwnedStorageIsReleased(int optimization)
        => Run("""
            using Standard; using Standard::Json;
            extern void*? malloc(nuint size);extern void free(void* storage);
            bool deny=false;int allocations=0;int releases=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){if(deny)return null;allocations++;return malloc(size);}
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            int main(){
                {
                    JsonReader reader=new("[[[[[]]]]]",10);
                    for(int i=0;i<4;i++)if(!reader.Read())return 1;
                    deny=true;
                    try{reader.Read();return 2;}catch(ref BadAllocException error){}
                    deny=false;
                    try{reader.Read();return 3;}catch(ref JsonParseException error){}
                    reader.Reset();int tokens=0;while(reader.Read())tokens++;if(tokens!=10)return 4;
                    int before=allocations;reader.Reset();while(reader.Read()){}
                    if(allocations!=before)return 5;
                }
                if(allocations!=releases)return 6;
                return 0;
            }
            """,optimization);

    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",code));
    private static void Run(string code,int optimization)
        => Assert.Equal(0,CompilerTestHelper.Run(Sources(code),optimization).ExitCode);
}

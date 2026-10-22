using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class StandardJsonValueTextEqualsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StandaloneEqualityMatchesDecodedUtf8WithNoAllocationOrMutation(int optimization)
    {
        string[] quoted = ["\"\"", "\"name\"", "\"na\\u006de\"", "\"NAME\"", "\"a\\u0000b\"",
            "\"\\b\\f\\n\\r\\t\\\"\\\\\\/\"", "\"€😀\"", "\"\\u20AC\\uD83D\\uDE00\"", "\"é\"", "\"é\"",
            JsonSerializer.Serialize(new string('a',1024)+"😀")];
        string[] text = ["", "name", "nam", "names", "NAME", "a\0b", "a", "\b\f\n\r\t\"\\/", "€😀", "é", "é",new string('a',1024)+"😀",new string('a',1024)+"😁"];
        var bytes = new List<byte>();
        string Add(byte[] value){int offset=bytes.Count;bytes.AddRange(value);return $"(nuint){offset},(nuint){value.Length}";}
        string[] quotedRows=quoted.Select(q=>Add(Encoding.UTF8.GetBytes(q))).ToArray();
        string[] textRows=text.Select(t=>Add(Encoding.UTF8.GetBytes(t))).ToArray();
        var matches=quoted.SelectMany(q=>text.Select(t=>JsonSerializer.Deserialize<string>(q)==t?"true":"false"));
        string code=$$"""
            int main() nothrow {
                char[{{bytes.Count}}] input=[{{string.Join(",",bytes.Select(b=>$"(char){b}"))}}];
                nuint[{{quoted.Length*2}}] quoted=[{{string.Join(",",quotedRows)}}];
                nuint[{{text.Length*2}}] text=[{{string.Join(",",textRows)}}];
                bool[{{quoted.Length*text.Length}}] expected=[{{string.Join(",",matches)}}];
                uint before=0u;for(nuint i=0;i<{{bytes.Count}};i++)before=before*31u+(uint)(byte)input[i];
                for(nuint q=0;q<{{quoted.Length}};q++)for(nuint t=0;t<{{text.Length}};t++){
                    bool equal=std::JsonString::ValueTextEquals(&input[0]+quoted[q*2],quoted[q*2+1],&input[0]+text[t*2],text[t*2+1]);
                    if(equal!=expected[q*{{text.Length}}+t])return 1;
                }
                uint after=0u;for(nuint i=0;i<{{bytes.Count}};i++)after=after*31u+(uint)(byte)input[i];
                if(after!=before)return 2;
                char[6] overlap=['"','n','a','m','e','"'];
                if(!std::JsonString::ValueTextEquals(overlap,6,&overlap[1],4))return 3;
                if(!std::JsonString::ValueTextEquals("\"\"",2,(readonly(char)*)1,0))return 4;
                if(std::JsonString::ValueTextEquals((readonly(char)*)1,0,(readonly(char)*)1,100))return 5;
                if(std::JsonString::ValueTextEquals((readonly(char)*)1,1,(readonly(char)*)1,100))return 6;
                return 0;
            }
            """;
        SourceFile[] sources=[new("JsonString.gf",Read("JsonString.gf")),new("Utf8.gf",Read("Utf8.gf")),new("main.gf",code)];
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
        foreach(string forbidden in new[]{"__gflat_eh","@malloc","@free","gflat$Memory$","llvm.memcpy"})Assert.DoesNotContain(forbidden,ir);
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidLiteralAndExpectedUtf8NeverMatch(int optimization)
    {
        byte[][] bad=[[],[34],[34,34,32],[32,34,34],"\"\\q\""u8.ToArray(),"\"\\uD800\""u8.ToArray(),"\"\\uDC00\""u8.ToArray(),
            "\"\\uD800\\u0041\""u8.ToArray(),[34,10,34],[34,0xff,34],[34,0xc0,0x80,34],[34,0xf0,0x9f,34]];
        var code=new StringBuilder("int main() nothrow {char[32] input;\n");
        foreach(var value in bad){for(int i=0;i<value.Length;i++)code.AppendLine($"input[{i}]=(char){value[i]};");code.AppendLine($"if(std::JsonString::ValueTextEquals(input,{value.Length},\"A\",1))return 1;");}
        code.AppendLine("""
            char[2] invalid=[(char)0xc0,(char)0x80];
            if(std::JsonString::ValueTextEquals("\"A\"",3,invalid,2))return 2;
            char[2] invalidSuffix=['A',(char)0xff];
            if(std::JsonString::ValueTextEquals("\"A\"",3,invalidSuffix,2))return 3;
            if(std::JsonString::ValueTextEquals("\"\"",2,invalid,2))return 4;
            return 0;}
            """);
        Assert.Equal(0,CompilerTestHelper.Run([new("JsonString.gf",Read("JsonString.gf")),new("Utf8.gf",Read("Utf8.gf")),new("main.gf",code.ToString())],optimization).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReaderMatchesEscapedPropertiesAndValuesThroughProtectedViews(int optimization)
    {
        string code="""
            using std;
            extern void*? malloc(nuint size);extern void free(void* storage);
            int allocations=0;
            namespace Memory{public replace void*? Allocate(nuint size){allocations++;return malloc(size);}public replace void Free(void* storage){free(storage);}}
            int main(){
                JsonReader unread=new((readonly(char)*)1,100);
                if(unread.ValueTextEquals((readonly(char)*)1,100))return 1;
                readonly(char)* input="{\"na\\u006de\":\"a\\u0000b\",\"x\":42}";
                nuint length=0;while(input[length]!='\0')length++;
                JsonReader reader=new(input,length);
                if(!reader.Read()||reader.ValueTextEquals((readonly(char)*)1,100))return 2;
                int before=allocations;
                reader.Read();
                readonly(JsonReader)* shallow=&reader;immutable(JsonReader)* deep=&reader;
                if(!reader.ValueTextEquals("name",4)||!shallow.ValueTextEquals("name",4)||!deep.ValueTextEquals("name",4))return 3;
                if(reader.ValueTextEquals("Name",4)||reader.ValueTextEquals("nam",3))return 4;
                reader.Read();if(!reader.ValueTextEquals("a\0b",3)||reader.ValueTextEquals("a",1))return 5;
                reader.Read();if(!reader.ValueTextEquals("x",1))return 6;
                reader.Read();if(reader.ValueTextEquals((readonly(char)*)1,100))return 7;
                while(reader.Read())if(reader.ValueTextEquals((readonly(char)*)1,100))return 8;
                if(reader.ValueTextEquals((readonly(char)*)1,100)||allocations!=before)return 9;
                reader.Reset();if(reader.ValueTextEquals((readonly(char)*)1,100))return 10;
                JsonReader failed=new("?",1);
                try{failed.Read();return 11;}catch(ref JsonParseException error){}
                if(failed.ValueTextEquals((readonly(char)*)1,100))return 12;
                return 0;
            }
            """;
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",code));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
    private static string Read(string name)=>File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name));
}

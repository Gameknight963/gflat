using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace gflat.Tests;

public sealed class StandardJsonWriterIndentationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndentedAndCompactAsciiLayoutMatchUtf8JsonWriter(int optimization)
    {
        string[] documents=["null","true","123.00e-99999","\"abc\"","{}","[]","[{},[],[1,2],{\"x\":false}]",
            "{\"a\":[],\"b\":{},\"c\":[1,[],{}, {\"x\":false}],\"a\":\"q\\\"\\\\\\n\\u0000\"}"];
        var code=new StringBuilder("using std;extern int printf(readonly(char)* text,...);extern int putchar(int ch);int main(){\n");
        var expected=new List<byte[]>();int id=0;
        foreach(string document in documents)foreach(bool indented in new[]{false,true}){
            using var parsed=JsonDocument.Parse(document);
            using var output=new MemoryStream();
            using(var writer=new Utf8JsonWriter(output,new JsonWriterOptions{Indented=indented,NewLine="\n",Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping}))parsed.RootElement.WriteTo(writer);
            expected.Add(output.ToArray());
            code.AppendLine($"{{String text=new();JsonWriter writer=new(&text,{(indented?"true":"false")});");
            Emit(code,parsed.RootElement,ref id);
            code.AppendLine("writer.Complete();for(nuint i=0;i<text.Length;i++)printf(\"%02X\",(uint)(byte)text.Data[i]);putchar(10);}");
        }
        code.AppendLine("return 0;}");
        var result=CompilerTestHelper.Run(Sources(code.ToString()),optimization);
        Assert.True(result.ExitCode==0,result.StandardError);
        string[] lines=result.StandardOutput.Replace("\r\n","\n").Split('\n',StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected.Count,lines.Length);
        for(int i=0;i<lines.Length;i++)Assert.Equal(expected[i],Convert.FromHexString(lines[i]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndentedUnicodePreservesDecodedNamesAndValues(int optimization)
    {
        var values=new Dictionary<string,string>{["\0\ud83d\ude00"]="\u20ac\0\n",["separators"]="\u2028\u2029\uffff\udbff\udfff",["quotes"]="\"\\\t\b\f\r"};
        using var original=JsonDocument.Parse(JsonSerializer.Serialize(values));
        var code=new StringBuilder("using std;extern int printf(readonly(char)* text,...);int main(){String text=new();JsonWriter writer=new(&text,true);\n");
        int id=0;Emit(code,original.RootElement,ref id);
        code.AppendLine("writer.Complete();for(nuint i=0;i<text.Length;i++)printf(\"%02X\",(uint)(byte)text.Data[i]);return 0;}");
        var result=CompilerTestHelper.Run(Sources(code.ToString()),optimization);Assert.Equal(0,result.ExitCode);
        byte[] bytes=Convert.FromHexString(result.StandardOutput);
        Assert.Contains("\n  ",Encoding.UTF8.GetString(bytes));
        using var parsed=JsonDocument.Parse(bytes);
        Assert.Equal(values.Keys,parsed.RootElement.EnumerateObject().Select(property=>property.Name));
        foreach(var value in values)Assert.Equal(value.Value,parsed.RootElement.GetProperty(value.Key).GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndentationAndMovedWriterAllocateOnlyTheOrdinaryNestingStack(int optimization)
        => Run("""
            using std;extern void*? malloc(nuint count);extern void free(void* pointer);int allocations=0;int releases=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){allocations++;return malloc(count);}
                public replace void Free(void* pointer) nothrow{releases++;free(pointer);}
            }
            struct Sink:ITextWriter{public nuint Bytes;public Sink(){Bytes=0;}public void Write(readonly(char)* data,nuint count){Bytes+=count;}}
            int main(){nuint compact=0;
                for(int mode=0;mode<2;mode++){
                    Sink sink=new();int before=allocations;
                    {JsonWriter writer=new(&sink,mode==1);writer.WriteStartArray();JsonWriter moved=move writer;
                     moved.WriteStartObject();moved.WritePropertyName("a");moved.WriteStartArray();moved.WriteNull();moved.WriteEndArray();
                     moved.WriteEndObject();moved.WriteEndArray();moved.Complete();if(allocations!=before+1)return 1;}
                    if(mode==0)compact=sink.Bytes;else if(sink.Bytes<=compact)return 2;
                }
                if(allocations!=2||releases!=2)return 3;return 0;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryOutputByteFailureIncludingIndentationPoisonsWriter(int optimization)
        => Run("""
            using std;
            struct Sink:ITextWriter{
                public int Budget;public int Calls;public nuint Bytes;
                public Sink(int budget){Budget=budget;Calls=0;Bytes=0;}
                public void Write(readonly(char)* input,nuint count){Calls++;for(nuint i=0;i<count;i++){
                    if(Budget==0)throw new Exception("output");if(Budget>0)Budget--;Bytes++;}}
            }
            void Document(JsonWriter* writer){
                writer.WriteStartObject();writer.WritePropertyName("a");writer.WriteStartArray();
                writer.WriteString("x");writer.WriteStartObject();writer.WriteEndObject();writer.WriteEndArray();
                writer.WritePropertyName("b");writer.WriteNull();writer.WriteEndObject();writer.Complete();
            }
            int main(){Sink complete=new(-1);JsonWriter baseline=new(&complete,true);Document(&baseline);
                for(int budget=0;budget<(int)complete.Bytes;budget++){
                    Sink sink=new(budget);JsonWriter writer=new(&sink,true);int caught=0;
                    try{Document(&writer);return 1;}catch(ref Exception error){caught++;}
                    int calls=sink.Calls;nuint bytes=sink.Bytes;sink.Budget=-1;
                    try{writer.WriteNull();}catch(ref JsonWriteException error){caught++;}
                    try{writer.WriteStartObject();}catch(ref JsonWriteException error){caught++;}
                    try{writer.Complete();}catch(ref JsonWriteException error){caught++;}
                    if(caught!=4||sink.Calls!=calls||sink.Bytes!=bytes)return 2;
                }
                return 0;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidationAndNestingAllocationFailuresDoNotEmitWhitespaceAndCanRetry(int optimization)
        => Run("""
            using std;extern void*? malloc(nuint count);extern void free(void* pointer);bool deny=false;
            namespace Memory{
                public replace void*? Allocate(nuint count){if(deny)return null;return malloc(count);}
                public replace void Free(void* pointer) nothrow{free(pointer);}
            }
            int main(){String text=new();text.Reserve(512);JsonWriter writer=new(&text,true);writer.WriteStartArray();
                nuint before=text.Length;char[1] invalid=[(char)255];int caught=0;
                try{writer.WriteString(invalid,1);}catch(ref JsonWriteException error){caught++;}
                try{writer.WriteRawNumber("01",2);}catch(ref JsonWriteException error){caught++;}
                try{writer.WriteEndObject();}catch(ref JsonWriteException error){caught++;}
                if(caught!=3||text.Length!=before)return 1;
                writer.WriteNull();writer.WriteStartArray();writer.WriteStartArray();writer.WriteStartArray();
                before=text.Length;deny=true;
                try{writer.WriteStartObject();return 2;}catch(ref BadAllocException error){}deny=false;
                if(text.Length!=before)return 3;
                writer.WriteStartObject();before=text.Length;
                try{writer.WritePropertyName(invalid,1);}catch(ref JsonWriteException error){caught++;}
                if(caught!=4||text.Length!=before)return 4;
                writer.WritePropertyName("x");writer.WriteBoolean(true);writer.WriteEndObject();
                for(int i=0;i<4;i++)writer.WriteEndArray();writer.Complete();
                JsonReader reader=new(text.Data,text.Length);while(reader.Read()){}
                return 0;}
            """,optimization);

    private static void Emit(StringBuilder code,JsonElement element,ref int id)
    {
        switch(element.ValueKind){
            case JsonValueKind.Object:
                code.AppendLine("writer.WriteStartObject();");foreach(var property in element.EnumerateObject()){
                    Text(code,"WritePropertyName",property.Name,ref id);Emit(code,property.Value,ref id);
                }code.AppendLine("writer.WriteEndObject();");break;
            case JsonValueKind.Array:
                code.AppendLine("writer.WriteStartArray();");foreach(var item in element.EnumerateArray())Emit(code,item,ref id);code.AppendLine("writer.WriteEndArray();");break;
            case JsonValueKind.String:Text(code,"WriteString",element.GetString()!,ref id);break;
            case JsonValueKind.Number:
                string raw=element.GetRawText();code.AppendLine($"writer.WriteRawNumber(\"{raw}\",{raw.Length});");break;
            default:code.AppendLine(element.ValueKind switch{JsonValueKind.True=>"writer.WriteBoolean(true);",JsonValueKind.False=>"writer.WriteBoolean(false);",_=>"writer.WriteNull();"});break;
        }
    }
    private static void Text(StringBuilder code,string method,string value,ref int id)
    {
        byte[] bytes=Encoding.UTF8.GetBytes(value);string name="part"+id++;
        code.AppendLine($"char[{Math.Max(1,bytes.Length)}] {name};");
        for(int i=0;i<bytes.Length;i++)code.AppendLine($"{name}[{i}]=(char){bytes[i]};");
        code.AppendLine($"writer.{method}({name},{bytes.Length});");
    }
    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",code));
    private static void Run(string code,int optimization)
        => Assert.Equal(0,CompilerTestHelper.Run(Sources(code),optimization).ExitCode);
}

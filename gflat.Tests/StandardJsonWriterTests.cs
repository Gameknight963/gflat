using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardJsonWriterTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("json-writer.gf", program));

    private static void Run(string program, int optimization, string output = "")
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"JSON writer exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        Assert.Equal(output, result.StandardOutput.Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidStructuralOperationsAreRetryableAndCompleteOnlyValidates(int optimization)
        => Run("""
            using std;
            extern int puts(readonly(char)* text);
            int main(){
                String text=s"";JsonWriter writer=new(&text);int caught=0;
                try{writer.Complete();}catch(JsonWriteException* error){caught++;}
                try{writer.WriteEndArray();}catch(JsonWriteException* error){caught++;}
                try{writer.WriteEndObject();}catch(JsonWriteException* error){caught++;}
                try{writer.WritePropertyName("x");}catch(JsonWriteException* error){caught++;}
                if(text.Length!=0 || caught!=4)return 1;
                writer.WriteStartObject();
                try{writer.WriteNull();}catch(JsonWriteException* error){caught++;}
                try{writer.WriteEndArray();}catch(JsonWriteException* error){caught++;}
                writer.WritePropertyName("same");
                try{writer.WriteEndObject();}catch(JsonWriteException* error){caught++;}
                try{writer.WritePropertyName("bad");}catch(JsonWriteException* error){caught++;}
                writer.WriteNumber(0);writer.WritePropertyName("same");writer.WriteStartArray();
                try{writer.WritePropertyName("bad");}catch(JsonWriteException* error){caught++;}
                try{writer.WriteEndObject();}catch(JsonWriteException* error){caught++;}
                writer.WriteBoolean(true);writer.WriteBoolean(false);writer.WriteNull();writer.WriteString("a\"b");
                try{writer.Complete();}catch(JsonWriteException* error){caught++;}
                writer.WriteEndArray();writer.WriteEndObject();writer.Complete();writer.Complete();
                nuint before=text.Length;
                try{writer.WriteNull();}catch(JsonWriteException* error){caught++;}
                if(caught!=12 || text.Length!=before)return 2;
                puts(text.Data);return 0;
            }
            """, optimization, "{\"same\":0,\"same\":[true,false,null,\"a\\\"b\"]}\n");

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidUtf8AndStrictRawNumbersLeaveOutputAndValuePositionUntouched(int optimization)
    {
        string[] invalid = ["", " ", " 1", "1 ", "+1", "01", "-", "1.", "1e", "1e+", "1 2", "1,", "1]", "NaN", "Infinity", "null", "true", "[]", "{}", "\"1\""];
        var code = new System.Text.StringBuilder("""
            using std;
            extern int puts(readonly(char)* text);
            int main(){String text=s"";JsonWriter writer=new(&text);writer.WriteStartObject();
                char[2] invalid=[(char)192,(char)128];int caught=0;
                try{writer.WritePropertyName(invalid,2);}catch(JsonWriteException* error){caught++;}
                if(caught!=1 || text.Length!=1)return 1;
                writer.WritePropertyName("values");writer.WriteStartArray();
                try{writer.WriteString(invalid,2);}catch(JsonWriteException* error){caught++;}
                if(caught!=2 || text.Length!=11)return 2;
            """);
        foreach (string value in invalid)
        {
            string literal = System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            code.AppendLine($$"""
                {nuint before=text.Length;try{writer.WriteRawNumber({{literal}},{{value.Length}});return 3;}
                 catch(JsonWriteException* error){}if(text.Length!=before)return 4;}
                """);
        }
        code.AppendLine("""
            writer.WriteRawNumber("-0.125e+9999",12);writer.WriteString("ok");
            writer.WriteEndArray();writer.WriteEndObject();writer.Complete();puts(text.Data);return 0;}
            """);
        Run(code.ToString(), optimization, "{\"values\":[-0.125e+9999,\"ok\"]}\n");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DestinationExceptionsRemainOriginalAndLatchAfterPartialOutput(int optimization)
        => Run("""
            using std;
            class SinkFailure:Exception{public SinkFailure():base("destination failed",77){}}
            struct Sink:ITextWriter{
                private String* text;public int Budget;public int Calls;
                public Sink(String* target){text=target;Budget=-1;Calls=0;}
                public void Write(readonly(char)* data,nuint count){
                    Calls++;
                    for(nuint i=0;i<count;i++){
                        if(Budget==0)throw new SinkFailure();
                        if(Budget>0)Budget--;text.Append(data[i]);
                    }
                }
            }
            bool Scenario(int mode){
                String text=s"";Sink sink=new(&text);JsonWriter writer=new(&sink);
                if(mode!=0){
                    if(mode==3 || mode==4 || mode==11)writer.WriteStartObject();
                    else writer.WriteStartArray();
                    if(mode==9 || mode==10)writer.WriteNull();
                    if(mode==11){writer.WritePropertyName("first");writer.WriteNull();}
                }
                sink.Budget=0;if(mode==1)sink.Budget=3;
                if(mode==4 || mode==5)sink.Budget=2;
                if(mode==10 || mode==12)sink.Budget=1;
                int caught=0;
                try{
                    if(mode==0)writer.WriteStartArray();
                    else if(mode==1)writer.WriteString("a\"b");
                    else if(mode==2)writer.WriteEndArray();
                    else if(mode==3)writer.WriteEndObject();
                    else if(mode==4)writer.WritePropertyName("name");
                    else if(mode==5)writer.WriteRawNumber("12345",5);
                    else if(mode==6)writer.WriteNumber(123);
                    else if(mode==7)writer.WriteBoolean(true);
                    else if(mode==10 || mode==13)writer.WriteStartObject();
                    else if(mode==11)writer.WritePropertyName("next");
                    else if(mode==12)writer.WriteString("");
                    else writer.WriteNull();
                }
                catch(SinkFailure* error){if(error.GetCode()!=77)return false;caught++;}
                catch{return false;}
                int calls=sink.Calls;nuint length=text.Length;sink.Budget=-1;
                try{writer.WriteNull();}catch(JsonWriteException* error){caught++;}
                try{writer.WriteStartArray();}catch(JsonWriteException* error){caught++;}
                try{writer.Complete();}catch(JsonWriteException* error){caught++;}
                return caught==4 && calls==sink.Calls && text.Length==length;
            }
            int main(){for(int mode=0;mode<14;mode++)if(!Scenario(mode))return mode+1;return 0;}
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedNestingAllocationDoesNotEmitDelimiterOrConsumeParentAndCanRetry(int optimization)
        => Run("""
            using std;
            extern int puts(readonly(char)* text);
            private extern void*? malloc(nuint bytes);private extern void free(void* pointer);
            bool deny=false;
            namespace Memory{
                replace void*? Allocate(nuint bytes){if(deny)return null;return malloc(bytes);}
                replace void Free(void* pointer) nothrow{free(pointer);}
            }
            int main(){String text=s"";text.Reserve(128);JsonWriter writer=new(&text);int caught=0;
                deny=true;try{writer.WriteStartArray();}catch(BadAllocException* error){caught++;}deny=false;
                if(caught!=1 || text.Length!=0)return 1;
                for(int i=0;i<4;i++)writer.WriteStartArray();
                deny=true;try{writer.WriteStartObject();}catch(BadAllocException* error){caught++;}deny=false;
                if(caught!=2 || text.Length!=4)return 2;
                writer.WriteStartObject();writer.WritePropertyName("ok");writer.WriteBoolean(true);writer.WriteEndObject();
                for(int i=0;i<4;i++)writer.WriteEndArray();writer.Complete();
                if(text.Length!=19)return 3;puts(text.Data);return 0;}
            """, optimization, "[[[[{\"ok\":true}]]]]\n");

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MovingPreservesStateAndDestructionDoesNotWriteOrDisposeDestination(int optimization)
        => Run("""
            using std;
            extern int puts(readonly(char)* text);
            int main(){String text=s"";
                {JsonWriter writer=new(&text);writer.WriteStartArray();JsonWriter moved=move writer;
                 moved.WriteNumber(1);moved.WriteEndArray();moved.Complete();}
                text.Append('!');
                {JsonWriter incomplete=new(&text);incomplete.WriteStartArray();}
                text.Append('!');puts(text.Data);return 0;}
            """, optimization, "[1]![!\n");

    [Fact]
    public void OwningWriterCannotBeImplicitlyCopied()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using std;int main(){String output=s"";JsonWriter writer=new(&output);JsonWriter copy=writer;return 0;}
            """)));
        Assert.Contains("copy", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

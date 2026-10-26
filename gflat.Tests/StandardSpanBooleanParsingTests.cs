using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardSpanBooleanParsingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactCountedViewsMatchStrictBooleanCorpusWithoutChangingInput(int optimization)
    {
        var values=new List<string>();
        foreach(string word in new[]{"true","false"})for(int mask=0;mask<(1<<word.Length);mask++)
            values.Add(new string(word.Select((letter,index)=>(mask&(1<<index))==0?letter:char.ToUpperInvariant(letter)).ToArray()));
        values.AddRange(["","t","tru","fals","trues","falsex","0","1","+true","-false"," true","false ",
            "\ttrue","false\r\n","t_rue","true\0","false\0","tr\0ue","fa\0lse","\u00fftrue"]);
        string path=Path.Combine(Path.GetTempPath(),"gflat_span_boolean_"+Guid.NewGuid().ToString("N")+".bin");
        try{
            using(var output=File.Create(path))foreach(string value in values){
                byte[] bytes=Encoding.Latin1.GetBytes(value);Assert.True(bytes.Length<13);
                byte[] record=Enumerable.Repeat((byte)'?',16).ToArray();
                record[0]=(byte)bytes.Length;bytes.CopyTo(record,1);
                bool valid=value.Equals("true",StringComparison.OrdinalIgnoreCase)||value.Equals("false",StringComparison.OrdinalIgnoreCase);
                record[14]=valid?(byte)1:(byte)0;record[15]=valid&&bool.Parse(value)?(byte)1:(byte)0;
                output.Write(record);
            }
            Run($$"""
                using std; using std::Memory;
                extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);extern int ferror(void* stream);
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;defer{fclose(file!);}
                    char[16] record;nuint seen=0;
                    while(true){
                        nuint bytes=fread((void*)&record[0],1,16,file!);if(bytes==0)break;if(bytes!=16)return 2;
                        nuint count=(nuint)(byte)record[0];char[16] original=record;
                        immutable(Span<readonly(char)>) text=new(&record[1],count);
                        bool expected=record[15]!=(char)0;bool valid=record[14]!=(char)0;bool actual=true;
                        if(text.TryParseBool(&actual)!=valid||actual!=expected)return 3;
                        actual=true;bool caught=false;
                        try{actual=text.ParseBool();if(!valid||actual!=expected)return 4;}
                        catch(ref Exception error){caught=true;}
                        if(caught==valid)return 5;
                        if(!valid&&!actual)return 6;
                        for(nuint i=0;i<16;i++)if(record[i]!=original[i])return 7;
                        seen++;
                    }
                    if(ferror(file!)!=0||seen!={{values.Count}})return 8;return 0;
                }
                """,optimization);
        }finally{File.Delete(path);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyViewsAndOverlappingResultKeepTryParsingAllocationFreeAndNothrow(int optimization)
    {
        const string program="""
            using std; using std::Memory;int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint bytes){allocations++;return null;}
                public replace void Free(void* storage) nothrow{}
            }
            bool CheckBooleanView(Span<readonly(char)> text,bool* result) nothrow=>text.TryParseBool(result);
            int main(){
                Span<readonly(char)> empty=new();Span<readonly(char)> zero=new((readonly(char)*)1,0);
                bool value=true;if(CheckBooleanView(empty,&value)||value)return 1;
                value=true;if(CheckBooleanView(zero,&value)||value)return 2;
                char[7] input=['?','T','r','U','e','?','?'];
                Span<readonly(char)> text=new(&input[1],4);
                if(!CheckBooleanView(text,&value)||!value)return 3;
                bool* overlapping=(bool*)&input[1];
                if(!CheckBooleanView(text,overlapping)||!*overlapping)return 4;
                for(nuint i=0;i<5;i++)input[i+1]="FaLsE"[i];
                text=new(&input[1],5);if(!CheckBooleanView(text,overlapping)||*overlapping)return 5;
                for(nuint i=0;i<5;i++)input[i+1]="falsX"[i];
                if(CheckBooleanView(text,overlapping)||*overlapping)return 6;
                if(input[0]!='?'||input[6]!='?'||allocations!=0)return 7;return 0;
            }
            """;
        string ir=Compiler.Emit(Sources(program));
        string body=Regex.Match(ir,@"(?ms)^define [^\r\n]*@[^\r\n ]*CheckBooleanView[^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);Assert.DoesNotContain("invoke",body);Assert.DoesNotContain("__gflat_eh_",body);
        string[] definitions=Regex.Matches(ir,@"(?m)^define [^\r\n]+").Select(match=>match.Value).Where(line=>line.Contains("$TryParseBool")).ToArray();
        Assert.NotEmpty(definitions);Assert.All(definitions,line=>Assert.Contains("nounwind",line));
        Run(program,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingDefaultAndZeroViewsDoNotReadMissingStorage(int optimization)
        =>Run("""
            using std; using std::Memory;
            int main(){
                Span<readonly(char)> empty=new();Span<readonly(char)> zero=new((readonly(char)*)1,0);
                bool value=true;int caught=0;
                try{value=empty.ParseBool();return 1;}catch(ref Exception error){caught++;}
                try{value=zero.ParseBool();return 2;}catch(ref Exception error){caught++;}
                if(caught!=2||!value)return 3;return 0;
            }
            """,optimization);

    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static IEnumerable<SourceFile> Sources(string code)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("span-boolean.gf",code));
    private static void Run(string code,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(code),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}

using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests.Std.Text;

public sealed class SpanIntegerParsingTests
{
    private static readonly (string Type,string Name)[] Types=[("byte","Byte"),("sbyte","SByte"),("short","Short"),("ushort","UShort"),("int","Int"),("uint","UInt"),("long","Long"),("ulong","ULong"),("extralong","ExtraLong"),("nint","NInt"),("nuint","NUInt")];

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryWidthForwardsTheExactCountAndExistingFailurePolicy(int optimization)
    {
        var texts=new HashSet<string>{"","0","+0","-0","+","-"," 12","12 ","1_2","0x12","12\0","12x","00012","+0012","-0012","\u00ff"};
        foreach(int width in new[]{8,16,32,64,128})foreach(BigInteger limit in new[]{BigInteger.One<<(width-1),BigInteger.One<<width})
            for(int delta=-1;delta<=1;delta++){texts.Add((limit+delta).ToString());texts.Add((-limit+delta).ToString());}
        string file=Path.Combine(Path.GetTempPath(),"gflat_span_parsing_"+Guid.NewGuid().ToString("N")+".bin");
        try{
            using(var output=File.Create(file))foreach(string text in texts){
                byte[] bytes=Encoding.Latin1.GetBytes(text),record=Enumerable.Repeat((byte)'x',64).ToArray();
                Assert.True(bytes.Length<63);record[0]=(byte)bytes.Length;bytes.CopyTo(record,1);output.Write(record);
            }
            string checks=string.Join("\n",Types.Select((type,index)=>$$"""
                { {{type.Type}} expected=({{type.Type}})99;{{type.Type}} actual=({{type.Type}})99;
                  bool valid=std::Text::TryParse{{type.Name}}(&record[1],count,&expected);
                  if(text.TryParse{{type.Name}}(&actual)!=valid||actual!=expected)return 2;
                  int caught=0;
                  try{actual=text.Parse{{type.Name}}();if(!valid||actual!=expected)return 3;}
                  catch(ref Exception error){caught++;}
                  if((valid&&caught!=0)||(!valid&&caught!=1))return 4; }
                """));
            string literal="\""+file.Replace("\\","\\\\")+"\"";
            Run($$"""
                using std; using std::Text; using std::Memory;extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* file);extern int fclose(void* file);
                int main(){
                    void*? input=fopen({{literal}},"rb");if(input==null)return 1;defer{fclose(input!);}
                    char[64] record;nuint seen=0;
                    while(fread((void*)&record[0],1,64,input!)==64){
                        nuint count=(nuint)(byte)record[0];Span<readonly(char)> text=new(&record[1],count);
                        char[64] original=record;
                        {{checks}}
                        for(nuint i=0;i<64;i++)if(record[i]!=original[i])return 5;
                        seen++;
                    }
                    if(seen!={{texts.Count}})return 6;return 0;
                }
                """,optimization);
        }
        finally{File.Delete(file);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DefaultEmptyOverlapAndNothrowForwardingNeedNoAllocation(int optimization)
    {
        string checks=string.Join("\n",Types.Select(type=>$$"""
            { {{type.Type}} result=({{type.Type}})99;
              if(!text.TryParse{{type.Name}}(&result)||result!=({{type.Type}})12)return false;
              if(empty.TryParse{{type.Name}}(&result)||result!=({{type.Type}})0)return false;
              if(zero.TryParse{{type.Name}}(&result)||result!=({{type.Type}})0)return false; }
            """));
        string program=$$"""
            using std; using std::Text; using std::Memory;int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){allocations++;return null;}
                public replace void Free(void* pointer) nothrow{}
            }
            bool SpanParsingCheck(Span<readonly(char)> text,Span<readonly(char)> empty,Span<readonly(char)> zero) nothrow{
                {{checks}}
                return true;
            }
            int main(){
                char[4] input=['x','1','2','x'];Span<readonly(char)> text=new(&input[1],2);
                Span<readonly(char)> empty=new();Span<readonly(char)> zero=new((readonly(char)*)1,0);
                if(!SpanParsingCheck(text,empty,zero))return 1;
                extralong[3] aligned;char* bytes=(char*)&aligned[0];bytes[0]='4';bytes[1]='2';
                Span<readonly(char)> overlap=new(bytes,2);
                if(!overlap.TryParseExtraLong(&aligned[0])||aligned[0]!=(extralong)42)return 2;
                bytes[0]='4';bytes[1]='x';
                if(overlap.TryParseExtraLong(&aligned[0])||aligned[0]!=(extralong)0)return 3;
                if(input[0]!='x'||input[1]!='1'||input[2]!='2'||input[3]!='x'||allocations!=0)return 4;return 0;
            }
            """;
        var sources=Sources(program).ToArray();string ir=Compiler.Emit(sources);
        string check=Regex.Match(ir,@"(?ms)^define [^\r\n]*@[^\r\n ]*SpanParsingCheck[^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(check);Assert.DoesNotContain("invoke",check);Assert.DoesNotContain("__gflat_eh_",check);
        string[] definitions=Regex.Matches(ir,@"(?m)^define [^\r\n]+").Select(match=>match.Value)
            .Where(line=>Types.Any(type=>line.Contains("@gflat$std$Text$TryParse"+type.Name+"(",StringComparison.Ordinal)||
                line.Contains("@gflat$std$Text$TryParse"+type.Name+"$overload$",StringComparison.Ordinal))).ToArray();
        Assert.NotEmpty(definitions);Assert.All(definitions,line=>Assert.Contains("nounwind",line));
        Run(program,optimization);
    }

    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("span-parsing.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}

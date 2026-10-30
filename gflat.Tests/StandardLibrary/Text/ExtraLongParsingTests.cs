using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class ExtraLongParsingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DecimalFormsMatchInt128AcrossLimitsSignsAndRandomBits(int optimization)
    {
        var texts=new List<string>{"0","-0","+0","0001","-0001","+0001",Int128.MinValue.ToString(),Int128.MaxValue.ToString(),
            ((BigInteger)Int128.MinValue-1).ToString(),((BigInteger)Int128.MinValue+1).ToString(),
            ((BigInteger)Int128.MaxValue-1).ToString(),((BigInteger)Int128.MaxValue+1).ToString(),
            "","+","-"," 1","1 ","1_0","0x10","1e2","--1","+-1","1x"};
        var random=new Random(9163);byte[] bytes=new byte[16];
        for(int i=0;i<64;i++){
            random.NextBytes(bytes);var value=new BigInteger(bytes,isUnsigned:false);
            texts.Add(value.ToString(CultureInfo.InvariantCulture));
        }
        var code=new StringBuilder("using std; using std::Text;int main(){extralong value=(extralong)91;\n");
        foreach(string text in texts)
        {
            bool valid=Int128.TryParse(text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var expected);
            BigInteger bits=(BigInteger)expected;if(bits<0)bits+=BigInteger.One<<128;
            ulong low=(ulong)(bits&ulong.MaxValue),high=(ulong)(bits>>64);
            code.AppendLine($$"""
                value=(extralong)91;
                if(TryParseExtraLong("{{text}}",{{text.Length}},&value)!={{valid.ToString().ToLowerInvariant()}})return 1;
                if((ulong)value!={{low}}ul||(ulong)(value>>64)!={{high}}ul)return 2;
                value=(extralong)91;
                if(TryParseExtraLong("{{text}}",&value)!={{valid.ToString().ToLowerInvariant()}})return 3;
                if((ulong)value!={{low}}ul||(ulong)(value>>64)!={{high}}ul)return 4;
                """);
            if(valid)code.AppendLine($"if(ParseExtraLong(\"{text}\")!=value||ParseExtraLong(\"{text}\",{text.Length})!=value)return 5;");
            else code.AppendLine($"try{{ParseExtraLong(\"{text}\",{text.Length});return 6;}}catch(ref Exception error){{}}try{{ParseExtraLong(\"{text}\");return 7;}}catch(ref Exception error){{}}");
        }
        code.AppendLine("return 0;}");Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedOverlapAndStringWrappersCommitOnlyAfterReadingWithoutAllocation(int optimization)
        => Run("""
            using std; using std::Text;extern void*? malloc(nuint size);extern void free(void* pointer);bool deny=false;int calls=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){if(deny){calls++;return null;}return malloc(size);}
                public replace void Free(void* pointer) nothrow{free(pointer);}
            }
            int main(){
                extralong[4] storage;char* text=((extralong*)storage).reinterpret<char*>();
                readonly(char)* minimum="-170141183460469231731687303715884105728";
                for(nuint i=0;i<40;i++)text[i]=minimum[i];
                deny=true;
                if(!TryParseExtraLong(text,40,&storage[0])||storage[0]!=((extralong)1<<127))return 1;
                text[0]='1';text[1]='2';text[2]='x';
                if(TryParseExtraLong(text,3,&storage[0])||storage[0]!=(extralong)0)return 2;
                extralong result=(extralong)9;
                if(TryParseExtraLong(((nuint)1).reinterpret<readonly(char)*>(),0,&result)||result!=(extralong)0)return 3;
                char[4] nul=['1','2','\0','9'];
                if(TryParseExtraLong(nul,4,&result)||result!=(extralong)0)return 4;
                if(!TryParseExtraLong(nul,&result)||result!=(extralong)12)return 5;
                nul[2]=(char)255;
                if(TryParseExtraLong(nul,3,&result)||result!=(extralong)0)return 6;
                char[4096] zeroes;for(nuint i=0;i<4096;i++)zeroes[i]='0';zeroes[4095]='7';
                if(!TryParseExtraLong(zeroes,4096,&result)||result!=(extralong)7)return 7;
                deny=false;
                String owner=String::From(minimum);immutable(String)* view=&owner;
                deny=true;
                if(!view.TryParseExtraLong(&result)||result!=((extralong)1<<127)||view.ParseExtraLong()!=result)return 8;
                deny=false;owner.Append('\0');deny=true;
                if(view.TryParseExtraLong(&result)||result!=(extralong)0)return 9;
                deny=false;
                if(calls!=0)return 10;return 0;
            }
            """,optimization,strings:true);

    [Fact]
    public void ExtraLongParserDoesNotIntroduceWideDivisionOrRemainder()
    {
        string ir=Compiler.Emit(Sources("using std; using std::Text;bool Test(readonly(char)* text,nuint count,extralong* value)nothrow=>TryParseExtraLong(text,count,value);"));
        Assert.False(NativeToolchain.RequiresWideIntegerRuntime(ir));
    }

    private static IEnumerable<SourceFile> Sources(string code,bool strings=false)
        => (strings?Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            :[Path.Combine(AppContext.BaseDirectory,"Std/core/Text/integer_parsing.gf")])
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("extralong-parsing.gf",code));
    private static void Run(string code,int optimization,bool strings=false)
    {
        var result=CompilerTestHelper.Run(Sources(code,strings),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}

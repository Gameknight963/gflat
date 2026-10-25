using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardStringLastIndexTests
{
    private static IEnumerable<SourceFile> Sources(string code)
        => new[]{"IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf","IStringConvertable.gf","String.gf", "ByteSearch.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("main.gf",code));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ByteOracleMatchesCountedAndCStringPatternsWithoutAllocatingDuringSearch(int optimization)
    {
        var random=new Random(9761);
        var code=new StringBuilder("""
            using std; using std::Collections;
            extern void*? malloc(nuint size);extern void free(void* storage);
            int allocations=0;int releases=0;
            namespace Memory {
                public replace void*? Allocate(nuint size){allocations++;return malloc(size);}
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            int main(){char[65] input;char[19] pattern;
            """);
        for(int test=0;test<72;test++)
        {
            byte[] input=new byte[random.Next(65)],pattern=new byte[random.Next(19)];random.NextBytes(input);random.NextBytes(pattern);
            if(test%2==0 && pattern.Length<=input.Length)input.AsSpan(random.Next(input.Length-pattern.Length+1),pattern.Length).CopyTo(pattern);
            Assign(code,"input",input);Assign(code,"pattern",pattern);
            code.AppendLine($"pattern[{pattern.Length}]='\\0';{{String value=String::From(input,{input.Length});immutable(String)* view=&value;int before=allocations;int freed=releases;");
            int found=input.AsSpan().LastIndexOf(pattern),zero=Array.IndexOf(pattern,(byte)0);
            byte[] cstring=zero<0?pattern:pattern[..zero];int cfound=input.AsSpan().LastIndexOf(cstring);
            code.AppendLine($"if(view.LastIndexOf(pattern,{pattern.Length})!={Index(found)})return 1;");
            code.AppendLine($"if(view.LastIndexOf(pattern)!={Index(cfound)})return 2;");
            byte key=(byte)random.Next(256);int character=input.AsSpan().LastIndexOf(key);
            code.AppendLine($"if(view.LastIndexOf((char){key})!={Index(character)})return 3;");
            code.AppendLine("if(allocations!=before||releases!=freed)return 4;}");
        }
        code.AppendLine("if(allocations!=releases)return 5;return 0;}");Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyPeriodicInternalAndEveryByteSearchUseReadonlyNothrowContract(int optimization)
    {
        string code="""
            using std; using std::Collections;
            bool Probe(immutable(String)* text,readonly(char)* key,nuint count,char value) nothrow {
                return text.LastIndexOf(value)!=String::NotFound && text.LastIndexOf(key,count)!=String::NotFound && text.LastIndexOf(key)!=String::NotFound;
            }
            int main() {
                String empty=new();immutable(String)* none=&empty;
                if(none.LastIndexOf('a')!=String::NotFound||none.LastIndexOf("")!=0)return 1;
                if(none.LastIndexOf((readonly(char)*)1,0)!=0||none.LastIndexOf((readonly(char)*)1,1)!=String::NotFound)return 2;
                char[256] bytes;for(int i=0;i<256;i++)bytes[i]=(char)i;
                String text=String::From(bytes,256);immutable(String)* view=&text;
                for(nuint i=0;i<256;i++)if(view.LastIndexOf((char)i)!=i)return 3;
                for(nuint i=0;i<=256;i++)if(view.LastIndexOf(view.Data+i,256-i)!=i)return 4;
                for(nuint i=1;i<=256;i++)if(view.LastIndexOf(view.Data+i)!=i)return 5;
                if(view.LastIndexOf(view.Data)!=256)return 6;
                if(view.LastIndexOf("\0",1)!=0||view.LastIndexOf("\0")!=256)return 7;
                if(!Probe(view,view.Data+254,2,(char)255))return 8;
                String repeated=s"abababa";
                if(repeated.LastIndexOf("aba")!=4||repeated.LastIndexOf("abababa")!=0||repeated.LastIndexOf("abababab")!=String::NotFound)return 9;
                String overlap=s"aaaaa";if(overlap.LastIndexOf("aaa")!=2)return 10;
                if(overlap.LastIndexOf('z')!=String::NotFound||overlap.LastIndexOf("")!=5)return 11;
                char[2048] large;for(nuint i=0;i<2048;i++)large[i]='a';
                String longText=String::From(large,2048);large[1023]='b';
                if(longText.LastIndexOf(large,1024)!=String::NotFound)return 12;
                large[1023]='a';if(longText.LastIndexOf(large,1024)!=1024)return 13;
                return 0;
            }
            """;
        string ir=Compiler.Emit(Sources(code));
        Match probe=Regex.Match(ir,@"(?ms)^define [^\r\n]*@gflat\$Probe\([^\r\n]*\{\r?\n(?<body>.*?)^\}");
        Assert.True(probe.Success);Assert.Contains("nounwind",probe.Value);
        Assert.DoesNotContain("invoke ",probe.Value);Assert.DoesNotContain("personality",probe.Value);
        string[] searches=ir.Split('\n').Where(line=>line.StartsWith("define ")&&Regex.IsMatch(line,@"\$String\$LastIndexOf(?:\$|\()")).ToArray();
        Assert.Equal(3,searches.Length);Assert.All(searches,line=>Assert.Contains("nounwind",line));
        Run(code,optimization);
    }

    private static string Index(int value)=>value<0?"String::NotFound":value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static void Assign(StringBuilder code,string name,byte[] bytes)
    {
        for(int i=0;i<bytes.Length;i++)code.AppendLine($"{name}[{i}]=(char){bytes[i]};");
    }
    private static void Run(string code,int optimization)=>Assert.Equal(0,CompilerTestHelper.Run(Sources(code),optimization).ExitCode);
}

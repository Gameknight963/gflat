using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardStringTrimTests
{
    private const string Counters="""
        using std;
        extern void*? malloc(nuint size);extern void free(void* storage);
        int allocations=0;int releases=0;
        namespace Memory {
            public replace void*? Allocate(nuint size){allocations++;return malloc(size);}
            public replace void Free(void* storage) nothrow{releases++;free(storage);}
        }
        void Apply(String* value,int mode) nothrow {
            if(mode==0)value.TrimStart();
            else if(mode==1)value.TrimEnd();
            else value.Trim();
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitAsciiTrimSetMatchesDotNetWithoutAllocationOrStorageReplacement(int optimization)
    {
        char[] whitespace=[' ','\t','\n','\r','\v','\f'];
        var random=new Random(8723);
        List<byte[]> cases=[[],[32],[32,9,10,11,12,13],Encoding.UTF8.GetBytes("  hello \0 world\n\t"),
            [9,0,32],[32,0xc2,0xa0,32],[9,0x85,0xa0,255,10],Encoding.UTF8.GetBytes("\t 😀 \r\n")];
        for(int test=0;test<32;test++)
        {
            byte[] value=new byte[random.Next(41)];random.NextBytes(value);
            for(int i=0;i<value.Length/3;i++)value[i]=(byte)whitespace[random.Next(whitespace.Length)];
            for(int i=value.Length*2/3;i<value.Length;i++)value[i]=(byte)whitespace[random.Next(whitespace.Length)];
            cases.Add(value);
        }
        var code=new StringBuilder(Counters+"int main(){char[64] input;\n");
        foreach(byte[] input in cases)
        {
            for(int i=0;i<input.Length;i++)code.AppendLine($"input[{i}]=(char){input[i]};");
            string text=Encoding.Latin1.GetString(input);
            for(int mode=0;mode<3;mode++)
            {
                string expected=mode switch{0=>text.TrimStart(whitespace),1=>text.TrimEnd(whitespace),_=>text.Trim(whitespace)};
                int start=mode==1?0:text.Length-text.TrimStart(whitespace).Length;
                code.AppendLine($$"""
                    {String value=String::From(input,{{input.Length}});
                     if({{mode}}!=2)value.Reserve(value.Length+17);
                     readonly(char)* before=value.Data;
                     nuint capacity=value.Capacity;int allocated=allocations;int freed=releases;
                     Apply(&value,{{mode}});
                     if(value.Length!={{expected.Length}}||value.Data!=before||value.Capacity!=capacity)return 1;
                     if(allocations!=allocated||releases!=freed||value.Data[value.Length]!='\0')return 2;
                     for(nuint i=0;i<value.Length;i++)if(value.Data[i]!=input[i+{{start}}])return 3;
                     Apply(&value,{{mode}});if(value.Length!={{expected.Length}}||allocations!=allocated)return 4;}
                    """);
            }
        }
        code.AppendLine("if(allocations!=releases)return 5;return 0;}");Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryByteOnlyTrimsSixWhitespaceValuesAndEmptyDefaultsRemainWritableLater(int optimization)
    {
        string code=Counters+"""
            int main(){
                String empty=new();empty.TrimStart();empty.TrimEnd();empty.Trim();
                if(allocations!=0||releases!=0||empty.Length!=0)return 1;
                empty.Append('x');empty.Trim();if(empty.Length!=1||empty.Data[0]!='x')return 2;
                char[3] input=[' ',(char)0,' '];
                for(int value=0;value<256;value++){
                    input[1]=(char)value;
                    bool whitespace=value==32||(value>=9&&value<=13);
                    for(int mode=0;mode<3;mode++){
                        String text=String::From(input,3);Apply(&text,mode);
                        if(whitespace){if(text.Length!=0||text.Data[0]!='\0')return 3;}
                        else{
                            nuint size=2;if(mode==2)size=1;
                            if(text.Length!=size)return 4;
                            nuint index=0;if(mode==1)index=1;
                            if(text.Data[index]!=(char)value||text.Data[text.Length]!='\0')return 5;
                            if(mode==0&&text.Data[1]!=' ')return 6;
                            if(mode==1&&text.Data[0]!=' ')return 7;
                        }
                    }
                }
                return 0;
            }
            """;
        string ir=Compiler.Emit(Sources(code));
        Match apply=Regex.Match(ir,@"(?ms)^define [^\r\n]*@gflat\$Apply\([^\r\n]*\{\r?\n(?<body>.*?)^\}");
        Assert.True(apply.Success);Assert.Contains("nounwind",apply.Value);Assert.DoesNotContain("invoke ",apply.Value);Assert.DoesNotContain("personality",apply.Value);
        Run(code,optimization);
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => new[]{"IHashable.gf","IEquatable.gf","IComparable.gf","Hashing.gf","IStringConvertable.gf","String.gf", "ByteSearch.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("main.gf",code));
    private static void Run(string code,int optimization)=>Assert.Equal(0,CompilerTestHelper.Run(Sources(code),optimization).ExitCode);
}

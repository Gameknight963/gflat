using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class StringJoinTests
{
    private const string Prelude="""
        using std; using std::Text; using std::Memory; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int allocations=0;int releases=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){if(deny)return null;allocations++;return malloc(count);}
            public replace void Free(void* storage) nothrow{releases++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedAndCStringSeparatorsMatchIndependentJoinWithoutOwnerCopies(int optimization)
    {
        var cases=new List<(byte[][] Values,byte[] Separator)>{([],[]),([[]],[]),([[],[]],[]),
            (["a"u8.ToArray()],"ignored"u8.ToArray()),(["a"u8.ToArray(),[],"c"u8.ToArray()],","u8.ToArray()),
            ([[65,0,255],[128,66]], [0,255,128]),([[],[],[]],"::"u8.ToArray())};
        var random=new Random(5529);
        for(int sample=0;sample<24;sample++){
            byte[][] values=Enumerable.Range(0,random.Next(7)).Select(_=>{byte[] bytes=new byte[random.Next(16)];random.NextBytes(bytes);return bytes;}).ToArray();
            byte[] separator=new byte[random.Next(5)];random.NextBytes(separator);cases.Add((values,separator));
        }
        var code=new StringBuilder(Prelude+"int main(){char[64] bytes;char[8] separator;char[160] expected;\n");
        foreach(var item in cases)
        {
            code.AppendLine("{Vector<String> fields=new();");
            foreach(byte[] bytes in item.Values){Assign(code,"bytes",bytes);code.AppendLine($"fields.Add(String::From(bytes,{bytes.Length}));");}
            Assign(code,"separator",item.Separator);code.AppendLine($"separator[{item.Separator.Length}]='\\0';");
            foreach(bool counted in new[]{true,false})
            {
                byte[] separator=counted?item.Separator:item.Separator.TakeWhile(b=>b!=0).ToArray();
                byte[] expected=Encoding.Latin1.GetBytes(string.Join(Encoding.Latin1.GetString(separator),item.Values.Select(Encoding.Latin1.GetString)));
                Assign(code,"expected",expected);
                string arguments=counted?$"separator,{separator.Length}":"separator";
                code.AppendLine($$"""
                    {int before=allocations;String joined=fields.AsImmutable().Join({{arguments}});
                     if(joined.Length!={{expected.Length}}||joined.Data[joined.Length]!='\0'||allocations!=before+{{(expected.Length==0?0:1)}})return 1;
                     for(nuint i=0;i<joined.Length;i++)if(joined.Data[i]!=expected[i])return 2;}
                    """);
            }
            for(int index=0;index<item.Values.Length;index++){
                Assign(code,"expected",item.Values[index]);
                code.AppendLine($"if(fields[{index}].Length!={item.Values[index].Length})return 3;for(nuint i=0;i<{item.Values[index].Length};i++)if(fields[{index}].Data[i]!=expected[i])return 4;");
            }
            code.AppendLine("}");
        }
        code.AppendLine("if(allocations!=releases)return 5;return 0;}");Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AliasesEmptySingletonOverflowAndAllocationFailurePreserveInputs(int optimization)
        => Run(Prelude+"""
            int main(){
                {Span<immutable(String)> empty=new();String joined=empty.Join((readonly(char)*)1,(nuint)0-(nuint)1);
                 String cempty=empty.Join((readonly(char)*)1);if(!joined.IsEmpty||!cempty.IsEmpty||allocations!=0)return 1;}
                {Vector<String> fields=new();fields.Add(s"one");
                 readonly(char)* original=fields[0].Data;int before=allocations;
                 {String one=fields.AsImmutable().Join((readonly(char)*)1,(nuint)0-(nuint)1);
                  if(one.Data==original||!CString::Equals(one.Data,"one")||allocations!=before+1)return 2;
                  one[0]='X';if(fields[0][0]!='o')return 3;}
                 {String one=fields.AsImmutable().Join((readonly(char)*)1);if(!CString::Equals(one.Data,"one"))return 4;}
                 fields.Add(s"two");fields.Add(s"three");
                 readonly(char)* separator=fields[1].Data;
                 {String joined=fields.AsImmutable().Join(separator+1,2);if(!CString::Equals(joined.Data,"onewotwowothree"))return 5;}
                 String destination=s"unchanged";before=allocations;deny=true;
                 try{destination=fields.AsImmutable().Join(separator);return 6;}catch(ref BadAllocException error){}
                 deny=false;
                 if(!CString::Equals(destination.Data,"unchanged")||fields.Count!=3||fields[0].Data!=original)return 7;
                 if(!CString::Equals(fields[1].Data,"two")||!CString::Equals(fields[2].Data,"three"))return 8;
                 try{fields.AsImmutable().Join((readonly(char)*)1,(nuint)0-(nuint)1);return 9;}
                 catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Joined String length overflow"))return 10;}
                 if(!CString::Equals(destination.Data,"unchanged")||fields[0].Data!=original)return 11;}
                if(allocations!=releases)return 12;return 0;
            }
            """,optimization);

    private static void Assign(StringBuilder code,string name,byte[] bytes){for(int i=0;i<bytes.Length;i++)code.AppendLine($"{name}[{i}]=(char){bytes[i]};");}
    private static void Run(string code,int optimization){
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",code));
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}

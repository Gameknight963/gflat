using System.Text;

namespace gflat.Tests;

public sealed class StandardTextSplitOnceSpanTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FirstSeparatorAndExactBorrowedSlicesMatchDotNetByteSearch(int optimization)
    {
        (byte[] Text,byte Separator)[] cases=[([],61),("="u8.ToArray(),61),("plain"u8.ToArray(),61),
            ("key="u8.ToArray(),61),("=value"u8.ToArray(),61),("a=b=c"u8.ToArray(),61),
            ("=="u8.ToArray(),61),(" a = b = c "u8.ToArray(),61),("a\0b\0c"u8.ToArray(),0),
            ([255,120,128],255),([255,120,128],128),([0],0)];
        var code=new StringBuilder("""
            using std; using std::Text; using std::Memory;int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){allocations++;return null;}
                public replace void Free(void* pointer) nothrow{}
            }
            int main(){char[34] data;
            """);
        foreach(var item in cases)
        {
            int index=item.Text.AsSpan().IndexOf(item.Separator);
            code.AppendLine($"{{data[0]='!';data[{item.Text.Length+1}]='?';");
            for(int i=0;i<item.Text.Length;i++)code.AppendLine($"data[{i+1}]=(char){item.Text[i]};");
            code.AppendLine($$"""
                Span<readonly(char)> text=new(&data[1],{{item.Text.Length}});
                Span<readonly(char)> before=new("stale",5);Span<readonly(char)> after=new("stale",5);
                bool found=text.TrySplitOnce((char){{item.Separator}},ref before,ref after);
                """);
            if(index<0)code.AppendLine("if(found||!before.IsEmpty||!after.IsEmpty||before.Data!=null||after.Data!=null)return 1;");
            else
            {
                int remaining=item.Text.Length-index-1;
                code.AppendLine($"if(!found||before.Length!={index}||after.Length!={remaining})return 2;");
                code.AppendLine(index==0?"if(before.Data!=null)return 3;":"if(before.Data!=&data[1])return 3;");
                code.AppendLine(remaining==0?"if(after.Data!=null)return 4;":$"if(after.Data!=&data[{index+2}])return 4;");
                for(int i=0;i<index;i++)code.AppendLine($"if(before[{i}]!=(char){item.Text[i]})return 5;");
                for(int i=index+1;i<item.Text.Length;i++)code.AppendLine($"if(after[{i-index-1}]!=(char){item.Text[i]})return 6;");
            }
            for(int i=0;i<item.Text.Length;i++)code.AppendLine($"if(data[{i+1}]!=(char){item.Text[i]})return 7;");
            code.AppendLine($"if(data[0]!='!'||data[{item.Text.Length+1}]!='?')return 8;}}");
        }
        code.AppendLine("if(allocations!=0)return 9;return 0;}");Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void KeyValueViewsRetainRemainderAndAllowReceiverMetadataReuseWithoutCopyingOwner(int optimization)
        => Run("""
            using std; using std::Text; using std::Memory;extern void*? malloc(nuint size);extern void free(void* pointer);
            int allocations=0;int releases=0;bool deny=false;
            namespace Memory{
                public replace void*? Allocate(nuint size){allocations++;if(deny)return null;return malloc(size);}
                public replace void Free(void* pointer) nothrow{releases++;free(pointer);}
            }
            int main(){
                {
                    String owner=s"mode=fast=checked";int before=allocations;deny=true;
                    Span<readonly(char)> text=owner.AsReadOnly();Span<readonly(char)> key=new();Span<readonly(char)> value=new();
                    if(!text.TrySplitOnce('=',ref key,ref value)||key.Length!=4||value.Length!=12)return 1;
                    if(!key.StartsWith("mode")||!value.StartsWith("fast=checked"))return 2;
                    owner[5]='l';if(value[0]!='l')return 3;
                    if(!text.TrySplitOnce('=',ref text,ref value)||text.Length!=4||value.Length!=12)return 4;
                    text=owner.AsReadOnly();
                    if(!text.TrySplitOnce('=',ref key,ref text)||key.Length!=4||text.Length!=12)return 5;
                    if(!text.StartsWith("last=checked")||allocations!=before)return 6;
                    Span<readonly(char)> empty=new();
                    if(empty.TrySplitOnce('=',ref key,ref value)||key.Data!=null||value.Data!=null)return 7;
                    deny=false;
                }
                if(allocations!=releases)return 8;return 0;
            }
            """,optimization);

    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("split-once.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}

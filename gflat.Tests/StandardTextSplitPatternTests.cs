namespace gflat.Tests;

public sealed class StandardTextSplitPatternTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedBinaryPatternsAndBorrowedSlicesMatchDotNetOracle(int optimization)
    {
        List<(byte[] Text,byte[] Pattern)> cases = [([],[]),([],"::"u8.ToArray()),("plain"u8.ToArray(),[]),
            ("key::value::rest"u8.ToArray(),"::"u8.ToArray()),("::value"u8.ToArray(),"::"u8.ToArray()),
            ("key::"u8.ToArray(),"::"u8.ToArray()),("::"u8.ToArray(),"::"u8.ToArray()),
            ("aaaaa"u8.ToArray(),"aaa"u8.ToArray()),("ababa"u8.ToArray(),"bab"u8.ToArray()),
            ("key\0=\0value"u8.ToArray(),[0,61,0]),([255,0,128,1,255],[128,1]),
            (Enumerable.Range(0,256).Select(v=>(byte)v).ToArray(),[253,254,255])];
        byte[] periodic=Enumerable.Repeat((byte)'a',8193).ToArray();periodic[^1]=(byte)'b';
        byte[] pattern=Enumerable.Repeat((byte)'a',1025).ToArray();pattern[^1]=(byte)'b';
        cases.Add((periodic,pattern));cases.Add((periodic,Enumerable.Repeat((byte)'a',1026).ToArray()));
        var random=new Random(82193);
        for(int i=0;i<80;i++){
            byte[] text=new byte[random.Next(513)],separator=new byte[random.Next(65)];
            random.NextBytes(text);random.NextBytes(separator);
            if(i%2==0&&separator.Length<=text.Length)separator.CopyTo(text,random.Next(text.Length-separator.Length+1));
            cases.Add((text,separator));
        }
        WithDirectory(directory=>{
            string path=Path.Combine(directory,"patterns.bin");
            using(var writer=new BinaryWriter(File.Create(path))){
                writer.Write(cases.Count);
                foreach(var item in cases){writer.Write(item.Text.Length);writer.Write(item.Pattern.Length);
                    writer.Write(item.Text.AsSpan().IndexOf(item.Pattern));writer.Write(item.Text);writer.Write(item.Pattern);}
            }
            Run(directory,"""
                using std;int allocations=0;
                namespace Memory{
                    public replace void*? Allocate(nuint bytes){allocations++;return null;}
                    public replace void Free(void* value){}
                }
                bool Check(Span<readonly(char)> text,Span<readonly(char)> separator,int expected,bool useSpan){
                    Span<readonly(char)> first=new("stale",5);Span<readonly(char)> rest=new("stale",5);
                    bool found=false;
                    if(useSpan)found=text.TrySplitOnce(separator,ref first,ref rest);
                    else found=text.TrySplitOnce(separator.Data!,separator.Length,ref first,ref rest);
                    if(expected<0)return !found&&first.Data==null&&rest.Data==null&&first.Length==0&&rest.Length==0;
                    nuint index=(nuint)expected; nuint end=index+separator.Length;
                    if(!found||first.Length!=index||rest.Length!=text.Length-end)return false;
                    if(index==0){if(first.Data!=null)return false;}
                    else if(first.Data!=text.Data)return false;
                    if(separator.Length==0){if(rest.Data!=text.Data)return false;}
                    else if(rest.IsEmpty){if(rest.Data!=null)return false;}
                    else if(rest.Data!=text.Data!+end)return false;
                    for(nuint i=0;i<first.Length;i++)if(first[i]!=text[i])return false;
                    for(nuint i=0;i<rest.Length;i++)if(rest[i]!=text[end+i])return false;
                    return true;
                }
                int main(int argc,char** argv){
                    File input=File::OpenRead(argv[1]);int count=FileBinary::ReadInt32LittleEndian(&input);
                    char[8195] text;char[1028] pattern;
                    for(int sample=0;sample<count;sample++){
                        int length=FileBinary::ReadInt32LittleEndian(&input);int width=FileBinary::ReadInt32LittleEndian(&input);
                        int expected=FileBinary::ReadInt32LittleEndian(&input);
                        for(nuint i=0;i<8195;i++)text[i]='?';for(nuint i=0;i<1028;i++)pattern[i]='!';
                        input.ReadExactly(&text[1],(nuint)length);input.ReadExactly(&pattern[1],(nuint)width);
                        char[8195] original=text;char[1028] originalPattern=pattern;
                        Span<readonly(char)> source=new(&text[1],(nuint)length);
                        Span<readonly(char)> separator=new(&pattern[1],(nuint)width);
                        if(!Check(source,separator,expected,false)||!Check(source,separator,expected,true))return 1;
                        for(nuint i=0;i<8195;i++)if(text[i]!=original[i])return 2;
                        for(nuint i=0;i<1028;i++)if(pattern[i]!=originalPattern[i])return 3;
                    }
                    input.Close();if(allocations!=0)return 4;return 0;
                }
                """,[path],optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConvenienceFormsSourceAliasesAndSeparatorAliasesKeepSnapshotMetadata(int optimization)
        => WithDirectory(directory=>Run(directory,"""
            using std;
            int main(){
                char[16] bytes=['k','e','y',':',':','v','a','l','u','e',':',':','r','e','s','t'];
                Span<readonly(char)> original=new(bytes,16);Span<readonly(char)> text=original;
                Span<readonly(char)> before=new();Span<readonly(char)> after=new();
                if(!text.TrySplitOnce("::",ref text,ref after)||text.Length!=3||after.Length!=11)return 1;
                text=original;if(!text.TrySplitOnce("::",2,ref before,ref text)||before.Length!=3||text.Length!=11)return 2;
                Span<readonly(char)> separator=new(&bytes[3],2);
                if(!original.TrySplitOnce(separator,ref separator,ref after)||separator.Length!=3||after.Length!=11)return 3;
                separator=new(&bytes[3],2);
                if(!original.TrySplitOnce(separator,ref before,ref separator)||before.Length!=3||separator.Length!=11)return 4;
                bytes[5]='V';if(after[0]!='V'||separator[0]!='V')return 5;
                char[4] pattern=[':',':','\0','!'];
                if(!original.TrySplitOnce(pattern,ref before,ref after)||before.Length!=3||after.Length!=11)return 6;
                if(original.TrySplitOnce(pattern,3,ref before,ref after)||before.Data!=null||after.Data!=null)return 7;
                char[2] needle=[':',':'];Span<readonly(char)> view=new(needle,2);
                if(!original.TrySplitOnce(view,ref before,ref after))return 8;
                needle[0]='?';if(before.Length!=3||after[0]!='V')return 9;
                if(!original.TrySplitOnce("",ref before,ref after)||before.Data!=null||after.Data!=original.Data)return 10;
                return 0;
            }
            """,[],optimization));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyAndImpossibleCountsPreflightWithoutReadingNullableOrFakePointers(int optimization)
        => WithDirectory(directory=>Run(directory,"""
            using std;int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint bytes){allocations++;return null;}
                public replace void Free(void* value){}
            }
            int main(){
                Span<readonly(char)> empty=new();Span<readonly(char)> before=new("stale",5);Span<readonly(char)> after=new("stale",5);
                if(!empty.TrySplitOnce((readonly(char)*)1,0,ref before,ref after)||before.Data!=null||after.Data!=null)return 1;
                if(empty.TrySplitOnce((readonly(char)*)1,1,ref before,ref after)||before.Data!=null||after.Data!=null)return 2;
                if(!empty.TrySplitOnce(empty,ref before,ref after)||before.Data!=null||after.Data!=null)return 3;
                char[1] storage=['x'];Span<readonly(char)> nonnullEmpty=new(storage,0);Span<readonly(char)> source=new(storage,1);
                if(!nonnullEmpty.TrySplitOnce(empty,ref before,ref after)||before.Data!=null||after.Data!=&storage[0])return 4;
                if(!source.TrySplitOnce(empty,ref before,ref after)||before.Data!=null||after.Data!=source.Data||after.Length!=1)return 5;
                if(source.TrySplitOnce((readonly(char)*)1,2,ref before,ref after)||before.Data!=null||after.Data!=null)return 6;
                if(source.TrySplitOnce((readonly(char)*)1,(nuint)0-(nuint)1,ref before,ref after))return 7;
                if(allocations!=0)return 8;return 0;
            }
            """,[],optimization));

    private static void WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-pattern-split-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);try{action(directory);}finally{Directory.Delete(directory,true);}
    }
    private static void Run(string directory,string program,string[] arguments,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("pattern-split.gf",program));
        string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");
        File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
        var result=NativeToolchain.Run(executable,arguments,30000);Assert.True(result.ExitCode==0,$"Pattern split exit{result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

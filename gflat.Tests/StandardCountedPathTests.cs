using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardCountedPathTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BoundedPrefixesMatchByteOracleForEveryByteAndExistingCStringPolicy(int optimization)
    {
        List<byte[]> values=[];
        foreach(string path in new[]{"","a","/","//","\\","\\\\","C:","C:child","C:/child","c:\\child","//host/share","/root/../file","a/b","1:/","_:x"}){
            byte[] bytes=Encoding.Latin1.GetBytes(path);
            for(int count=0;count<=bytes.Length;count++)values.Add(bytes[..count]);
        }
        foreach(string prefix in new[]{"C:/","//x","x:\\"})for(int at=0;at<3;at++)for(int value=0;value<256;value++){
            byte[] bytes=Encoding.Latin1.GetBytes(prefix);bytes[at]=(byte)value;
            for(int count=1;count<=3;count++)values.Add(bytes[..count]);
        }
        WithFixture(path=>{
            using(var stream=File.Create(path))foreach(byte[] value in values){
                byte[] record=Enumerable.Repeat((byte)'?',40).ToArray();record[0]=(byte)value.Length;value.CopyTo(record,1);
                record[36]=Rooted(value,false)?(byte)1:(byte)0;record[37]=Qualified(value,false)?(byte)1:(byte)0;
                record[38]=Rooted(value,true)?(byte)1:(byte)0;record[39]=Qualified(value,true)?(byte)1:(byte)0;stream.Write(record);
            }
            Run($$"""
                using std;extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);extern int ferror(void* stream);
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;defer{fclose(file!);}
                    char[40] record;char[34] terminated;nuint seen=0;
                    while(true){
                        nuint amount=fread((void*)&record[0],1,40,file!);if(amount==0)break;if(amount!=40)return 2;
                        char[40] original=record;nuint count=(nuint)(byte)record[0];
                        immutable(Span<readonly(char)>) text=new(&record[1],count);
                        if(Paths::IsRooted(text,PathStyle::Unix)!=(record[36]!=(char)0))return 3;
                        if(Paths::IsFullyQualified(text,PathStyle::Unix)!=(record[37]!=(char)0))return 4;
                        if(Paths::IsRooted(text,PathStyle::Windows)!=(record[38]!=(char)0))return 5;
                        if(Paths::IsFullyQualified(text,PathStyle::Windows)!=(record[39]!=(char)0))return 6;
                        bool noNull=true;
                        for(nuint i=0;i<count;i++){terminated[i]=record[i+1];if(terminated[i]=='\0')noNull=false;}
                        terminated[count]='\0';
                        if(noNull){
                            if(Paths::IsRooted(terminated,PathStyle::Unix)!=(record[36]!=(char)0))return 7;
                            if(Paths::IsFullyQualified(terminated,PathStyle::Unix)!=(record[37]!=(char)0))return 8;
                            if(Paths::IsRooted(terminated,PathStyle::Windows)!=(record[38]!=(char)0))return 9;
                            if(Paths::IsFullyQualified(terminated,PathStyle::Windows)!=(record[39]!=(char)0))return 10;
                        }
                        for(nuint i=0;i<40;i++)if(record[i]!=original[i])return 11;seen++;
                    }
                    if(ferror(file!)!=0||seen!={{values.Count}})return 12;return 0;
                }
                """,optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ClassificationAndEmptyJoinNeedNoAllocationOrMissingStorageReads(int optimization)
    {
        const string code="""
            using std;int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint bytes){allocations++;return null;}
                public replace void Free(void* storage) nothrow{}
            }
            bool ClassifyCounted(Span<readonly(char)> path,PathStyle style) nothrow{
                return Paths::IsRooted(path,style)||Paths::IsFullyQualified(path,style);
            }
            int main(){
                Span<readonly(char)> empty=new();Span<readonly(char)> zero=new((readonly(char)*)1,0);
                if(ClassifyCounted(empty,PathStyle::Unix)||ClassifyCounted(zero,PathStyle::Windows))return 1;
                char[3] root=['C',':','/'];Span<readonly(char)> view=new(root,3);
                if(!ClassifyCounted(view,PathStyle::Windows)||ClassifyCounted(view,PathStyle::Unix))return 2;
                String joined=Paths::Join(empty,zero,PathStyle::Unix);
                if(!joined.IsEmpty||joined.Data[0]!='\0'||allocations!=0)return 3;return 0;
            }
            """;
        string ir=Compiler.Emit(Sources(code));
        string body=Regex.Match(ir,@"(?ms)^define [^\r\n]*@[^\r\n ]*ClassifyCounted[^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);Assert.DoesNotContain("invoke",body);Assert.DoesNotContain("__gflat_eh_",body);
        Run(code,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void JoiningCountedBytesPreservesLexicalBoundaryPolicyAndIndependentOwnership(int optimization)
    {
        byte[][] parts= [[],"left"u8.ToArray(),"a/"u8.ToArray(),"a//"u8.ToArray(),"/right"u8.ToArray(),
            "//right"u8.ToArray(),"C:"u8.ToArray(),"C:/child"u8.ToArray(),"a\\"u8.ToArray(),"\\right"u8.ToArray(),
            [(byte)'a',0,255],[(byte)'/',0,(byte)'x']];
        WithFixture(path=>{
            using(var stream=new BinaryWriter(File.Create(path)))foreach(bool windows in new[]{false,true})foreach(byte[] left in parts)foreach(byte[] right in parts){
                byte[] expected=Joined(left,right,windows);stream.Write((uint)left.Length);stream.Write((uint)right.Length);stream.Write((uint)expected.Length);stream.Write(windows?(byte)1:(byte)0);
                stream.Write(left);stream.Write(right);stream.Write(expected);
            }
            Run($$"""
                using std;extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);extern int ferror(void* stream);
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;defer{fclose(file!);}
                    uint[3] header;char styleByte=(char)0;char[32] left;char[32] right;char[64] expected;nuint seen=0;
                    while(true){
                        nuint amount=fread((void*)&header[0],1,12,file!);if(amount==0)break;if(amount!=12)return 2;
                        if(fread((void*)&styleByte,1,1,file!)!=1)return 3;
                        nuint a=(nuint)header[0];nuint b=(nuint)header[1];nuint length=(nuint)header[2];
                        if(a>32||b>32||length>64)return 4;
                        if(fread((void*)&left[0],1,a,file!)!=a||fread((void*)&right[0],1,b,file!)!=b||fread((void*)&expected[0],1,length,file!)!=length)return 5;
                        PathStyle style=PathStyle::Unix;if(styleByte!=(char)0)style=PathStyle::Windows;
                        immutable(Span<readonly(char)>) first=new(left,a);Span<readonly(char)> second=new(right,b);
                        String result=Paths::Join(first,second,style);
                        if(result.Length!=length||result.Data[length]!='\0')return 6;
                        for(nuint i=0;i<length;i++)if(result[i]!=expected[i])return 7;
                        // Nonempty result owns bytes independently of either borrowed input.
                        if(a!=0)left[0]='!';if(b!=0)right[0]='!';
                        for(nuint i=0;i<length;i++)if(result[i]!=expected[i])return 8;
                        seen++;
                    }
                    if(ferror(file!)!=0||seen!={{parts.Length*parts.Length*2}})return 9;return 0;
                }
                """,optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void JoinAllocationFailuresAtEachGrowthPreserveInputsAndExistingDestination(int optimization)
        =>Run("""
            using std;extern void*? malloc(nuint bytes);extern void free(void* storage);
            int attempts=0;int failAt=0;int allocations=0;int releases=0;
            namespace Memory{
                public replace void*? Allocate(nuint bytes){
                    attempts++;if(failAt!=0&&attempts==failAt)return null;
                    void*? result=malloc(bytes);if(result!=null)allocations++;return result;
                }
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            int main(){
                char[1] left=['a'];char[4] right=['b','\0','c',(char)255];
                Span<readonly(char)> a=new(left,1);Span<readonly(char)> b=new(right,4);
                {
                    String destination=s"unchanged";readonly(char)* original=destination.Data;nuint capacity=destination.Capacity;int growthPoints=0;
                    {
                        attempts=0;String measured=Paths::Join(a,b,PathStyle::Unix);
                        growthPoints=attempts;if(growthPoints==0||measured.Length!=6||measured[3]!='\0')return 1;
                    }
                    for(int failure=1;failure<=growthPoints;failure++){
                        int balance=allocations-releases;attempts=0;failAt=failure;
                        try{destination=Paths::Join(a,b,PathStyle::Unix);return 2;}catch(ref BadAllocException error){}
                        failAt=0;
                        if(allocations-releases!=balance||destination.Data!=original||destination.Length!=9||destination.Capacity!=capacity)return 3;
                        if(!destination.StartsWith("unchanged")||destination.Data[9]!='\0')return 4;
                        if(left[0]!='a'||right[0]!='b'||right[1]!='\0'||right[2]!='c'||right[3]!=(char)255)return 5;
                    }
                }
                if(allocations!=releases)return 6;return 0;
            }
            """,optimization);

    private static bool Separator(byte value,bool windows)=>value=='/'||(windows&&value=='\\');
    private static bool Letter(byte value)=>(value>=(byte)'a'&&value<=(byte)'z')||(value>=(byte)'A'&&value<=(byte)'Z');
    private static bool Rooted(byte[] path,bool windows)=>path.Length!=0&&(Separator(path[0],windows)||(windows&&path.Length>=2&&Letter(path[0])&&path[1]==':'));
    private static bool Qualified(byte[] path,bool windows)
    {
        if(path.Length==0)return false;if(!windows)return path[0]=='/';
        if(Separator(path[0],true))return path.Length>=2&&Separator(path[1],true);
        return path.Length>=3&&Letter(path[0])&&path[1]==':'&&Separator(path[2],true);
    }
    private static byte[] Joined(byte[] left,byte[] right,bool windows)
    {
        if(left.Length==0)return right;if(right.Length==0)return left;
        bool leftSeparator=Separator(left[^1],windows),rightSeparator=Separator(right[0],windows);
        List<byte> output=[..left];if(!leftSeparator&&!rightSeparator)output.Add(windows?(byte)'\\':(byte)'/');
        output.AddRange(right.AsSpan(leftSeparator&&rightSeparator?1:0).ToArray());return output.ToArray();
    }
    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static void WithFixture(Action<string> action)
    {
        string path=Path.Combine(Path.GetTempPath(),"gflat_counted_paths_"+Guid.NewGuid().ToString("N")+".bin");
        try{action(path);}finally{File.Delete(path);}
    }
    private static IEnumerable<SourceFile> Sources(string code)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("counted-paths.gf",code));
    private static void Run(string code,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(code),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}



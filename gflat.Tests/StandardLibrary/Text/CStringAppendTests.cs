namespace gflat.Tests.Std.Text;

public sealed class CStringAppendTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryOverlapAndCapacityMatchesSnapshotCopyWithAtomicFailure(int optimization)
    {
        string file=Path.Combine(Path.GetTempPath(),"gflat_cstring_append_"+Guid.NewGuid().ToString("N")+".bin");
        int cases=0;
        try{
            using(var stream=File.Create(file))
            foreach(int source in new[]{0,1,3,9,17})foreach(int target in new[]{0,1,3,9,17})
            foreach(int sourceLength in new[]{0,1,3,7})foreach(int targetLength in new[]{0,1,3,7})
            foreach(int capacity in new[]{0,1,3,8,16,32}){
                byte[] initial=Enumerable.Range(0,64).Select(i=>(byte)(i*37%255+1)).ToArray();
                initial[target+targetLength]=0;initial[source+sourceLength]=0;
                byte[] expected=(byte[])initial.Clone();
                int outputLength=initial.AsSpan(target,capacity).IndexOf((byte)0);bool valid=false;
                if(outputLength>=0){
                    int inputLength=initial.AsSpan(source,capacity-outputLength).IndexOf((byte)0);
                    valid=inputLength>=0;
                    if(valid)initial.AsSpan(source,inputLength+1).CopyTo(expected.AsSpan(target+outputLength));
                }
                stream.Write([(byte)source,(byte)target,(byte)capacity,(byte)(valid?1:0)]);
                stream.Write(initial);stream.Write(expected);cases++;
            }
            string literal="\""+file.Replace("\\","\\\\")+"\"";
            Run($$"""
                using Standard; using Standard::Text;extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* file);extern int fclose(void* file);
                int main(){
                    void*? input=fopen({{literal}},"rb");if(input==null)return 1;defer{fclose(input!);}
                    byte[132] record;char[66] bytes;nuint seen=0;
                    while(fread((void*)&record[0],1,132,input!)==132){
                        bytes[0]='!';bytes[65]='?';for(nuint i=0;i<64;i++)bytes[i+1]=(char)record[i+4];
                        bool valid=CString::TryAppend(&bytes[(nuint)record[0]+1],&bytes[(nuint)record[1]+1],(nuint)record[2]);
                        if(valid!=(record[3]!=(byte)0))return 2;
                        for(nuint i=0;i<64;i++)if((byte)bytes[i+1]!=record[i+68])return 3;
                        if(bytes[0]!='!'||bytes[65]!='?')return 4;seen++;
                    }
                    if(seen!={{cases}})return 5;return 0;
                }
                """,optimization);
        }
        finally{File.Delete(file);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ScanBoundsEmptyAndUnterminatedBuffersNeedNoRuntimeServices(int optimization)
    {
        const string program="""
            using Standard; using Standard::Text;
            int main(){
                if(CString::TryAppend(((nuint)1).reinterpret<readonly(char)*>(),((nuint)1).reinterpret<char*>(),0))return 1;
                char[3] none=['a','b','c'];
                if(CString::TryAppend(((nuint)1).reinterpret<readonly(char)*>(),none,3)||none[0]!='a'||none[2]!='c')return 2;
                char[5] output=['a','\0','!','!','!'];char[2] shortInput=['b','c'];
                if(CString::TryAppend(shortInput,output,3)||output[1]!='\0'||output[2]!='!'||output[4]!='!')return 3;
                if(!CString::TryAppend("",output,2)||output[0]!='a'||output[1]!='\0'||output[2]!='!')return 4;
                if(CString::TryAppend("b",output,2)||output[1]!='\0'||output[2]!='!')return 5;
                if(!CString::TryAppend("b\0ignored",output,3)||output[0]!='a'||output[1]!='b'||output[2]!='\0'||output[3]!='!')return 6;
                char[7] empty=['\0','!','!','!','!','!','!'];char[3] high=[(char)128,(char)255,'\0'];
                if(!CString::TryAppend(high,empty,3)||empty[0]!=(char)128||empty[1]!=(char)255||empty[2]!='\0'||empty[3]!='!')return 7;
                char[7] self=['a','b','c','\0','x','x','!'];
                if(CString::TryAppend(self,self,6)||self[3]!='\0'||self[4]!='x'||self[6]!='!')return 8;
                return 0;
            }
            """;
        string ir=Compiler.Emit(Sources(program),options:new(){NoStdLib=true,NoBuiltins=true});
        foreach(string forbidden in new[]{"@__gflat_eh_","personality","gflat$Memory$","@malloc","@free"})Assert.DoesNotContain(forbidden,ir);
        Run(program,optimization);
    }

    private static SourceFile[] Sources(string program)=>[
        new("CString.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Standard/core/Text/CString.gf"))),
        new("append.gf",program)
    ];
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}

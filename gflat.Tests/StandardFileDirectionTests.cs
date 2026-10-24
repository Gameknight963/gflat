namespace gflat.Tests;

public sealed class StandardFileDirectionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TemporaryStreamsSupportExplicitFlushAndObservedEofDirectionTransitions(int optimization)
    {
        const string code="""
            using std;
            extern void*? malloc(nuint size);extern void free(void* storage);
            bool deny=false;int attempts=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){attempts++;if(deny)return null;return malloc(size);}
                public replace void Free(void* storage)nothrow{free(storage);}
            }
            int main(){
                File stream=File::OpenTemporary();
                if(!stream.IsOpen||stream.EndOfFile)return 1;
                int before=attempts;deny=true;
                stream.WriteByte('a');stream.Flush();
                if(stream.EndOfFile)return 2;
                if(stream.ReadByte()!=-1||!stream.EndOfFile)return 3;

                // The immediately preceding input encountered EOF, permitting output.
                stream.WriteByte('b');stream.Flush();
                // Do not require output to clear libc's observed EOF indicator.
                stream.ClearError();
                if(stream.EndOfFile)return 4;
                char[2] bytes=['?','!'];
                if(stream.Read(bytes,2)!=0||!stream.EndOfFile)return 5;
                if(bytes[0]!='?'||bytes[1]!='!')return 6;

                stream.Write("cd",2);stream.Flush();stream.ClearError();
                if(stream.PeekByte()!=-1||!stream.EndOfFile)return 7;
                // EOF lookahead is input too, and permits another output transition.
                stream.WriteByte('e');stream.Flush();stream.ClearError();
                if(stream.ReadByte()!=-1||!stream.EndOfFile)return 8;
                if(attempts!=before)return 9;
                stream.Close();if(stream.IsOpen)return 10;
                stream.Close();deny=false;return 0;
            }
            """;
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("file-direction.gf",code));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"File direction exit {result.ExitCode}: {result.StandardError}");
    }
}

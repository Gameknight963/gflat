using System.Text;

namespace gflat.Tests;

public sealed class StandardPathEndingTests
{
    private const string Allocator="""
        using std;extern void*? malloc(nuint size);extern void free(void* storage);
        bool deny=false;int attempts=0;int allocated=0;int released=0;
        namespace Memory{
            public replace void*? Allocate(nuint size){attempts++;if(deny)return null;void*? result=malloc(size);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitStylesPreserveExistingRootsAndRemoveExactlyOneSeparator(int optimization)
    {
        var rows=new List<(byte[] Path,bool Windows,int Length,bool Ends,bool CString)>();
        void Add(string path,string expected,bool windows){
            byte[] bytes=Encoding.UTF8.GetBytes(path);rows.Add((bytes,windows,Encoding.UTF8.GetByteCount(expected),path.Length!=0&&(path[^1]=='/'||(windows&&path[^1]=='\\')),true));
        }
        (string Path,string Expected)[] unix=[("",""),("/","/"),("//","/"),("///","//"),("a","a"),("a/","a"),
            ("a//","a/"),("dir/file/","dir/file"),("dir/./","dir/."),("/a/","/a"),("é/","é"),("a\\","a\\")];
        foreach(var row in unix){
            Add(row.Path,row.Expected,false);
            if(!OperatingSystem.IsWindows()){
                Assert.Equal(row.Expected,Path.TrimEndingDirectorySeparator(row.Path));
                Assert.Equal(row.Path.EndsWith('/'),Path.EndsInDirectorySeparator(row.Path));
            }
        }
        (string Path,string Expected)[] windows=[("",""),("\\","\\"),("/","/"),("C:","C:"),("C:\\","C:\\"),
            ("C:/","C:/"),("C:\\\\","C:\\"),("C:dir\\","C:dir"),("C:\\dir\\\\","C:\\dir\\"),
            ("dir/\\","dir/"),("\\\\","\\\\"),("\\\\server","\\\\server"),("\\\\server\\","\\\\server\\"),
            ("\\\\server\\\\","\\\\server\\\\"),("\\\\server\\share\\","\\\\server\\share\\"),
            ("\\\\server\\share\\dir\\","\\\\server\\share\\dir"),("//server/share//","//server/share/"),
            ("\\\\?\\C:\\","\\\\?\\C:\\"),("\\\\?\\C:\\dir\\","\\\\?\\C:\\dir")];
        foreach(var row in windows){
            Add(row.Path,row.Expected,true);
            if(OperatingSystem.IsWindows()&&!row.Path.StartsWith("\\\\",StringComparison.Ordinal)&&!row.Path.StartsWith("//",StringComparison.Ordinal)){
                Assert.Equal(row.Expected,Path.TrimEndingDirectorySeparator(row.Path));
                Assert.Equal(row.Path.Length!=0&&(row.Path[^1]=='/'||row.Path[^1]=='\\'),Path.EndsInDirectorySeparator(row.Path));
            }
        }
        rows.Add(([97,0,98,47],false,3,true,false));rows.Add(([97,47,0],false,3,false,false));
        rows.Add(([255,47],false,1,true,false));rows.Add(([67,0,92],true,2,true,false));
        using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){
            writer.Write((uint)rows.Count);foreach(var row in rows){writer.Write((uint)row.Path.Length);writer.Write(row.Path);writer.Write(row.Windows);writer.Write((uint)row.Length);writer.Write(row.Ends);writer.Write(row.CString);}
        }
        string packed=Convert.ToHexString(stream.ToArray());
        Run(Allocator+$$"""
            uint Hex(char c)nothrow{if(c<='9')return(uint)(c-'0');return(uint)(c-'A')+10u;}
            byte Next(readonly(char)* packed,ref nuint offset)nothrow{uint value=Hex(packed[offset])*16u+Hex(packed[offset+1]);offset+=2;return(byte)value;}
            nuint Size(readonly(char)* packed,ref nuint offset)nothrow{nuint result=0;for(nuint i=0;i<4;i++)result=result|((nuint)Next(packed,ref offset)<<(i*8));return result;}
            int main(){readonly(char)* packed="{{packed}}";nuint offset=0;nuint rows=Size(packed,ref offset);char[128] input;char[128] saved;
                for(nuint row=0;row<rows;row++){
                    nuint length=Size(packed,ref offset);for(nuint i=0;i<length;i++){input[i]=(char)Next(packed,ref offset);saved[i]=input[i];}input[length]='\0';
                    PathStyle style=PathStyle::Unix;if(Next(packed,ref offset)!=0)style=PathStyle::Windows;
                    nuint expected=Size(packed,ref offset);bool ends=Next(packed,ref offset)!=0;bool cstring=Next(packed,ref offset)!=0;
                    immutable(char)* frozen=input;Span<readonly(char)> source=new(frozen,length);
                    int before=attempts;deny=true;
                    if(Paths::EndsInDirectorySeparator(source,style)!=ends)return 1;
                    Span<readonly(char)> result=Paths::TrimEndingDirectorySeparator(source,style);
                    if(result.Length!=expected)return 2;
                    if(expected==0){if(result.Data!=null)return 3;}else if(result.Data!=frozen)return 4;
                    if(cstring&&Paths::EndsInDirectorySeparator(frozen,style)!=ends)return 5;
                    deny=false;if(attempts!=before)return 6;
                    if(cstring){
                        {String owned=Paths::TrimEndingDirectorySeparator(frozen,style);
                         if(owned.Length!=expected||owned.Data[expected]!='\0')return 7;
                         if(expected!=0&&owned.Data==frozen)return 8;
                         for(nuint i=0;i<expected;i++)if(owned.Data[i]!=saved[i])return 9;}
                        if(allocated!=released)return 10;
                    }
                    for(nuint i=0;i<length;i++)if(input[i]!=saved[i])return 11;
                }
                Span<readonly(char)> absent=new();deny=true;
                if(Paths::EndsInDirectorySeparator(absent,PathStyle::Windows))return 12;
                Span<readonly(char)> empty=Paths::TrimEndingDirectorySeparator(absent,PathStyle::Windows);
                if(!empty.IsEmpty||empty.Data!=null)return 13;
                String owned=Paths::TrimEndingDirectorySeparator("",PathStyle::Unix);
                if(!owned.IsEmpty)return 14;deny=false;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningFailurePreservesInputAndAssignmentDestination(int optimization)
        =>Run(Allocator+"""
            int main(){
                char[7] input=['d','i','r','/','/','\0','!'];
                {String destination=s"kept";readonly(char)* original=destination.Data;nuint capacity=destination.Capacity;
                 int before=attempts;deny=true;bool caught=false;
                 try{destination=Paths::TrimEndingDirectorySeparator(input,PathStyle::Unix);}
                 catch(ref BadAllocException error){caught=true;}
                 deny=false;
                 if(!caught||attempts<=before||destination.Data!=original||destination.Capacity!=capacity||!destination.Equals("kept"))return 1;
                 if(input[0]!='d'||input[1]!='i'||input[2]!='r'||input[3]!='/'||input[4]!='/'||input[5]!='\0'||input[6]!='!')return 2;
                 before=attempts;destination=Paths::TrimEndingDirectorySeparator(input,PathStyle::Unix);
                 if(attempts!=before+1||!destination.Equals("dir/")||input[4]!='/')return 3;
                 input[0]='X';if(!destination.Equals("dir/"))return 4;}
                if(allocated!=released)return 5;return 0;
            }
            """,optimization);

    private static IEnumerable<SourceFile> Sources(string code)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("path-ending.gf",code));
    private static void Run(string code,int optimization)=>Assert.Equal(0,CompilerTestHelper.Run(Sources(code),optimization).ExitCode);
}

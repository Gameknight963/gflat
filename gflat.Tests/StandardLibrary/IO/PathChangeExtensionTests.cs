namespace gflat.Tests.Std.IO;

public sealed class PathChangeExtensionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitStylesAndExistingFilenameRulesMatchGoldenCases(int optimization)
    {
        (bool Windows, string Path, string Extension, string Expected)[] rows =
        [
            (false,"","txt",""), (false,"/","txt","/"), (false,"//","x","//"),
            (false,"dir/","txt","dir/"), (false,"dir/file.old","new","dir/file.new"),
            (false,"dir/file.old",".new","dir/file.new"), (false,"dir/file.tar.gz","","dir/file.tar"),
            (false,"dir.with.dot/file","new","dir.with.dot/file.new"),
            (false,".config","json",".json"), (false,"file.","txt","file.txt"),
            (false,"file.old",".","file."), (false,"file.old","..","file.."),
            (false,"file.old","","file"), (false,".","",""), (false,"..","x","..x"),
            (false,@"folder.old\name","new","folder.new"),
            (true,@"folder.old\name","new",@"folder.old\name.new"),
            (false,@"C:\","new",@"C:\.new"),
            (true,"C:","txt","C:"), (true,"C:/","txt","C:/"),
            (true,"C:file.old","new","C:file.new"),
            (true,@"C:\dir\.config","json",@"C:\dir\.json"),
            (true,@"\\server","new",@"\\server"),
            (true,@"\\server\share","txt",@"\\server\share"),
            (true,@"\\server\share\","txt",@"\\server\share\"),
            (true,@"\\server\share\file.old","new",@"\\server\share\file.new"),
            (true,@"C:\dir/file.old","new",@"C:\dir/file.new"),
            (true,@"\file.old","new",@"\file.new")
        ];
        string Values(IEnumerable<string> values) => string.Join(",", values.Select(Literal));
        Run($$"""
            using Standard::Text; using Standard::IO; using Standard::Memory;
            int main() {
                readonly(char)*[{{rows.Length}}] paths=[{{Values(rows.Select(r=>r.Path))}}];
                readonly(char)*[{{rows.Length}}] suffixes=[{{Values(rows.Select(r=>r.Extension))}}];
                readonly(char)*[{{rows.Length}}] expected=[{{Values(rows.Select(r=>r.Expected))}}];
                bool[{{rows.Length}}] windows=[{{string.Join(",",rows.Select(r=>r.Windows?"true":"false"))}}];
                for(nuint i=0;i<{{rows.Length}};i++) {
                    PathStyle style=PathStyle::Unix; if(windows[i])style=PathStyle::Windows;
                    String owned=Paths::ChangeExtension(paths[i],suffixes[i],style);
                    Span<readonly(char)> path=new(paths[i],CString::Length(paths[i]));
                    Span<readonly(char)> extension=new(suffixes[i],CString::Length(suffixes[i]));
                    String counted=Paths::ChangeExtension(path,extension,style);
                    if(!owned.Equals(expected[i])||!counted.Equals(expected[i]))return 1;
                    if(owned.Data[owned.Length]!='\0'||counted.Data[counted.Length]!='\0')return 2;
                }
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedBytesOverlappingInputsAndSelfAssignmentKeepIndependentOwnership(int optimization)
        =>Run("""
            using Standard::Text; using Standard::IO; using Standard::Memory;
            int main() {
                char[11] bytes=['!','d','/','f','\0',(char)255,'.','x','.', 'z','!'];
                char[5] suffix=['!','q','\0',(char)254,'!'];
                Span<readonly(char)> path=new(&bytes[1],7);
                Span<readonly(char)> extension=new(&suffix[1],3);
                String changed=Paths::ChangeExtension(path,extension,PathStyle::Unix);
                char[8] expected=['d','/','f','\0',(char)255,'.','q','\0'];
                if(changed.Length!=9||changed[8]!=(char)254)return 1;
                // Equals(pointer,count) compares the whole owner, so verify its prefix directly.
                for(nuint i=0;i<8;i++)if(changed[i]!=expected[i])return 2;
                if(changed.Data[9]!='\0'||bytes[0]!='!'||bytes[8]!='.'||suffix[4]!='!')return 3;
                bytes[1]='X';suffix[1]='X';if(changed[0]!='d'||changed[6]!='q')return 4;
                char[12] overlap=['d','i','r','/','f','.','o','l','d','\0','!','!'];
                path=new(overlap,9);extension=new(&overlap[5],4);
                String same=Paths::ChangeExtension(path,extension,PathStyle::Unix);
                overlap[0]='X';overlap[6]='X';if(!same.Equals("dir/f.old"))return 5;
                String owner=s"dir/file.old";
                owner=Paths::ChangeExtension(owner.AsReadOnly(),owner.AsReadOnly().Slice(4,3),PathStyle::Unix);
                if(!owner.Equals("dir/file.fil"))return 6;
                String cstring=s"dir/file.old";
                cstring=Paths::ChangeExtension(cstring.Data,cstring.Data+8,PathStyle::Unix);
                if(!cstring.Equals("dir/file.old"))return 7;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyRangesUnusedReplacementAndLengthOverflowDoNotTouchInputs(int optimization)
        =>Run("""
            using Standard::Text; using Standard::IO; using Standard::Memory;
            int main() {
                Span<readonly(char)> empty=new(((nuint)1).reinterpret<readonly(char)*>(),0);
                Span<readonly(char)> suffix=new("txt",3);
                String result=Paths::ChangeExtension(empty,suffix,PathStyle::Unix);
                if(!result.IsEmpty)return 1;
                Span<readonly(char)> path=new("name.old",8);
                result=Paths::ChangeExtension(path,empty,PathStyle::Unix);
                if(!result.Equals("name"))return 2;
                // No-filename input never inspects replacement bytes.
                suffix=new(((nuint)1).reinterpret<readonly(char)*>(),(nuint)0-1);
                path=new("C:",2);result=Paths::ChangeExtension(path,suffix,PathStyle::Windows);
                if(!result.Equals("C:"))return 3;
                result=Paths::ChangeExtension("dir/",((nuint)1).reinterpret<readonly(char)*>(),PathStyle::Unix);
                if(!result.Equals("dir/"))return 4;
                String destination=s"kept";readonly(char)* original=destination.Data;
                path=new("a",1);nuint maximum=(nuint)0-1;int caught=0;
                // Impossible sizes must reject before copying or allocating result storage.
                suffix=new(".",maximum);
                try{destination=Paths::ChangeExtension(path,suffix,PathStyle::Unix);return 5;}catch(ref Exception error){caught++;}
                suffix=new("x",maximum-1);
                try{destination=Paths::ChangeExtension(path,suffix,PathStyle::Unix);return 6;}catch(ref Exception error){caught++;}
                if(caught!=2||destination.Data!=original||!destination.Equals("kept"))return 7;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationFailuresPreserveBorrowedInputsAndExistingDestination(int optimization)
        =>Run("""
            using Standard; using Standard::Text; using Standard::IO; using Standard::Memory;
            extern void*? malloc(nuint bytes);extern void free(void* storage);
            int attempts=0;int failAt=0;int allocations=0;int releases=0;
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    attempts++;if(failAt!=0&&attempts==failAt)return null;
                    void*? result=malloc(bytes);if(result!=null)allocations++;return result;
                }
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            String Change(bool counted,readonly(char)* path,readonly(char)* extension) {
                if(counted){Span<readonly(char)> p=new(path,9);Span<readonly(char)> e=new(extension,3);
                    return Paths::ChangeExtension(p,e,PathStyle::Unix);}
                return Paths::ChangeExtension(path,extension,PathStyle::Unix);
            }
            int main() {
                char[11] path=['d','i','r','/','f','.','o','l','d','\0','!'];
                char[5] extension=['n','e','w','\0','!'];
                {
                    String destination=s"kept";readonly(char)* original=destination.Data;nuint capacity=destination.Capacity;
                    for(int form=0;form<2;form++) {
                        int points=0;{attempts=0;String measured=Change(form!=0,path,extension);points=attempts;if(!measured.Equals("dir/f.new")||points==0)return 1;}
                        for(int failure=1;failure<=points;failure++) {
                            int balance=allocations-releases;attempts=0;failAt=failure;
                            try{destination=Change(form!=0,path,extension);return 2;}catch(ref BadAllocException error){}
                            failAt=0;
                            if(allocations-releases!=balance||destination.Data!=original||destination.Capacity!=capacity||!destination.Equals("kept"))return 3;
                            if(path[0]!='d'||path[6]!='o'||path[9]!='\0'||path[10]!='!'||extension[0]!='n'||extension[3]!='\0'||extension[4]!='!')return 4;
                        }
                    }
                }
                if(allocations!=releases)return 5;return 0;
            }
            """,optimization);

    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static IEnumerable<SourceFile> Sources(string code)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("path-change-extension.gf",code));
    private static void Run(string code,int optimization) {
        var result=CompilerTestHelper.Run(Sources(code),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}

using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests.Std.IO;

public sealed class FileLineSequenceTests
{
    private const string Counters="""
        using std; using std::Text; using std::IO; using std::Queries; using std::Collections;
        private extern void*? malloc(nuint bytes);private extern void free(void* storage);
        int allocations=0;int releases=0;bool failAllocation=false;
        namespace Memory{
            public replace void*? Allocate(nuint bytes){if(failAllocation)return null;void*? result=malloc(bytes);if(result!=null)allocations++;return result;}
            public replace void Free(void* storage) nothrow{releases++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedLinesPreserveZerosBareCrAndFinalLineAndMaterializeIndependentStrings(int optimization)
        => Run(Encoding.UTF8.GetBytes("\r\none\nA\0B\rX\r\nlast\r"),path=>Counters+$$"""
            int main(){ {
                File input=File::OpenRead({{path}});File* source=&input;var view=source.Lines();
                if(allocations!=0 || input.PeekByte()!=13)return 1;
                Vector<String> lines=view.AsEnumerable().ToVector();
                if(lines.Count!=4 || lines[0].Length!=0 || lines[1].Length!=3 || lines[1][0]!='o')return 2;
                if(lines[2].Length!=5 || lines[2][0]!='A' || lines[2][1]!='\0' || lines[2][2]!='B' || lines[2][3]!='\r' || lines[2][4]!='X')return 3;
                if(lines[3].Length!=5 || lines[3][4]!='\r' || !input.IsOpen || !input.EndOfFile)return 4;
                lines[1][0]='O';if(lines[2][0]!='A' || lines[3][0]!='l')return 5;
                if(view.AsEnumerable().Count()!=0 || !input.IsOpen)return 6;
            }return allocations-releases;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyInputAndReadonlyCurrentDoNotAllocateCopies(int optimization)
    {
        Run([],path=>Counters+$$"""
            int main(){File input=File::OpenRead({{path}});File* source=&input;var view=source.Lines();
                {var iterator=view.GetEnumerator();if(allocations!=0)return 1;
                    int caught=0;try{iterator.Current;}catch{caught++;}int before=allocations;
                    if(iterator.MoveNext() || iterator.MoveNext() || allocations!=before)return 2;
                    try{iterator.Current;}catch{caught++;}
                    if(caught!=2 || allocations!=releases || !input.IsOpen)return 3;
                }return allocations-releases;}
            """,optimization);
        Run(Encoding.UTF8.GetBytes("first\nsecond\n"),path=>Counters+$$"""
            int main(){ {File input=File::OpenRead({{path}});File* source=&input;
                {var iterator=source.Lines().GetEnumerator();if(!iterator.MoveNext())return 1;
                    int before=allocations;readonly(char)* address=iterator.Current.Data;
                    if(iterator.Current.Length!=5 || iterator.Current[0]!='f' || iterator.Current.Data!=address || allocations!=before)return 2;
                    String copied=iterator.Current;if(copied.Data==address || !iterator.MoveNext() || copied[0]!='f' || iterator.Current[0]!='s')return 3;
                }if(allocations!=releases || !input.IsOpen)return 4;
            }return allocations-releases;}
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EarlyBreakAndThrowingQueriesReleaseLinesButDoNotCloseOrReplayTheFile(int optimization)
        => Run(Encoding.UTF8.GetBytes("first\nsecond\nthird\n"),path=>Counters+$$"""
            int calls=0;bool Fail(String line){calls++;if(calls==2)throw new Exception("predicate");return true;}
            int main(){
                {File input=File::OpenRead({{path}});File* source=&input;var view=source.Lines();
                    foreach(String line in view){if(line.Length!=5)return 1;break;}
                    if(allocations!=releases || !input.IsOpen || input.PeekByte()!='s')return 2;
                    {Vector<String> remainder=view.AsEnumerable().ToVector();if(remainder.Count!=2 || remainder[0][0]!='s' || remainder[1][0]!='t')return 3;}
                    if(view.AsEnumerable().Any() || !input.IsOpen || allocations!=releases)return 4;
                }
                {File input=File::OpenRead({{path}});File* source=&input;var query=source.Lines().AsEnumerable();bool caught=false;
                    try{query.Where(Fail).ToVector();}catch{caught=true;}
                    if(!caught || calls!=2 || allocations!=releases || !input.IsOpen || input.PeekByte()!='t')return 5;
                    if(query.Count()!=1 || !input.IsOpen || allocations!=releases)return 6;
                }return allocations-releases;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LongLinesGrowOnceAndReuseTheBufferForEmptyAndShortLines(int optimization)
        => Run(Encoding.UTF8.GetBytes(new string('a',1300)+"\r\n\nb\n"+new string('c',513)),path=>Counters+$$"""
            int main(){ {File input=File::OpenRead({{path}});File* source=&input;
                {var iterator=source.Lines().GetEnumerator();
                    if(!iterator.MoveNext() || iterator.Current.Length!=1300 || iterator.Current[1299]!='a')return 1;
                    int before=allocations;nuint capacity=iterator.Current.Capacity;
                    if(!iterator.MoveNext() || iterator.Current.Length!=0 || iterator.Current.Capacity!=capacity || allocations!=before)return 2;
                    if(!iterator.MoveNext() || iterator.Current.Length!=1 || iterator.Current[0]!='b' || allocations!=before)return 3;
                    if(!iterator.MoveNext() || iterator.Current.Length!=513 || iterator.Current[512]!='c' || allocations!=before)return 4;
                    if(iterator.MoveNext() || !input.IsOpen)return 5;
                }if(allocations!=releases || !input.IsOpen)return 6;
            }return allocations-releases;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationAndReadFailuresDisposeCurrentStorageAndLeaveTheBorrowedFileOpen(int optimization)
        => Run(Encoding.UTF8.GetBytes("first\nsecond\n"),path=>Counters+$$"""
            int main(){
                {File input=File::OpenRead({{path}});File* source=&input;
                    {var iterator=source.Lines().GetEnumerator();failAllocation=true;bool caught=false;
                        try{iterator.MoveNext();}catch{caught=true;}failAllocation=false;
                        if(!caught || !input.IsOpen || input.PeekByte()!='s')return 1;
                        caught=false;try{iterator.Current;}catch{caught=true;}if(!caught)return 2;
                    }
                    if(allocations!=releases)return 3;
                    {String next=source.Lines().AsEnumerable().First();if(next.Length!=6 || next[0]!='s')return 4;}
                    if(allocations!=releases || !input.IsOpen)return 5;
                }
                {File output=File::OpenWrite({{path}});File* source=&output;
                    {var iterator=source.Lines().GetEnumerator();bool caught=false;try{iterator.MoveNext();}catch{caught=true;}
                        if(!caught || !output.IsOpen || allocations!=releases)return 6;
                    }
                    output.ClearError();output.Write("ok");output.Flush();if(!output.IsOpen)return 7;
                }return allocations-releases;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TakeLastConsumesOnlyRemainingFileBeforeYieldAndRetainsIndependentCountedTails(int optimization)
    {
        byte[] contents = [.. Encoding.ASCII.GetBytes("prefix\nskip\r\n"),
            0, (byte)'A', 255, 13, (byte)'X', 13, 10, (byte)'z', 13];
        Run(contents, path => Counters + $$$"""
            int main()
            {
                {
                    File input = File::OpenRead({{{path}}});
                    File* source = &input;
                    {
                        String prefix = source.Lines().AsEnumerable().First();
                        if (!prefix.Equals("prefix") || !input.IsOpen || input.EndOfFile) return 1;
                    }
                    if (allocations != releases || input.PeekByte() != 's') return 2;
                    int before = allocations;
                    failAllocation = true;
                    {
                        var empty = source.Lines().AsEnumerable().TakeLast(0).GetEnumerator();
                        if (empty.MoveNext() || empty.MoveNext()) return 3;
                    }
                    failAllocation = false;
                    if (allocations != before || !input.IsOpen || input.EndOfFile || input.PeekByte() != 's') return 4;

                    String keptFirst = new();
                    String keptLast = new();
                    var query = source.Lines().AsEnumerable().TakeLast(2);
                    before = allocations;
                    {
                        var tail = query.GetEnumerator();
                        if (allocations != before || input.EndOfFile || input.PeekByte() != 's') return 5;
                        if (!tail.MoveNext() || !input.IsOpen || !input.EndOfFile) return 6;
                        keptFirst = tail.Current;
                        if (keptFirst.Length != 5 || keptFirst[0] != '\0' || keptFirst[1] != 'A'
                            || (byte)keptFirst[2] != (byte)255 || keptFirst[3] != '\r' || keptFirst[4] != 'X') return 7;
                        {
                            String another = tail.Current;
                            if (another.Data == keptFirst.Data || !another.Equals(&keptFirst)) return 8;
                            another[1] = 'Q';
                            if (keptFirst[1] != 'A') return 9;
                        }
                        if (!tail.MoveNext()) return 10;
                        keptLast = tail.Current;
                        if (keptLast.Length != 2 || keptLast[0] != 'z' || keptLast[1] != '\r') return 11;
                        if (tail.MoveNext() || tail.MoveNext() || !input.IsOpen || !input.EndOfFile) return 12;
                        before = allocations;
                        if (query.Count() != 0 || allocations != before || !input.IsOpen || !input.EndOfFile) return 13;
                    }
                    input.Close();
                    if (keptFirst.Length != 5 || keptFirst[0] != '\0' || keptFirst[1] != 'A'
                        || (byte)keptFirst[2] != (byte)255 || keptFirst[3] != '\r' || keptFirst[4] != 'X') return 14;
                    if (keptLast.Length != 2 || keptLast[0] != 'z' || keptLast[1] != '\r') return 15;
                }
                return allocations - releases;
            }
            """, optimization);
    }

    [Theory]
    [InlineData("iterator.Current[0]='X';")]
    [InlineData("iterator.Current.Clear();")]
    public void CurrentCannotMutateTheIteratorsOwnedString(string invalid)
        => RunCompileFailure(path=>$$"""
            using std; using std::Text; using std::IO; using std::Queries; using std::Collections;int main(){File input=File::OpenRead({{path}});File* source=&input;
                var iterator=source.Lines().GetEnumerator();iterator.MoveNext();{{invalid}}return 0;}
            """);

    private static IEnumerable<SourceFile> Sources(string program)
    {
        var core=Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,"Std/core"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)));
        var io=new[]{"IO/File.gf","IO/FileLines.gf","IO/FileLineSequence.gf"}
            .Select(file=>new SourceFile(file,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/libc",file))));
        return core.Concat(io).Append(new SourceFile("lines.gf",program));
    }
    private static void RunCompileFailure(Func<string,string> program)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(program("\"unused\""))));
        Assert.Contains("immutable",error.Message,StringComparison.OrdinalIgnoreCase);
    }
    private static void Run(byte[] contents,Func<string,string> program,int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat_file_line_sequence_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string input=Path.Combine(directory,"input.bin"),ir=Path.Combine(directory,"lines.ll"),executable=Path.Combine(directory,"lines.exe");File.WriteAllBytes(input,contents);
            string literal="\""+input.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
            File.WriteAllText(ir,Compiler.Emit(Sources(program(literal)),options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"File line sequence exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}

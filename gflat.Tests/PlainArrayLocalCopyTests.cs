using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class PlainArrayLocalCopyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task LargeLocalSnapshotPreservesAllBytesAndCatchDeferCleanup(int optimization)
    {
        string ir = Emit(Large);
        Assert.Contains("i64 8195, i1 false", ir);
        Assert.DoesNotContain("load [8195 x i8]", ir);
        Assert.DoesNotContain("store [8195 x i8]", ir);
        Assert.Contains("call void @llvm.memcpy.inline.p0.p0.i64", ir);
        await Run(ir, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AddressableShapesRetainSideEffectsStartupProtectionAndClassDispatch(int optimization)
    {
        string ir = Emit(Shapes);
        Assert.Contains("@llvm.memcpy.inline.p0.p0.i64", ir);
        // Identifier, member, indexed subarray, namespace, protected and nested
        // array snapshots all have the same representation as their source.
        Assert.True(Regex.Matches(ir, @"call void @llvm\.memcpy\.inline\.p0\.p0\.i64\b").Count >= 6);
        await Run(ir, optimization);
    }

    [Fact]
    public void PureCopiesNeedNoRuntimeAndAuthoredIntrinsicDeclarationsAreReused()
    {
        const string pureCode = """
            int main(){int[3] source=[1,2,3];int[3] first=source;int[3] second=source;return first[0]+second[2];}
            """;
        string pureIr = Compiler.Emit([new SourceFile("plain-array-pure.gf", pureCode)],
            options: new() { NoStdLib = true, NoBuiltins = true });
        Assert.Contains("@llvm.memcpy.inline.p0.p0.i64", pureIr);
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "gflat$Memory$", "@malloc", "@free" })
            Assert.DoesNotContain(forbidden, pureIr);

        const string code = """
            int Declared() => __llvm_module<int>("declare void @llvm.memcpy.inline.p0.p0.i64(i8*, i8*, i64 immarg, i1 immarg) nounwind\ndefine i32 @entry() { ret i32 0 }", "entry");
            int main() {
                int[3] source=[1,2,3];int[3] first=source;int[3] second=source;
                return first[0]+second[2]+Declared();
            }
            """;
        string ir = Compiler.Emit([new SourceFile("plain-array-declaration.gf", code)],
            options: new() { NoStdLib = true, NoBuiltins = true });
        Assert.Single(Regex.Matches(ir, @"(?m)^declare void @llvm\.memcpy\.inline\.p0\.p0\.i64\("));
        Assert.Equal(2, Regex.Matches(ir, @"call void @llvm\.memcpy\.inline\.p0\.p0\.i64\b").Count);
    }

    private static string Emit(string code)
        => Compiler.Emit([new SourceFile("plain-array-copy.gf", code)],
            options: new() { NoBuiltins = true, InternalizeFunctions = true });

    private static async Task Run(string ir, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-plain-array-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "main.ll"), output = Path.Combine(directory, "main.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(output, []);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private const string Large =
    """
    using std;
    int cleaned=0;int deferred=0;
    struct Guard {~Guard(){cleaned++;}}
    int Exercise(bool fail) {
        Guard guard=new();defer{deferred++;}
        char[8195] source;
        for(nuint i=0;i<8195;i++)source[i]=(char)((i*17+13)%256);
        char[8195] snapshot=source;
        for(nuint i=0;i<8195;i++)if(snapshot[i]!=(char)((i*17+13)%256))return 1;
        source[0]='X';source[8194]='Y';
        if(snapshot[0]!=(char)13||snapshot[8194]!=(char)((8194*17+13)%256))return 2;
        if(fail)throw new Exception("after copy",73);
        return 0;
    }
    int main(){
        if(Exercise(false)!=0||cleaned!=1||deferred!=1)return 3;
        bool caught=false;
        try{if(Exercise(true)!=0)return 4;}
        catch(ref Exception error){if(error.GetCode()!=73)return 5;caught=true;}
        if(!caught||cleaned!=2||deferred!=2)return 6;
        bool innerCaught=false;
        try {
            try{throw new Exception("outer",31);}
            catch(ref Exception outer) {
                if(outer.GetCode()!=31)return 7;
                Guard guard=new();defer{deferred++;}
                char[3] source=['a','b','c'];char[3] snapshot=source;
                source[1]='X';if(snapshot[1]!='b')return 8;
                throw new Exception("inner",37);
            }
        }
        catch(ref Exception inner){if(inner.GetCode()!=37)return 9;innerCaught=true;}
        if(!innerCaught||cleaned!=3||deferred!=3)return 10;
        return 0;
    }
    """;

    private const string Shapes =
    """
    struct Empty{}
    class C{public virtual int Get()=>21;}
    struct ClassStorage{public C[2] values;}
    int calls=0;int initialized=0;
    int Next(){calls++;return 1;}
    int[2] Make(){initialized++;return [31,47];}
    namespace State {public int[2] values=Make();}
    struct Storage {
        public int[2] items;
        public Storage(){items=[7,9];}
    }
    int main(){
        Storage storage=new();int[2] member=storage.items;
        storage.items[0]=99;if(member[0]!=7||member[1]!=9)return 1;
        int[2][2] source=[[11,13],[17,19]];
        int[2] indexed=source[Next()];
        if(calls!=1||indexed[0]!=17||indexed[1]!=19)return 2;
        source[1][1]=88;if(indexed[1]!=19)return 3;
        int[2] namespaceCopy=State::values;
        if(initialized!=1||namespaceCopy[0]!=31||namespaceCopy[1]!=47)return 4;
        int* namespaceCopyAddress=State::values;namespaceCopyAddress[0]=77;if(namespaceCopy[0]!=31)return 5;
        readonly(int)[2] protectedValues=[23,29];
        int[2] qualified=protectedValues;
        if(qualified[0]!=23||qualified[1]!=29)return 6;
        int[2][2] nested=source;source[0][0]=66;
        if(nested[0][0]!=11||nested[1][1]!=88)return 7;
        ClassStorage classes=default(ClassStorage);C[2] objects=classes.values;
        C* first=&objects[0];C* second=&objects[1];
        if(first.Get()!=21||second.Get()!=21)return 8;
        Empty[2][2] emptySource=default(Empty[2][2]);int before=calls;
        Empty[2] emptySnapshot=emptySource[Next()];
        if(calls!=before+1||(nuint)sizeof(Empty[2])!=(nuint)0)return 9;
        return 0;
    }
    """;
}

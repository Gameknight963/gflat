using System.Collections;

namespace gflat.Tests;

public sealed class StandardBitSetOverlapTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WordIntersectionsMatchBitArrayWithoutCopyingAllocatingOrMutating(int optimization)
    {
        int[] lengths=[0,1,31,32,33,63,64,65,97];
        var expected=new List<bool>();
        foreach(int length in lengths)
        for(int mode=0;mode<6;mode++){
            var left=new BitArray(length);var right=new BitArray(length);
            for(int i=0;i<length;i++){
                left[i]=mode switch{0=>false,1=>i%2==0,2=>i%3==0,3=>i+1==length,4=>i is 31 or 32 or 64,_=>true};
                right[i]=mode switch{0=>false,1=>i%2==1,2=>i%5==0,3=>i+1==length,4=>i is 32 or 63,_=>i%7==1};
            }
            expected.Add(((BitArray)left.Clone()).And(right).Cast<bool>().Any(v=>v));
        }
        Run($$"""
            using std; using std::Collections;
            extern void*? malloc(nuint size);extern void free(void* value);
            bool deny=false;int attempts=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){if(deny){attempts++;return null;}return malloc(size);}
                public replace void Free(void* value) nothrow{free(value);}
            }
            bool Left(nuint i,nuint length,nuint mode) nothrow{
                if(mode==0)return false;if(mode==1)return i%2==0;if(mode==2)return i%3==0;
                if(mode==3)return i+1==length;if(mode==4)return i==31||i==32||i==64;return true;
            }
            bool Right(nuint i,nuint length,nuint mode) nothrow{
                if(mode==0)return false;if(mode==1)return i%2==1;if(mode==2)return i%5==0;
                if(mode==3)return i+1==length;if(mode==4)return i==32||i==63;return i%7==1;
            }
            bool Observe(immutable(BitSet)* left,immutable(BitSet)* right)=>left.Overlaps(right);
            int main(){
                const nuint[9] lengths=[(nuint)0,(nuint)1,(nuint)31,(nuint)32,(nuint)33,(nuint)63,(nuint)64,(nuint)65,(nuint)97];
                const bool[{{expected.Count}}] expected=[{{string.Join(",",expected.Select(v=>v?"true":"false"))}}];
                nuint sample=0;
                for(nuint size=0;size<9;size++)for(nuint mode=0;mode<6;mode++){
                    nuint length=lengths[size];BitSet left=new(length);BitSet right=new(length);
                    bool leftAny=false;bool rightAny=false;
                    for(nuint i=0;i<length;i++){
                        bool a=Left(i,length,mode);bool b=Right(i,length,mode);
                        left[i]=a;right[i]=b;leftAny=leftAny||a;rightAny=rightAny||b;
                    }
                    immutable(BitSet)* deep=&left;readonly(BitSet)* shallow=&left;
                    deny=true;
                    if(Observe(&left,&right)!=expected[sample]||Observe(&right,&left)!=expected[sample])return 1;
                    if(deep.Overlaps(&right)!=expected[sample]||shallow.Overlaps(&right)!=expected[sample])return 2;
                    if(left.Overlaps(&left)!=leftAny||right.Overlaps(&right)!=rightAny)return 3;
                    if(attempts!=0||left.Length!=length||right.Length!=length)return 4;
                    for(nuint i=0;i<length;i++)if(left[i]!=Left(i,length,mode)||right[i]!=Right(i,length,mode))return 5;
                    deny=false;sample++;
                }
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShrinkRegrowthClearsPaddingAndLengthMismatchesLeaveInputsUnchanged(int optimization)
        => Run("""
            using std; using std::Collections;
            int main(){
                BitSet left=new(65);BitSet right=new(65);left[64]=true;right[64]=true;
                if(!left.Overlaps(&right))return 1;
                left.Resize(33);right.Resize(33);if(left.Overlaps(&right))return 2;
                left.Resize(65);right.Resize(65);if(left.Overlaps(&right)||left[64]||right[64])return 3;
                left[32]=true;right[32]=true;if(!left.Overlaps(&right))return 4;
                left.Resize(32);right.Resize(32);if(left.Overlaps(&right))return 5;
                left.Resize(33);right.Resize(33);left[31]=true;right[32]=true;
                if(left.Overlaps(&right))return 6;left[32]=true;if(!left.Overlaps(&right))return 7;
                left.Resize(0);right.Resize(0);if(left.Overlaps(&right)||left.Overlaps(&left))return 8;
                const nuint[8] lengths=[(nuint)0,(nuint)1,(nuint)31,(nuint)32,(nuint)32,(nuint)33,(nuint)64,(nuint)65];int caught=0;
                for(nuint pair=0;pair<8;pair+=2){
                    BitSet first=new(lengths[pair]);BitSet second=new(lengths[pair+1]);
                    first.SetAll(true);second.SetAll(true);
                    try{first.Overlaps(&second);}catch(Exception* error){caught++;}
                    try{second.Overlaps(&first);}catch(Exception* error){caught++;}
                    if(first.Length!=lengths[pair]||second.Length!=lengths[pair+1])return 9;
                    for(nuint i=0;i<first.Length;i++)if(!first[i])return 10;
                    for(nuint i=0;i<second.Length;i++)if(!second[i])return 11;
                }
                if(caught!=8)return 12;return 0;
            }
            """,optimization);

    private static void Run(string program,int optimization)
    {
        string[] names=["Collections/IEnumerable.gf","Collections/Enumerable.gf","Collections/BitSet.gf", "IEquatable.gf", "IHashable.gf", "Hashing.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("bitset-overlaps.gf",program));
        string directory=Path.Combine(Path.GetTempPath(),"gflat-bitset-overlap-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"BitSet overlap exit{result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}

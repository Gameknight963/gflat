using System.Collections;
using System.Text;

namespace gflat.Tests;

public sealed class StandardBitSetOccupancyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallLengthAndSingleHoleRespectWordPaddingAndReadonlyNothrowQueries(int optimization)
        => Run("""
            using std; using std::Queries; using std::Collections;
            bool Any(immutable(BitSet)* value) nothrow=>value.HasAnySet();
            bool All(immutable(BitSet)* value) nothrow=>value.HasAllSet();
            int main(){BitSet bits=new();
                for(nuint length=0;length<131;length++){
                    bits.Resize(length);bits.Clear();
                    if(Any(&bits) || All(&bits)!=(length==0))return 1;
                    bits.SetAll(true);if(Any(&bits)!=(length!=0) || !All(&bits))return 2;
                    for(nuint hole=0;hole<length;hole++){
                        bits[hole]=false;
                        if(All(&bits) || Any(&bits)!=(length>1))return 3;
                        bits[hole]=true;if(!All(&bits))return 4;
                    }
                    if(length!=0){
                        bits.Resize(length-1);if(!All(&bits))return 5;
                        bits.Resize(length);if(All(&bits) || Any(&bits)!=(length>1))return 6;
                    }
                }
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RandomResizeRangesAndSetOperationsMatchBitArrayOccupancy(int optimization)
    {
        var random=new Random(109833);var reference=new BitArray(0);
        var source=new StringBuilder("using std; using std::Queries; using std::Collections;int main(){BitSet bits=new();\n");
        for(int step=0;step<180;step++)
        {
            int operation=random.Next(8);
            if(step%12==0 || operation==0){int length=random.Next(180);reference.Length=length;source.AppendLine($"bits.Resize({length});");}
            else if(operation==1){bool value=random.Next(2)==0;reference.SetAll(value);source.AppendLine($"bits.SetAll({(value?"true":"false")});");}
            else if(operation==2 || operation==3){
                int start=random.Next(reference.Length+1),count=random.Next(reference.Length-start+1);bool value=random.Next(2)==0;
                for(int i=start;i<start+count;i++)reference[i]=operation==2?value:!reference[i];
                source.AppendLine(operation==2?$"bits.SetRange({start},{count},{(value?"true":"false")});":$"bits.FlipRange({start},{count});");
            }
            else{
                var other=new BitArray(reference.Length);source.AppendLine($"{{BitSet other=new({reference.Length});");
                int stride=random.Next(1,8),offset=random.Next(stride);
                for(int i=offset;i<other.Length;i+=stride)other[i]=true;
                source.AppendLine($"for(nuint i={offset};i<other.Length;i+={stride})other[i]=true;");
                switch(operation){
                    case 4:reference.Or(other);source.AppendLine("bits.UnionWith(&other);");break;
                    case 5:reference.And(other);source.AppendLine("bits.IntersectWith(&other);");break;
                    case 6:reference.Xor(other);source.AppendLine("bits.SymmetricExceptWith(&other);");break;
                    default:reference.And(other.Not());source.AppendLine("bits.ExceptWith(&other);");break;
                }
                source.AppendLine("}");
            }
            bool any=reference.Cast<bool>().Any(v=>v),all=reference.Cast<bool>().All(v=>v);
            source.AppendLine($"if(bits.HasAnySet()!={(any?"true":"false")} || bits.HasAllSet()!={(all?"true":"false")})return 1;");
        }
        source.AppendLine("return 0;}");Run(source.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OccupancyQueriesNeedNoAllocationAndRemainCorrectAcrossMovesCloneAndFailedResize(int optimization)
        => Run("""
            using std; using std::Queries; using std::Collections;
            int allocations=0;int releases=0;bool fail=false;
            private extern void*? malloc(nuint bytes);private extern void free(void* storage);
            namespace Memory{
                public replace void*? Allocate(nuint bytes){if(fail)return null;void*? result=malloc(bytes);if(result!=null)allocations++;return result;}
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            int main(){ {
                BitSet original=new(33);original.SetAll(true);BitSet clone=original.Clone();
                original[32]=false;if(!original.HasAnySet() || original.HasAllSet() || !clone.HasAllSet())return 1;
                BitSet moved=move clone;int before=allocations;
                fail=true;
                if(!moved.HasAllSet() || !moved.HasAnySet() || original.HasAllSet())return 2;
                bool caught=false;try{moved.Resize(65);}catch{caught=true;}
                if(!caught || moved.Length!=33 || !moved.HasAllSet() || !moved.HasAnySet() || allocations!=before)return 3;
                moved.Clear();if(moved.HasAnySet() || moved.HasAllSet())return 4;
                moved.Resize(0);if(moved.HasAnySet() || !moved.HasAllSet())return 5;
                fail=false;
            }
            if(allocations!=releases)return 6;return 0;}
            """,optimization);

    private static void Run(string program,int optimization)
    {
        var sources=new[]{"Collections/IEnumerable.gf", "Queries/Enumerable.gf","Collections/BitSet.gf", "IEquatable.gf", "IHashable.gf", "Hashing.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("bitset-occupancy.gf",program));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_bitset_occupancy_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"bits.ll"),executable=Path.Combine(directory,"bits.exe");File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compilation=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compilation.ExitCode==0,compilation.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"BitSet occupancy exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}

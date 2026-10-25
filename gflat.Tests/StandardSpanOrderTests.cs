using System.Text;

namespace gflat.Tests;

public sealed class StandardSpanOrderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExtremaIndicesAndLexicographicOrderMatchDotNetIncludingTiesAndPrefixes(int optimization)
    {
        var random=new Random(89071);
        var source=new StringBuilder("""
            using std; using std::Numerics; using std::Queries; using std::Collections;extern int puts(immutable(char*) text);int calls=0;
            int Compare(readonly(int)* a,readonly(int)* b){calls++;if(*a<*b)return -1;if(*a>*b)return 1;return 0;}
            int main(){
            """);
        for(int scenario=0;scenario<80;scenario++){
            int[] first=Enumerable.Range(0,random.Next(32)).Select(_=>random.Next(-3,4)).ToArray();
            int[] second=Enumerable.Range(0,random.Next(32)).Select(_=>random.Next(-3,4)).ToArray();
            if(scenario==0)first=[];
            if(scenario%4==0)second=[..first];
            else if(scenario%4==1)second=[..first,1,2];
            else if(scenario%4==2)second=first.Take(random.Next(first.Length+1)).ToArray();
            int min=first.Length==0?0:first.Select((value,index)=>(value,index)).MinBy(item=>item.value).index;
            int max=first.Length==0?0:first.Select((value,index)=>(value,index)).MaxBy(item=>item.value).index;
            int order=Math.Sign(((ReadOnlySpan<int>)first).SequenceCompareTo(second));
            int shared=Math.Min(first.Length,second.Length),compared=0;
            while(compared<shared){int index=compared++;if(first[index]!=second[index])break;}
            source.AppendLine($"{{int[{Math.Max(1,first.Length)}] first=["+string.Join(",",first.Length==0?[0]:first)+"];");
            source.AppendLine($"int[{Math.Max(1,second.Length)}] second=["+string.Join(",",second.Length==0?[0]:second)+"];");
            source.AppendLine($"Span<readonly(int)> a=new((readonly(int)*)first,{first.Length});Span<readonly(int)> b=new((readonly(int)*)second,{second.Length});");
            Check($"MinIndex<int>(a,Compare)=={min} && calls=={Math.Max(0,first.Length-1)}",$"minimum {scenario}");
            Check($"MaxIndex<int>(a,Compare)=={max} && calls=={Math.Max(0,first.Length-1)}",$"maximum {scenario}");
            Check($"SequenceCompare<int>(a,b,Compare)=={order} && calls=={compared}",$"order {scenario}");
            source.AppendLine("}");
        }
        source.AppendLine("return 0;}");Run(source.ToString(),optimization);
        void Check(string condition,string label)=>source.AppendLine($"calls=0;if(!({condition})){{puts(\"{label}\");return 1;}}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersAreBorrowedWithoutAllocationAndRemainIntactWhenComparisonThrows(int optimization)
        => Run("""
            using std; using std::Numerics; using std::Queries; using std::Collections;
            private extern void*? malloc(nuint bytes);private extern void free(void* storage);
            int allocations=0;int releases=0;int made=0;int dead=0;int calls=0;int failAt=0;
            namespace Memory{
                public replace void*? Allocate(nuint bytes){void*? result=malloc(bytes);if(result!=null)allocations++;return result;}
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            struct Owner{public int Value;public String Token;public Owner(int value){Value=value;Token=s"owner";made++;}~Owner(){dead++;}}
            int Compare(immutable(Owner)* a,immutable(Owner)* b){calls++;if(calls==failAt)throw new Exception("compare");if(a.Value<b.Value)return -17;if(a.Value>b.Value)return 23;return 0;}
            int main(){ {
                Vector<Owner> values=new();values.Add(new Owner(3));values.Add(new Owner(1));values.Add(new Owner(1));values.Add(new Owner(4));values.Add(new Owner(4));
                Span<immutable(Owner)> source=values.AsImmutable();int before=allocations;int freed=releases;
                calls=0;if(MinIndex<Owner>(source,Compare)!=1 || calls!=4)return 1;
                calls=0;if(MaxIndex<Owner>(source,Compare)!=3 || calls!=4)return 2;
                calls=0;if(SequenceCompare<Owner>(source,source,Compare)!=0 || calls!=5)return 3;
                calls=0;if(SequenceCompare<Owner>(source.Slice(1,1),source.Slice(3,1),Compare)!=-17 || calls!=1)return 4;
                calls=0;if(SequenceCompare<Owner>(source.Slice(3,1),source.Slice(1,1),Compare)!=23 || calls!=1)return 5;
                calls=0;if(SequenceCompare<Owner>(source.Slice(0,2),source,Compare)!=-1 || calls!=2)return 6;
                calls=0;if(SequenceCompare<Owner>(source,source.Slice(0,2),Compare)!=1 || calls!=2)return 7;
                if(allocations!=before || releases!=freed || made!=5 || dead!=0)return 8;
                for(int failure=1;failure<=4;failure++){
                    failAt=failure;calls=0;bool caught=false;try{MinIndex<Owner>(source,Compare);}catch{caught=true;}if(!caught)return 9;
                    calls=0;caught=false;try{MaxIndex<Owner>(source,Compare);}catch{caught=true;}if(!caught)return 10;
                    calls=0;caught=false;try{SequenceCompare<Owner>(source,source,Compare);}catch{caught=true;}if(!caught)return 11;
                    if(made!=5 || dead!=0 || allocations-releases!=before-freed)return 12;
                    if(source[0].Value!=3 || source[1].Value!=1 || source[2].Value!=1 || source[3].Value!=4 || source[4].Value!=4)return 13;
                    for(nuint i=0;i<source.Length;i++)if(source[i].Token.Length!=5)return 14;
                }
                failAt=0;Span<immutable(Owner)> empty=new();calls=0;
                if(MinIndex<Owner>(empty,Compare)!=0 || MaxIndex<Owner>(empty,Compare)!=0 || SequenceCompare<Owner>(empty,source,Compare)!=-1 || calls!=0)return 15;
            }if(made!=dead || allocations!=releases)return 16;return 0;}
            """,optimization);

    private static void Run(string program,int optimization)
    {
        string[] files=["Collections/IEnumerable.gf","Queries/Enumerable.gf","Collections/span_algorithms.gf","Collections/Vector.gf","Span.gf","String.gf", "ByteSearch.gf",
            "IStringConvertable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing.gf"];
        var sources=files.Select(file=>new SourceFile(file,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",file))))
            .Append(new SourceFile("order.gf",program));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_span_order_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"order.ll"),executable=Path.Combine(directory,"order.exe");File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"Span order exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}

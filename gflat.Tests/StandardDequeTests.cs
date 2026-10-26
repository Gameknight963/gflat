using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardDequeTests
{
    private const string Owner="""
        using std; using std::Memory; using std::Numerics; using std::Queries; using std::Collections;
        extern int puts(immutable(char*) text);
        int made=0;int dead=0;
        struct Owned{
            public int Value;public String Token;
            public Owned(int value){Value=value;Token=s"owner";made++;}
            ~Owned(){dead++;}
        }
        bool Matches(Deque<Owned>* values,readonly(int)* expected,nuint count){
            if(values.Count!=count || values.IsEmpty!=(count==0) || values.Capacity<count)return false;
            immutable(Deque<Owned>)* read=values;
            var iterator=values.GetEnumerator();
            for(nuint i=0;i<count;i++){
                if(!iterator.MoveNext() || iterator.Current.Value!=expected[i] || iterator.Current.Token.Length!=5)return false;
                if((*read)[i].Value!=expected[i])return false;
            }
            if(iterator.MoveNext())return false;
            if(count!=0 && (read.Front.Value!=expected[0] || read.Back.Value!=expected[count-1]))return false;
            return true;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BothEndTraceMatchesLinkedListAcrossWraparoundAndGrowth(int optimization)
    {
        var reference=new LinkedList<int>();var random=new Random(21673);int made=0;
        var source=new StringBuilder(Owner).AppendLine("int main(){{Deque<Owned> values=new();values.Reserve(5);");
        for(int value=0;value<5;value++)Add(value,false);
        for(int i=0;i<3;i++)Remove(true,"forced initial removal");
        for(int value=5;value<8;value++)Add(value,false);
        Add(2,true);Snapshot("forced wrapped growth");
        for(int step=0;step<360;step++){
            int operation=random.Next(100);
            if(reference.Count==0 || operation<48 && reference.Count<70)Add(random.Next(-30,31),random.Next(2)==0);
            else if(operation<82)Remove(random.Next(2)==0,$"remove {step}");
            else if(operation<90){
                int position=random.Next(reference.Count),value=random.Next(-100,101);
                var node=reference.First!;for(int i=0;i<position;i++)node=node.Next!;node.Value=value;
                source.AppendLine($"values[{position}].Value={value};");
            }
            else if(operation<97)source.AppendLine($"values.Reserve({reference.Count+random.Next(1,30)});");
            else{reference.Clear();source.AppendLine("values.Clear();");}
            Snapshot($"state {step}");
        }
        source.AppendLine("}return made-dead;}");Run(source.ToString(),optimization);

        void Add(int value,bool first){if(first)reference.AddFirst(value);else reference.AddLast(value);source.AppendLine($"values.{(first?"AddFirst":"AddLast")}(new Owned({value}));");made++;}
        void Remove(bool first,string label){int expected=first?reference.First!.Value:reference.Last!.Value;if(first)reference.RemoveFirst();else reference.RemoveLast();
            source.AppendLine($"{{Owned removed=values.{(first?"RemoveFirst":"RemoveLast")}();");Check($"removed.Value=={expected} && removed.Token.Length==5",label);source.AppendLine("}");}
        void Snapshot(string label){int[] values=reference.ToArray();source.AppendLine($"{{int[{Math.Max(1,values.Length)}] expected=["+string.Join(",",values.Length==0?[0]:values)+"];");
            Check($"Matches(&values,(readonly(int)*)expected,{values.Length})",label);source.AppendLine("}");Check($"made=={made} && dead=={made-reference.Count}",label+" owners");}
        void Check(string condition,string label)=>source.AppendLine($"if(!({condition})){{puts(\"{label}\");return 1;}}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedGrowthAtEitherEndPreservesWrappedOwnersAndMovesCleanly(int optimization)
        => Run(Owner+"""
            private extern void*? malloc(nuint bytes);private extern void free(void* storage);
            int allocations=0;int releases=0;bool fail=false;
            namespace Memory{
                public replace void*? Allocate(nuint bytes){if(fail)return null;void*? result=malloc(bytes);if(result!=null)allocations++;return result;}
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            int main(){{
                Deque<Owned> values=new();values.Reserve(2);values.AddLast(new Owned(1));values.AddLast(new Owned(2));
                values.RemoveFirst();values.AddLast(new Owned(3));Owned* front=&values.Front;Owned* back=&values.Back;
                Owned incoming=new Owned(4);fail=true;bool caught=false;try{values.AddFirst(move incoming);}catch{caught=true;}fail=false;
                if(!caught || values.Count!=2 || &values.Front!=front || &values.Back!=back || values.Front.Value!=2 || values.Back.Value!=3 || made-dead!=2)return 1;
                Owned last=new Owned(5);fail=true;caught=false;try{values.AddLast(move last);}catch{caught=true;}fail=false;
                if(!caught || values.Count!=2 || &values.Front!=front || &values.Back!=back || made-dead!=2)return 2;
                fail=true;caught=false;try{values.Reserve(99);}catch{caught=true;}fail=false;
                if(!caught || values.Capacity!=2 || &values.Front!=front || &values.Back!=back)return 3;
                values.AddFirst(new Owned(6));values.AddLast(new Owned(7));
                int[4] expected=[6,2,3,7];if(!Matches(&values,(readonly(int)*)expected,4))return 4;
                values.RemoveFirst();values.RemoveLast();
                Deque<Owned> moved=move values;values=new Deque<Owned>();values.AddLast(new Owned(8));
                if(moved.Front.Value!=2 || moved.Back.Value!=3 || values.Front.Value!=8 || made-dead!=3)return 5;
                nuint capacity=moved.Capacity;moved.Clear();moved.Clear();if(!moved.IsEmpty || moved.Capacity!=capacity || made-dead!=1)return 6;
            }if(made!=dead || allocations!=releases)return 7;return 0;}
            """,optimization);

    [Fact]
    public void EmptyIndexAndIteratorErrorsDoNotCorruptTheDeque()
        => Run("""
            using std; using std::Memory; using std::Numerics; using std::Queries; using std::Collections;
            int main(){Deque<int> values=new();int caught=0;
                try{values.RemoveFirst();}catch{caught++;}try{values.RemoveLast();}catch{caught++;}
                try{values.Front;}catch{caught++;}try{values.Back;}catch{caught++;}try{values[0];}catch{caught++;}
                var empty=values.GetEnumerator();try{empty.Current;}catch{caught++;}if(empty.MoveNext() || empty.MoveNext())return 1;
                values.AddFirst(1);values.AddFirst(2);values.AddLast(3);immutable(Deque<int>)* read=&values;
                if(read.Front!=2 || read.Back!=3 || (*read)[1]!=1)return 2;
                var iterator=values.GetEnumerator();int sum=0;while(iterator.MoveNext())sum+=iterator.Current;
                try{iterator.Current;}catch{caught++;}try{values[3];}catch{caught++;}
                try{values.Reserve((nuint)0-(nuint)1);}catch{caught++;}
                if(caught!=9 || sum!=6 || values.Count!=3)return 3;
                values.Clear();values.AddLast(9);if(values.RemoveLast()!=9 || !values.IsEmpty)return 4;
                return 0;
            }
            """,0);

    [Theory]
    [InlineData("read.Front.Value=9;")]
    [InlineData("read.Back.Value=9;")]
    [InlineData("(*read)[0].Value=9;")]
    [InlineData("Deque<Owned> copied=values;")]
    public void ReadonlyBorrowsCannotMutateOwnersAndDequeCannotImplicitlyCopy(string invalid)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(Owner+"""
            int main(){Deque<Owned> values=new();values.AddLast(new Owned(1));immutable(Deque<Owned>)* read=&values;
            """+invalid+"return 0;}")));
        Assert.True(error.Message.Contains("readonly",StringComparison.OrdinalIgnoreCase)||error.Message.Contains("copy",StringComparison.OrdinalIgnoreCase),error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedElementsSurviveBothEndGrowthAndReleaseOnRemoval(int optimization)
        => Run("""
            using std; using std::Memory; using std::Numerics; using std::Queries; using std::Collections;
            class Node{public int Value;public Node^? Next;public Node(int value){Value=value;}}
            void Fill(Deque<Node^>* values){for(int i=0;i<32;i++){Node^ value=new^ Node(i);value.Next=value;if(i%2==0)values.AddFirst(value);else values.AddLast(value);GC::Collect();}}
            int main(){Deque<Node^> values=new();Fill(&values);values.Reserve(1000);GC::Collect();
                if(TracingCollector::ManagedCount!=32 || values.Count!=32)return 1;
                for(nuint i=0;i<16;i++)if(values[i].Value!=30-(int)i*2 || values[i+16].Value!=(int)i*2+1)return 2;
                for(int i=0;i<8;i++){values.RemoveFirst();values.RemoveLast();}
                GC::Collect();if(TracingCollector::ManagedCount!=16)return 3;
                for(nuint i=0;i<8;i++)if(values[i].Value!=14-(int)i*2 || values[i+8].Value!=(int)i*2+1)return 4;
                values.Clear();GC::Collect();return (int)TracingCollector::ManagedCount;
            }
            """,optimization,managed:true);

    private static IEnumerable<SourceFile> Sources(string program,bool managed=false)
    {
        string[] files=["Collections/IEnumerable.gf","Queries/Enumerable.gf","Collections/Deque.gf","Memory/Span.gf","Collections/Vector.gf","String.gf", "Text/ByteSearch.gf",
            "IStringConvertable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf"];
        IEnumerable<SourceFile> sources=files.Select(file=>new SourceFile(file,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",file))));
        if(managed)sources=sources.Concat(new[]{"GC.gf","TracingCollector.gf"}.Select(file=>new SourceFile(file,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/libc",file)))));
        return sources.Append(new SourceFile("deque.gf",program));
    }
    private static void Run(string program,int optimization,bool managed=false)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat_deque_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"deque.ll"),executable=Path.Combine(directory,"deque.exe");File.WriteAllText(ir,Compiler.Emit(Sources(program,managed),options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"Deque exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}

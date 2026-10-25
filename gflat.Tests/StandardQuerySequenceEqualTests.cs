namespace gflat.Tests;

public sealed class StandardQuerySequenceEqualTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DifferentQuerySourcesMatchIndependentLinqOraclesWithoutAllocation(int optimization)
    {
        var random=new Random(730214);var leftData=new List<int>();var rightData=new List<int>();var manifest=new List<int>();
        for(int sample=0;sample<64;sample++){
            int[] left=Enumerable.Range(0,sample%16).Select(_=>random.Next(-20,21)).ToArray();
            int[] right=left.ToArray();
            if(sample%6==1)right=[..right,random.Next(-20,21)];
            else if(sample%6==2&&right.Length!=0)right=right[..^1];
            else if(sample%6>=3&&right.Length!=0){int at=0;if(sample%6==4)at=right.Length/2;if(sample%6==5)at=right.Length-1;right[at]+=2;}
            manifest.AddRange([left.Length,right.Length,left.SequenceEqual(right)?1:0,
                left.Select(value=>value&1).SequenceEqual(right.Select(value=>value&1))?1:0,
                left.Where(value=>value>=0).SequenceEqual(right.Where(value=>value>=0))?1:0]);
            leftData.AddRange(left);rightData.AddRange(right);
        }
        // Spare terminal elements keep even the last empty view's pointer backed.
        leftData.Add(0);rightData.Add(0);
        Run($$"""
            using std; using std::Collections;int allocations=0;
            namespace Memory{public replace void*? Allocate(nuint count){allocations++;return null;}public replace void Free(void* storage)nothrow{} }
            int Identity(int value)=>value;bool Keep(int value)=>value>=0;
            bool Equal(immutable(int)* left,immutable(int)* right)=>*left==*right;
            bool Parity(immutable(int)* left,immutable(int)* right)=>(*left&1)==(*right&1);
            struct Plain{public int Value;public Plain(int value){Value=value;} }
            bool EqualPlain(immutable(Plain)* left,immutable(Plain)* right)=>left.Value==right.Value;
            int main(){
                int[{{leftData.Count}}] leftData=[{{string.Join(',',leftData)}}];int[{{rightData.Count}}] rightData=[{{string.Join(',',rightData)}}];
                int[{{manifest.Count}}] manifest=[{{string.Join(',',manifest)}}];nuint leftOffset=0;nuint rightOffset=0;
                for(nuint sample=0;sample<64;sample++){
                    nuint row=sample*5;Span<int> left=new(&leftData[leftOffset],(nuint)manifest[row]);Span<int> right=new(&rightData[rightOffset],(nuint)manifest[row+1]);
                    var first=left.AsEnumerable();var second=right.AsEnumerable().Select(&Identity);
                    if(first.SequenceEqual(second)!=(manifest[row+2]!=0)||first.SequenceEqual(second,&Equal)!=(manifest[row+2]!=0))return 1;
                    if(first.SequenceEqual(second,&Parity)!=(manifest[row+3]!=0))return 2;
                    if(first.Where(&Keep).SequenceEqual(second.Where(&Keep))!=(manifest[row+4]!=0))return 3;
                    if(left.AsImmutable().AsEnumerable().SequenceEqual(right.AsImmutable().AsEnumerable(),&Equal)!=(manifest[row+2]!=0))return 4;
                    if(std::Collections::SequenceEqual<int>(left.AsImmutable(),right.AsImmutable(),&Equal)!=(manifest[row+2]!=0))return 5;
                    if(first.SequenceEqual(second)!=(manifest[row+2]!=0))return 6;
                    leftOffset+=left.Length;rightOffset+=right.Length;
                }
                Plain[1] one=[new Plain(7)];Plain[1] two=[new Plain(7)];Span<Plain> a=new(one,1);Span<Plain> b=new(two,1);
                if(!a.AsEnumerable().SequenceEqual(b.AsEnumerable(),&EqualPlain)||one[0].Value!=7||two[0].Value!=7)return 7;
                if(allocations!=0)return 8;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EnumerationOrderLengthMismatchAndPairMismatchHaveExactObservableEffects(int optimization)
        =>Run("""
            using std; using std::Collections;char[128] events;nuint used=0;int leftLength=0;int rightLength=0;bool mismatch=false;bool poison=false;
            void Event(char value){events[used]=value;used++;events[used]='\0';}
            void Reset(){used=0;events[0]='\0';}
            struct Source{
                private int side;public Source(int id){side=id;}
                public struct Iterator{
                    private int side;private int position;
                    public Iterator(int id){side=id;position=0;if(side==0)Event('L');else Event('R');}
                    public bool MoveNext(){if(side==0)Event('l');else Event('r');if(poison&&position==1)throw new Exception("suffix");int length=leftLength;if(side==1)length=rightLength;if(position==length)return false;position++;return true;}
                    public int Current{get{if(side==0)Event('a');else Event('b');if(side==1&&mismatch)return position+1;return position;}}
                    ~Iterator(){if(side==0)Event('X');else Event('Y');}
                }
                public Iterator GetEnumerator()=>new Iterator(side);
            }
            bool Equal(immutable(int)* left,immutable(int)* right){Event('c');return *left==*right;}
            int main(){
                var left=new Enumerable<int,Source,Source::Iterator>(new Source(0));var right=new Enumerable<int,Source,Source::Iterator>(new Source(1));
                Reset();if(!left.SequenceEqual(right,&Equal)||!CString::Equals(&events[0],"LRlrYX"))return 1;
                leftLength=2;rightLength=2;Reset();if(!left.SequenceEqual(right,&Equal)||!CString::Equals(&events[0],"LRlrabclrabclrYX"))return 2;
                leftLength=1;Reset();if(left.SequenceEqual(right,&Equal)||!CString::Equals(&events[0],"LRlrabclrYX"))return 3;
                leftLength=2;rightLength=1;Reset();if(left.SequenceEqual(right,&Equal)||!CString::Equals(&events[0],"LRlrabclrYX"))return 4;
                rightLength=2;mismatch=true;poison=true;Reset();if(left.SequenceEqual(right,&Equal)||!CString::Equals(&events[0],"LRlrabcYX"))return 5;
                mismatch=false;poison=false;leftLength=1;Reset();if(!left.SequenceEqual(left,&Equal)||!CString::Equals(&events[0],"LLllaacllXX"))return 6;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BothIteratorsAndOwningCurrentsCleanUpAcrossConstructionReadCopyAndComparisonFailures(int optimization)
        =>Run("""
            using std; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
            int allocations=0;int releases=0;int starts=0;int ends=0;int live=0;int mode=0;int calls=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){void*? value=malloc(count);if(value!=null)allocations++;return value;}
                public replace void Free(void* storage)nothrow{releases++;free(storage);}
            }
            struct Owner{
                public int Id;private String text;
                public Owner(int id){Id=id;text=s"owned";live++;}
                public copy Owner(immutable(Owner)* source){Id=source.Id;text=source.text;if(mode==6&&Id==12)throw new Exception("copy",76);live++;}
                ~Owner(){live--;}
            }
            struct Source{
                private int side;public Source(int id){side=id;}
                public struct Iterator{
                    private int side;private int position;private String token;
                    public Iterator(int id){side=id;position=0;token=s"iterator";if(mode==1&&side==1)throw new Exception("right construction",71);starts++;}
                    public bool MoveNext(){if(position==1&&mode==2&&side==0)throw new Exception("left advance",72);if(position==1&&mode==3&&side==1)throw new Exception("right advance",73);if(position==3)return false;position++;return true;}
                    public Owner Current{get{if(mode==4&&side==1&&position==2)throw new Exception("right current",74);return new Owner(side*10+position);}}
                    ~Iterator(){ends++;}
                }
                public Iterator GetEnumerator()=>new Iterator(side);
            }
            bool Equal(immutable(Owner)* left,immutable(Owner)* right){calls++;if(mode==5&&calls==2)throw new Exception("compare",75);return left.Id%10==right.Id%10;}
            int main(){
                var left=new Enumerable<Owner,Source,Source::Iterator>(new Source(0));var right=new Enumerable<Owner,Source,Source::Iterator>(new Source(1));
                if(!left.SequenceEqual(right,&Equal)||live!=0||starts!=ends||allocations!=releases)return 1;
                for(int failure=1;failure<=5;failure++){
                    mode=failure;calls=0;bool caught=false;
                    try{left.SequenceEqual(right,&Equal);}catch(ref Exception error){if(error.GetCode()!=70+failure)return 2;caught=true;}
                    mode=0;if(!caught||live!=0||starts!=ends||allocations!=releases)return 3;
                }
                {
                    Vector<Owner> a=new();Vector<Owner> b=new();a.Add(new Owner(1));a.Add(new Owner(2));b.Add(new Owner(11));b.Add(new Owner(12));
                    int baseline=allocations-releases;mode=6;bool caught=false;
                    try{a.AsEnumerable().SequenceEqual(b.AsEnumerable(),&Equal);}catch(ref Exception error){if(error.GetCode()!=76)return 4;caught=true;}
                    mode=0;if(!caught||live!=4||allocations-releases!=baseline||a.Count!=2||b.Count!=2||a[0].Id!=1||a[1].Id!=2||b[0].Id!=11||b[1].Id!=12)return 5;
                    if(!a.AsEnumerable().SequenceEqual(b.AsEnumerable(),&Equal)||live!=4)return 6;
                }
                if(live!=0||starts!=ends||allocations!=releases)return 7;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedPairLocalsRemainRootedDuringComparerCollection(int optimization)
        =>Run("""
            using std; using std::Collections;int calls=0;class Item{public int Value;public Item(int value){Value=value;}}
            bool Equal(immutable(Item^)* left,immutable(Item^)* right){calls++;GC::Collect();return (*left).Value==(*right).Value;}
            Item^ Identity(Item^ value)=>value;
            int main(){
                Vector<Item^> a=new();Vector<Item^> b=new();for(int value=1;value<=4;value++){a.Add(new^ Item(value));b.Add(new^ Item(value));}
                if(!a.AsEnumerable().SequenceEqual(b.AsEnumerable().Select(&Identity),&Equal)||calls!=4)return 1;
                b[2].Value=99;calls=0;if(a.AsEnumerable().SequenceEqual(b.AsEnumerable(),&Equal)||calls!=3)return 2;
                GC::Collect();for(nuint i=0;i<4;i++)if(a[i].Value!=(int)i+1)return 3;
                if(b[2].Value!=99||b.Count!=4||a.Count!=4)return 4;return 0;
            }
            """,optimization,managed:true);

    private static void Run(string source,int optimization,bool managed=false)
    {
        string root=Path.Combine(AppContext.BaseDirectory,"Std");
        IEnumerable<string> paths=Directory.GetFiles(Path.Combine(root,"core"),"*.gf",SearchOption.AllDirectories);
        if(managed)paths=paths.Concat(new[]{Path.Combine(root,"libc/GC.gf"),Path.Combine(root,"libc/TracingCollector.gf")});
        var sources=paths.Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("query-sequence-equal.gf",source));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"SequenceEqual exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

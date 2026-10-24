namespace gflat.Tests;

public sealed class StandardRepeatedTests
{
    private const string Counters="""
        using std;extern void*? malloc(nuint count);extern void free(void* storage);
        int allocated=0;int released=0;int attempts=0;bool deny=false;
        namespace Memory {
            public replace void*? Allocate(nuint count) { attempts++;if(deny)return null;void*? p=malloc(count);if(p!=null)allocated++;return p; }
            public replace void Free(void* storage) nothrow { released++;free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ScalarCountsStickyEndIndependentCursorCopiesAndBoundedQueriesNeedNoAllocation(int optimization)
        =>Run(Counters+"""
            bool Seven(int value) => value==7;
            int Twice(int value) => value*2;
            int main() {
                int before=attempts;deny=true;
                for(nuint count=0;count<=8;count++) {
                    Repeated<int> values=new(7,count);immutable(Repeated<int>)* observed=&values;
                    if(values.Count!=count||observed.Count!=count||values.IsEmpty!=(count==0)||observed.IsEmpty!=(count==0))return 1;
                    var first=values.GetEnumerator();var second=values.GetEnumerator();
                    for(nuint index=0;index<count;index++) {
                        if(!first.MoveNext()||first.Current!=7||first.Current!=7)return 2;
                        if(!second.MoveNext()||second.Current!=7)return 3;
                        if(index==0) {
                            var copy=first;
                            for(nuint rest=1;rest<count;rest++)if(!copy.MoveNext()||copy.Current!=7)return 4;
                            if(copy.MoveNext()||copy.MoveNext())return 5;
                        }
                    }
                    if(first.MoveNext()||first.MoveNext()||second.MoveNext()||second.MoveNext())return 6;
                    nuint expected=count;if(expected>3)expected=3;
                    if(values.AsEnumerable().Where(&Seven).Select(&Twice).Take(3).Count()!=expected)return 7;
                    if(values.AsEnumerable().Take(0).Count()!=0)return 8;
                    nuint enumerated=0;foreach(int value in values){if(value!=7)return 9;enumerated++;}
                    if(enumerated!=count)return 10;
                }
                Repeated<int> huge=new(7,(nuint)0-1);
                if(huge.Count!=(nuint)0-1||huge.AsEnumerable().Take(3).Count()!=3)return 11;
                var iterator=huge.GetEnumerator();for(int index=0;index<4;index++)if(!iterator.MoveNext()||iterator.Current!=7)return 12;
                int leaf=10;Repeated<int*> pointers=new(&leaf,2);readonly(Repeated<int*>)* sourceView=&pointers;
                var pointerCursor=sourceView.GetEnumerator();readonly(Repeated<int*>::Enumerator)* cursorView=&pointerCursor;
                if(!pointerCursor.MoveNext())return 20;
                int* borrowed=cursorView.Current;*borrowed=11;
                var query=sourceView.AsEnumerable();int* again=query.First();*again=12;
                if(leaf!=12)return 21;
                if(attempts!=before)return 13;deny=false;
                {
                    Repeated<int> values=new(7,1);var first=values.GetEnumerator();bool caught=false;
                    try{first.Current;}catch(ref Exception error){caught=true;}if(!caught)return 14;
                    if(!first.MoveNext()||first.Current!=7||first.MoveNext()||first.MoveNext())return 15;
                    caught=false;try{first.Current;}catch(ref Exception error){caught=true;}if(!caught)return 16;
                    Repeated<int> empty=new(7,0);var zero=empty.GetEnumerator();
                    if(zero.MoveNext()||zero.MoveNext())return 17;
                    caught=false;try{zero.Current;}catch(ref Exception error){caught=true;}if(!caught)return 18;
                }
                if(allocated!=released)return 19;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningCapturesCopyOncePerIteratorAndCurrentFailureIsRetryableWithCompleteCleanup(int optimization)
        =>Run(Counters+"""
            int live=0;int copies=0;bool failCopy=false;
            struct Item {
                public int Id;public String Text;
                public Item(int id,String text){Id=id;Text=move text;live++;}
                public copy Item(immutable(Item)* source) {
                    Id=source.Id;Text=source.Text;copies++;
                    if(failCopy)throw new Exception("item copy",73);
                    live++;
                }
                ~Item(){live--;}
            }
            Repeated<Item>::Enumerator Detached() {
                Repeated<Item> values=new(new Item(9,s"detached"),2);
                return values.GetEnumerator();
            }
            int main() {
                {
                    Item original=new(7,s"a\0b");Repeated<Item> values=new(original,3);original.Text[0]='X';
                    int owners=live;int balance=allocated-released;int copied=copies;
                    {
                        var first=values.GetEnumerator();if(copies!=copied+1||live!=owners+1)return 1;
                        var second=values.GetEnumerator();if(copies!=copied+2||live!=owners+2)return 2;
                        values=new(new Item(8,s"replacement"),1);
                        if(!first.MoveNext()||!second.MoveNext())return 3;
                        Item a=first.Current;Item b=first.Current;
                        if(a.Id!=7||b.Id!=7||a.Text.Length!=3||a.Text[0]!='a'||a.Text[1]!='\0'||a.Text[2]!='b'||a.Text.Data==b.Text.Data)return 4;
                        a.Text[0]='Y';{Item other=second.Current;if(other.Text[0]!='a'||b.Text[0]!='a')return 5;}
                        var cursorCopy=first;
                        if(!cursorCopy.MoveNext()||!first.MoveNext())return 6;
                        int currentBalance=allocated-released;int currentOwners=live;
                        failCopy=true;bool caught=false;
                        try{first.Current;}catch(ref Exception error){if(error.GetCode()!=73)return 7;caught=true;}
                        failCopy=false;
                        if(!caught||allocated-released!=currentBalance||live!=currentOwners)return 8;
                        {Item retry=first.Current;if(retry.Id!=7||retry.Text[0]!='a')return 9;}
                        failCopy=true;caught=false;
                        try{var failed=first;}catch(ref Exception error){if(error.GetCode()!=73)return 10;caught=true;}
                        failCopy=false;if(!caught||allocated-released!=currentBalance||live!=currentOwners)return 11;
                    }
                    if(live!=owners||allocated-released!=balance)return 12;
                    for(int operation=0;operation<3;operation++) {
                        balance=allocated-released;owners=live;failCopy=true;bool caught=false;
                        try {
                            if(operation==0)values.GetEnumerator();
                            else if(operation==1)values.AsEnumerable();
                            else {Repeated<Item> failed=new(original,1);}
                        }catch(ref Exception error){if(error.GetCode()!=73)return 13;caught=true;}
                        failCopy=false;
                        if(!caught||allocated-released!=balance||live!=owners||original.Id!=7||original.Text[0]!='X'||values.Count!=1)return 14;
                    }
                    {
                        Item input=new(11,s"zero");Repeated<Item> empty=new(move input,0);copied=copies;
                        var cursor=empty.GetEnumerator();
                        if(copies!=copied+1||cursor.MoveNext()||cursor.MoveNext())return 15;
                        if(empty.AsEnumerable().Take(0).Count()!=0)return 16;
                    }
                    {
                        var detached=Detached();
                        if(!detached.MoveNext())return 17;
                        Item value=detached.Current;if(value.Id!=9||!CString::Equals(value.Text.Data,"detached"))return 18;
                    }
                }
                if(live!=0||allocated!=released)return 19;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedReferenceCapturesAndEmptyIteratorsKeepRootsUntilTheirOwnScopeEnds(int optimization)
        =>Run("""
            using std;
            public static replace nuint std::TracingCollector::CollectionThreshold()=>0;
            class Box {
                public Box^? Self;public int Value;
                public Box(int value){Self=null;Value=value;}
            }
            Repeated<Box^>::Enumerator Detached(nuint count) {
                Box^ value=new^ Box(42);value.Self=value;
                Repeated<Box^> values=new(value,count);
                return values.GetEnumerator();
            }
            void Exercise() {
                var first=Detached(2);var second=first;
                if(GC::Collect()!=0||TracingCollector::ManagedCount!=1)throw new Exception("lost capture");
                if(!first.MoveNext()||!second.MoveNext())throw new Exception("missing item");
                {Box^ item=first.Current;item.Value=73;}
                {Box^ item=second.Current;if(item.Value!=73||item.Self!.Value!=73)throw new Exception("lost reference semantics");}
                if(GC::Collect()!=0||!first.MoveNext()||first.MoveNext()||first.MoveNext())throw new Exception("lost exhausted capture");
                if(GC::Collect()!=0||TracingCollector::ManagedCount!=1)throw new Exception("exhausted root retired early");
            }
            void Empty() {
                var empty=Detached(0);
                if(empty.MoveNext()||empty.MoveNext()||GC::Collect()!=0||TracingCollector::ManagedCount!=1)throw new Exception("empty capture lost");
            }
            int main() {
                Exercise();if(GC::Collect()!=1||TracingCollector::ManagedCount!=0)return 1;
                Empty();if(GC::Collect()!=1||TracingCollector::ManagedCount!=0)return 2;
                return 0;
            }
            """,optimization);

    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("repeated.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Repeated exit {result.ExitCode}: {result.StandardError}");
    }
}

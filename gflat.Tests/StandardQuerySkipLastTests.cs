namespace gflat.Tests;

public sealed class StandardQuerySkipLastTests
{
    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("query-skip-last.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrefixesMatchHostSkipLastAndCursorsRemainIndependent(int optimization)
    {
        var random = new Random(91273);
        using var packet = new MemoryStream();
        int rows = 0;
        for (int length = 0; length <= 24; length += 3)
        foreach (int skipped in new[] { 0, 1, 3, length, length + 2, 255 })
        {
            int[] values = Enumerable.Range(0, length).Select(_ => random.Next(32)).ToArray();
            int[] expected = values.SkipLast(skipped).ToArray();
            packet.WriteByte((byte)length); packet.WriteByte((byte)skipped);
            packet.WriteByte((byte)expected.Length);
            foreach (int value in values) packet.WriteByte((byte)value);
            foreach (int value in expected) packet.WriteByte((byte)value);
            rows++;
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run(Memory + $$$"""
            nuint ReadByte(readonly(char)* text, ref nuint offset) {
                nuint value=0;
                for(int i=0;i<2;i++) {
                    char digit=text[offset];offset++;
                    nuint part=(nuint)((int)digit-(int)'0');
                    if(digit>='A')part=(nuint)((int)digit-(int)'A'+10);
                    value=value*16+part;
                }
                return value;
            }
            int main() {
                readonly(char)* packet="{{{hex}}}";nuint offset=0;
                for(int row=0;row<{{{rows}}};row++) {
                    nuint length=ReadByte(packet,ref offset);nuint skipped=ReadByte(packet,ref offset);
                    if(skipped==255)skipped=(nuint)0-(nuint)1;
                    nuint count=ReadByte(packet,ref offset);int[24] data;int[24] saved;
                    for(nuint i=0;i<length;i++){data[i]=(int)ReadByte(packet,ref offset);saved[i]=data[i];}
                    {
                        Span<int> values=new(data,length);var query=values.AsEnumerable().SkipLast(skipped);
                        var first=query.GetEnumerator();var second=query.GetEnumerator();
                        for(nuint i=0;i<count;i++) {
                            int expected=(int)ReadByte(packet,ref offset);
                            if(!first.MoveNext()||first.Current!=expected||first.Current!=expected)return 1;
                            if(!second.MoveNext()||second.Current!=expected)return 2;
                        }
                        if(first.MoveNext()||first.MoveNext()||second.MoveNext())return 3;
                        for(nuint i=0;i<length;i++)if(values[i]!=saved[i])return 4;
                    }
                }
                if(offset!={{{hex.Length}}}||allocated!=released)return 5;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LookaheadZeroAndEarlyExitHaveExactMoveAndCurrentEffects(int optimization)
        => Run(Memory + IntSource + """
            int main() {
                {
                    Enumerable<int,Source,Source::Enumerator> values=new(new Source());
                    var query=values.SkipLast(2);var iterator=query.GetEnumerator();
                    if(moves!=0||reads!=0||active!=1)return 1;
                    if(!iterator.MoveNext()||iterator.Current!=0||iterator.Current!=0||moves!=3||reads!=3)return 2;
                    if(!iterator.MoveNext()||iterator.Current!=1||moves!=4||reads!=4)return 3;
                }
                if(active!=0)return 4;
                moves=0;reads=0;int before=attempts;deny=true;
                {
                    Enumerable<int,Source,Source::Enumerator> values=new(new Source());
                    foreach(int value in values.SkipLast(2).Take(0))return 5;
                    if(moves!=0||reads!=0)return 6;
                    var iterator=values.SkipLast(0).GetEnumerator();
                    if(!iterator.MoveNext()||moves!=1||reads!=0)return 7;
                    if(iterator.Current!=0||iterator.Current!=0||reads!=2)return 8;
                }
                deny=false;
                if(attempts!=before||active!=0)return 9;
                moves=0;reads=0;
                {
                    Enumerable<int,Source,Source::Enumerator> values=new(new Source());
                    var iterator=values.SkipLast(2).GetEnumerator();
                    for(int expected=0;expected<3;expected++)if(!iterator.MoveNext()||iterator.Current!=expected)return 10;
                    if(iterator.MoveNext()||moves!=6||reads!=5||iterator.MoveNext()||moves!=6)return 11;
                }
                if(active!=0||allocated!=released)return 12;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwnedLookaheadCleansOnFailuresAndBufferedCurrentCopyCanRetry(int optimization)
        => Run(Memory + OwningSource + """
            int main() {
                {
                    Enumerable<Owner,Source,Source::Enumerator> values=new(new Source());
                    var iterator=values.SkipLast(2).GetEnumerator();
                    if(!iterator.MoveNext()||reads!=3)return 1;
                    Owner output=new Owner(99);copyBudget=0;
                    try{output=iterator.Current;return 2;}catch(ref Exception error){if(error.GetCode()!=37)return 3;}
                    copyBudget=-1;if(output.Id!=99)return 4;
                    output=iterator.Current;if(output.Id!=0||reads!=3)return 5;
                    output.Text[0]='X';Owner again=iterator.Current;if(again.Text[0]!='o')return 18;
                    if(!iterator.MoveNext()||reads!=4)return 6;
                }
                if(active!=0||made!=dead||allocated!=released)return 7;
                {
                    Enumerable<Owner,Source,Source::Enumerator> values=new(new Source());
                    var iterator=values.SkipLast(2).GetEnumerator();
                    while(iterator.MoveNext()){Owner current=iterator.Current;}
                    if(made!=dead||active!=1)return 19;
                }
                if(active!=0||allocated!=released)return 20;
                for(int late=0;late<2;late++)for(int kind=0;kind<2;kind++) {
                    moves=0;reads=0;moveFailure=-1;currentFailure=-1;
                    int failedPosition=2;if(late==1)failedPosition=3;
                    if(kind==0)moveFailure=failedPosition;else currentFailure=failedPosition;
                    {
                        Enumerable<Owner,Source,Source::Enumerator> values=new(new Source());
                        var iterator=values.SkipLast(2).GetEnumerator();bool caught=false;
                        if(late==1&&!iterator.MoveNext())return 21;
                        int oldDead=dead;
                        try{iterator.MoveNext();}catch(ref Exception error){if(error.GetCode()!=29)return 8;caught=true;}
                        if(!caught)return 9;
                        if(late==1&&dead!=oldDead+1)return 22;
                        int oldMoves=moves;int oldReads=reads;
                        if(iterator.MoveNext()||moves!=oldMoves||reads!=oldReads)return 10;
                        try{Owner invalid=iterator.Current;return 11;}catch(ref Exception error){}
                    }
                    if(active!=0||made!=dead||allocated!=released)return 12;
                }
                moveFailure=-1;currentFailure=-1;
                {
                    Vector<Owner> backing=new();
                    for(int i=0;i<3;i++)backing.Add(new Owner(i+40));
                    {
                        var iterator=backing.AsEnumerable().SkipLast(2).GetEnumerator();
                        copyBudget=1;bool caught=false;
                        try{iterator.MoveNext();}
                        catch(ref Exception error){if(error.GetCode()!=37)return 23;caught=true;}
                        copyBudget=-1;
                        if(!caught||iterator.MoveNext())return 24;
                        try{Owner invalid=iterator.Current;return 25;}catch(ref Exception error){}
                        if(backing.Count!=3)return 26;
                        for(nuint i=0;i<3;i++)if(backing[i].Id!=(int)i+40||backing[i].Text[0]!='o')return 27;
                        // First buffered owner is an independent copy, not a borrowed header.
                        // Changing backing storage while the failed iterator remains alive is safe.
                        backing[0].Text[0]='X';
                    }
                    if(backing[0].Text[0]!='X'||made-dead!=3)return 28;
                }
                if(made!=dead||allocated!=released)return 29;
                int requests=0;
                {
                    Enumerable<Owner,Source,Source::Enumerator> values=new(new Source());
                    var iterator=values.SkipLast(2).GetEnumerator();attempts=0;
                    if(!iterator.MoveNext())return 13;requests=attempts;
                }
                if(requests==0)return 14;
                for(int failure=1;failure<=requests;failure++) {
                    {
                        Enumerable<Owner,Source,Source::Enumerator> values=new(new Source());
                        var iterator=values.SkipLast(2).GetEnumerator();attempts=0;failAt=failure;
                        bool caught=false;
                        try{iterator.MoveNext();}catch(ref BadAllocException error){caught=true;}
                        failAt=0;if(!caught)return 15;
                        int oldMoves=moves;int oldReads=reads;
                        if(iterator.MoveNext()||moves!=oldMoves||reads!=oldReads)return 16;
                    }
                    if(active!=0||made!=dead||allocated!=released)return 17;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedElementsRemainRootedAcrossEvictionAndReleaseOnEofOrEarlyExit(int optimization)
        => Run("""
            using std; using std::Collections;
            int created = 0;
            class Cell {
                public int Id;
                public int Mark;
                public Cell^? Next;
                public Cell(int id) { Id = id; Mark = 0; }
            }
            Cell^ MakeCell(int id) {
                Cell^ result = new^ Cell(id);
                result.Next = result;
                created++;
                GC::Collect();
                return result;
            }
            nuint CleanCount() {
                GC::Collect();
                return std::TracingCollector::ManagedCount;
            }
            bool CountIs(nuint wanted, int phase) {
                nuint actual = CleanCount();
                if (actual == wanted) return true;
                Console::Error.Write("GC phase ", 9); phase.Format(Console::Error);
                Console::Error.Write(" expected ", 10); wanted.Format(Console::Error);
                Console::Error.Write(" actual ", 8); actual.Format(Console::Error);
                Console::Error.Write("\n", 1);
                return false;
            }
            int main() {
                nuint baseline = CleanCount();
                IntRange range = new(0, 7);
                var query = range.AsEnumerable().Select(MakeCell).SkipLast(2);
                {
                    var iterator = query.GetEnumerator();
                    if (created != 0) return 1;
                    for (int expected = 0; expected < 5; expected++) {
                        if (!iterator.MoveNext() || created != expected + 3) return 2;
                        if (!CountIs(baseline + 3, 10 + expected)) return 3;
                        {
                            Cell^ first = iterator.Current;
                            Cell^ second = iterator.Current;
                            if (first.Id != expected || second.Id != expected) return 4;
                            first.Mark = expected + 1000;
                            GC::Collect();
                            if (second.Mark != expected + 1000 || iterator.Current.Mark != expected + 1000) return 5;
                        }
                        // Both output-reference locals expire before the next eviction.
                    }
                    if (iterator.MoveNext() || created != 7) return 6;
                    // The iterator remains alive here, but EOF has cleared its withheld tail.
                    if (!CountIs(baseline, 20)) return 7;
                    if (iterator.MoveNext() || created != 7 || !CountIs(baseline, 21)) return 8;
                }
                if (!CountIs(baseline, 22)) return 9;
                created = 0;
                foreach (Cell^ value in query) {
                    if (value.Id != 0 || created != 3) return 10;
                    if (!CountIs(baseline + 3, 30)) return 11;
                    break;
                }
                if (created != 3 || !CountIs(baseline, 31)) return 12;
                return 0;
            }
            """, optimization);

    private const string Memory = """
        using std; using std::Collections;
        int attempts=0;int allocated=0;int released=0;int failAt=0;bool deny=false;
        extern void*? malloc(nuint count);extern void free(void* value);
        namespace Memory {
            public replace void*? Allocate(nuint count) {
                attempts++;if(deny||attempts==failAt)return null;
                void*? value=malloc(count);if(value!=null)allocated++;return value;
            }
            public replace void Free(void* value)nothrow{released++;free(value);}
        }
        """;

    private const string IntSource = """
        int active=0;int moves=0;int reads=0;
        struct Source {
            public Enumerator GetEnumerator()=>new Enumerator();
            public struct Enumerator {
                private int position;
                public Enumerator(){position=-1;active++;}
                public bool MoveNext(){moves++;position++;return position<5;}
                public int Current{get{reads++;return position;}}
                ~Enumerator(){active--;}
            }
        }
        """;

    private const string OwningSource = """
        int active=0;int moves=0;int reads=0;int made=0;int dead=0;
        int copyBudget=-1;int moveFailure=-1;int currentFailure=-1;
        struct Owner {
            public int Id;public String Text;
            public Owner(int id){Id=id;Text=s"owned";made++;}
            public copy Owner(immutable(Owner)* source) {
                Id=source.Id;Text=source.Text;
                if(copyBudget==0)throw new Exception("copy failed",37);
                if(copyBudget>0)copyBudget--;made++;
            }
            ~Owner(){dead++;}
        }
        struct Source {
            public Enumerator GetEnumerator()=>new Enumerator();
            public struct Enumerator {
                private int position;
                public Enumerator(){position=-1;active++;}
                public bool MoveNext(){moves++;position++;if(position==moveFailure)throw new Exception("move failed",29);return position<5;}
                public Owner Current{get{reads++;if(position==currentFailure)throw new Exception("current failed",29);return new Owner(position);}}
                ~Enumerator(){active--;}
            }
        }
        """;
}

namespace gflat.Tests.Std.Collections;

public class BitSetEnumerationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryBitPositionAcrossWordsAndDirtyPaddingRespectsLogicalLength(int optimization)
        => Run("""
            using std; using std::Queries; using std::Collections;
            int main() {
                uint[5] words;
                for (nuint size=0;size<=(nuint)129;size++) {
                    for(int i=0;i<5;i++)words[i]=0xffffffffu;
                    BitSet::Enumerator dense=new(words,size);
                    nuint expected=0;
                    while(dense.MoveNext()) {
                        if(dense.Current!=expected||expected>=size)return 1;
                        expected++;
                    }
                    if(expected!=size||dense.MoveNext())return 2;
                    for(nuint target=0;target<size;target++) {
                        for(int i=0;i<5;i++)words[i]=0u;
                        words[target/32]=1u<<(int)(target%32);
                        nuint tail=size%32;
                        if(tail!=0)words[size/32]=words[size/32]|(0xffffffffu<<(int)tail);
                        BitSet::Enumerator sparse=new(words,size);
                        if(!sparse.MoveNext()||sparse.Current!=target||sparse.MoveNext()||sparse.MoveNext())return 3;
                    }
                    for(int i=0;i<5;i++)words[i]=0u;
                    if(size%32!=0)words[size/32]=0xffffffffu<<(int)(size%32);
                    BitSet::Enumerator paddingOnly=new(words,size);
                    if(paddingOnly.MoveNext())return 4;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RawBorrowedWordsAreReloadedBetweenCallsAndCopiesKeepIndependentPositions(int optimization)
        => Run("""
            using std; using std::Queries; using std::Collections;
            int main() {
                uint[3] words=[0x80000001u,0u,0xffffffffu];
                BitSet::Enumerator iterator=new(words,65);
                if(!iterator.MoveNext()||iterator.Current!=0)return 1;
                // Remove an unread bit and add another within the current word.
                words[0]=1u<<5;
                if(!iterator.MoveNext()||iterator.Current!=5)return 2;
                BitSet::Enumerator copy=iterator;
                words[0]=words[0]|(1u<<31);
                if(!iterator.MoveNext()||iterator.Current!=31)return 3;
                if(!copy.MoveNext()||copy.Current!=31)return 4;
                // Adding behind the current position must not cause repetition.
                words[0]=words[0]|1u; words[1]=1u;
                if(!iterator.MoveNext()||iterator.Current!=32)return 5;
                words[1]=0u;
                if(!copy.MoveNext()||copy.Current!=64)return 6;
                if(!iterator.MoveNext()||iterator.Current!=64||iterator.MoveNext()||copy.MoveNext())return 7;
                bool caught=false;
                try{iterator.Current;}catch{caught=true;}
                if(!caught)return 8;
                // Once exhausted, both old and new scans stay at their end.
                words[1]=0xffffffffu;
                if(iterator.MoveNext()||copy.MoveNext())return 9;
                BitSet::Enumerator fresh=new(words,65);
                nuint count=0;
                while(fresh.MoveNext())count++;
                if(count!=36)return 10;
                return 0;
            }
            """, optimization);

    private static void Run(string code,int optimization)
    {
        var sources=new[]{"Collections/IEnumerable.gf", "Queries/Enumerable.gf","Collections/BitSet.gf", "IEquatable.gf", "IHashable.gf", "Hashing/primitive_hashing.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("main.gf",code));
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}

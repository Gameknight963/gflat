namespace gflat.Tests.Std.Collections;

public sealed class BitSetValueTests
{
    private const string Counters="""
        using Standard; using Standard::Text; using Standard::IO; using Standard::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int allocated=0;int released=0;int attempts=0;int denied=0;int failAfter=-1;bool deny=false;
        bool capture=false;void*? captured=null;void*? watchSource=null;void*? watchTarget=null;int watchedFrees=0;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny||failAfter==0){denied++;return null;}if(failAfter>0)failAfter--;
                void*? value=malloc(count);if(value!=null){allocated++;if(capture)captured=value;}return value;}
            public replace void Free(void* value)nothrow{if(value==watchSource||value==watchTarget)watchedFrees++;released++;free(value);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LogicalEqualityAndHashesMatchBooleanModelAcrossResizeShiftAndPaddingHistories(int optimization)
    {
        bool[] model=[];var records=new List<(int[] Header,byte[] Bits)>();
        void Record(int operation,int argument=0,int second=0){
            switch(operation){
                case 0:Array.Resize(ref model,argument);break;
                case 1:model[argument]=second!=0;break;
                case 2:Array.Fill(model,argument!=0);break;
                case 3:
                case 4:
                    // A per-bit snapshot oracle has no packed padding to leak
                    // into later shifts or growth.
                    bool[] previous=(bool[])model.Clone();
                    for(int index=0;index<model.Length;index++){
                        int source=operation==3?index-argument:index+argument;
                        model[index]=argument>=0&&source>=0&&source<previous.Length&&previous[source];
                    }
                    break;
                case 5:model[argument]=!model[argument];break;
            }
            byte[] bits=Enumerable.Range(0,model.Length).Select(index=>(byte)(model[index]?1:0)).ToArray();
            records.Add(([operation,argument,second,model.Length,bits.Count(value=>value!=0)],bits));
        }
        Record(0,65);Record(2,1);Record(0,33);Record(0,65);Record(3,31);Record(4,32);
        Record(0,31);Record(2,1);Record(0,32);Record(0,33);Record(3,-1);Record(4,-1);
        int[] boundaries=[0,1,31,32,33,63,64,65,127,128,129,255,256];var random=new Random(431805);
        foreach(int size in boundaries){Record(0,size);Record(2,1);Record(4,1);Record(3,32);}
        for(int sample=0;sample<360;sample++){
            int operation=random.Next(6);
            if(operation==0)Record(0,boundaries[random.Next(boundaries.Length)]);
            else if(operation==1||operation==5){if(model.Length==0)Record(0,1);Record(operation,random.Next(model.Length),random.Next(2));}
            else if(operation==2)Record(2,random.Next(2));else Record(operation,random.Next(model.Length+40));
        }
        string path=Path.Combine(Path.GetTempPath(),"gflat_bitset_value_"+Guid.NewGuid().ToString("N")+".bin");
        try{
            using(var writer=new BinaryWriter(File.Create(path)))foreach(var record in records){foreach(int value in record.Header)writer.Write(value);writer.Write(record.Bits);}
            Run(Counters+$$"""
                bool Equal(immutable(BitSet)* first,immutable(BitSet)* second)nothrow=>first.Equals(second);
                nuint Hash(immutable(BitSet)* value)nothrow=>value.GetHashCode();
                int main(){
                    {
                        File input=File::OpenRead({{Literal(path)}});BitSet bits=new();char[256] expected;
                        for(int sample=0;sample<{{records.Count}};sample++){
                            int operation=FileBinary::ReadInt32LittleEndian(&input);int argument=FileBinary::ReadInt32LittleEndian(&input);int second=FileBinary::ReadInt32LittleEndian(&input);int size=FileBinary::ReadInt32LittleEndian(&input);int ones=FileBinary::ReadInt32LittleEndian(&input);
                            if(size<0||size>256)return 1;input.ReadExactly(&expected[0],(nuint)size);
                            if(operation==0)bits.Resize((nuint)argument);else if(operation==1)bits[(nuint)argument]=second!=0;else if(operation==2)bits.SetAll(argument!=0);
                            else if(operation==3){nuint shift=(nuint)argument;if(argument<0)shift=(nuint)0-(nuint)1;bits.ShiftLeft(shift);}
                            else if(operation==4){nuint shift=(nuint)argument;if(argument<0)shift=(nuint)0-(nuint)1;bits.ShiftRight(shift);}else bits.Flip((nuint)argument);
                            if(bits.Length!=(nuint)size||bits.CountSetBits()!=(nuint)ones){
                                String failure=s$"sample={sample}, op={operation}, arg={argument}, size={size}, actualLength={bits.Length}, expectedCount={ones}, actualCount={bits.CountSetBits()}";
                                Console::WriteLine(failure.Data,failure.Length);return 2;
                            }
                            int baseline=allocated-released;
                            {
                                BitSet fresh=new((nuint)size);BitSet history=new((nuint)size+63);history.SetAll(true);history.Resize((nuint)size);history.Clear();
                                for(nuint i=0;i<(nuint)size;i++){bool bit=expected[i]!='\0';fresh[i]=bit;history[i]=bit;if(bits[i]!=bit)return 3;}
                                BitSet differentLength=new((nuint)size+1);for(nuint i=0;i<(nuint)size;i++)differentLength[i]=fresh[i];
                                int before=attempts;deny=true;
                                if(!Equal(&bits,&fresh)||!Equal(&fresh,&bits)||!Equal(&bits,&history)||Equal(&bits,&differentLength)||Hash(&bits)!=Hash(&fresh)||Hash(&bits)!=Hash(&history))return 4;
                                deny=false;if(attempts!=before)return 5;
                                BitSet copy=bits;BitSet clone=bits.Clone();
                                if(size!=0){copy.Flip(0);clone.Clear();}else{copy.Resize(1);copy[0]=true;clone.Resize(2);clone[1]=true;}
                                for(nuint i=0;i<(nuint)size;i++)if(bits[i]!=(expected[i]!='\0'))return 6;
                                if(bits.Length!=(nuint)size)return 7;
                            }
                            if(allocated-released!=baseline)return 8;
                        }
                        input.Close();
                    }
                    if(allocated!=released)return 9;return 0;
                }
                """,optimization);
        }finally{File.Delete(path);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeepCopyAndCloneAreIndependentAndEveryCopyFailurePreservesBothOwnersStorage(int optimization)
        =>Run(Counters+"""
            int main(){
                {
                    BitSet empty=new();BitSet old=new(33);int before=attempts;deny=true;
                    {BitSet copy=empty;BitSet clone=empty.Clone();old=empty;if(copy.Length!=0||clone.Length!=0||old.Length!=0||attempts!=before)return 1;}
                    deny=false;
                    int[12] sizes=[0,1,31,32,33,63,64,65,127,128,129,4097];
                    for(nuint sample=0;sample<12;sample++){
                        nuint size=(nuint)sizes[sample];captured=null;capture=true;BitSet source=new(size);capture=false;void*? sourceStorage=captured;
                        for(nuint i=0;i<size;i++)source[i]=i%3==1;BitSet saved=source;
                        captured=null;capture=true;BitSet target=new(19);capture=false;void*? targetStorage=captured;target[0]=true;target[18]=true;BitSet oldTarget=target;
                        for(int operation=0;operation<2;operation++){
                            int initial=allocated;int points=0;{BitSet measured=new();if(operation==0)measured=source;else measured=source.Clone();points=allocated-initial;if(!measured.Equals(&saved))return 2;}
                            if(size==0&&points!=0)return 3;if(size!=0&&points==0)return 4;
                            for(int point=0;point<points;point++){
                                watchSource=sourceStorage;watchTarget=targetStorage;watchedFrees=0;int balance=allocated-released;int deniedBefore=denied;failAfter=point;bool caught=false;
                                try{if(operation==0)target=source;else target=source.Clone();return 5;}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                                if(!caught||denied==deniedBefore||allocated-released!=balance||watchedFrees!=0||!source.Equals(&saved)||!target.Equals(&oldTarget))return 6;
                                watchSource=null;watchTarget=null;
                            }
                        }
                        BitSet copied=source;BitSet cloned=source.Clone();source=source;if(!source.Equals(&saved))return 7;
                        if(size!=0){copied.Flip(0);if(source[0]!=saved[0]||cloned[0]!=saved[0])return 8;}
                        source.Resize(0);if(cloned.Length!=size||!cloned.Equals(&saved)||copied.Length!=size)return 9;
                    }
                }
                if(allocated!=released)return 10;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopiedHashKeysIgnoreOriginalMutationAndSurviveRehashedOwnerMoves(int optimization)
        =>Run(Counters+"""
            int main(){
                {
                    HashSet<BitSet> set=new();HashMap<BitSet,int> map=new();BitSet original=new(33);original[1]=true;original[32]=true;BitSet lookup=original;
                    map.Add(original,17);if(!set.Add(original))return 1;
                    original.Clear();original.Resize(1);original[0]=true;
                    if(!set.Contains(&lookup)||map[&lookup]!=17||set.Add(lookup)||map.TryAdd(lookup,99)||map[&lookup]!=17)return 2;
                    BitSet same=lookup;if(!EqualityComparer<BitSet>::Equals(&same,&lookup)||same.GetHashCode()!=lookup.GetHashCode())return 3;
                    for(int i=0;i<64;i++){
                        BitSet key=new((nuint)(80+i));key[(nuint)i]=true;key[(nuint)(79+i)]=true;
                        map.Add(key,1000+i);if(!set.Add(key))return 4;key.Clear();key.Resize(0);
                    }
                    map.Reserve(200);set.Reserve(200);
                    if(map.Count!=65||set.Count!=65||map[&lookup]!=17||!set.Contains(&lookup))return 5;
                    for(int i=0;i<64;i++){
                        BitSet key=new((nuint)(80+i));key[(nuint)i]=true;key[(nuint)(79+i)]=true;
                        if(map[&key]!=1000+i||!set.Contains(&key))return 6;
                    }
                    if(!map.Remove(&lookup)||!set.Remove(&lookup)||map.ContainsKey(&lookup)||set.Contains(&lookup)||lookup.Length!=33||!lookup[1]||!lookup[32])return 7;
                }
                if(allocated!=released)return 8;return 0;
            }
            """,optimization);

    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static void Run(string source,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("bitset-value.gf",source));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"BitSet value exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

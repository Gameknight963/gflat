using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardHashCombineTests
{
    // Independent unchecked oracle for this mixer implementation; hashes remain
    // implementation details rather than a persistent serialization contract.
    private static ulong Mix(ulong value)
    {
        value=unchecked((value^(value>>30))*0xbf58476d1ce4e5b9ul);
        value=unchecked((value^(value>>27))*0x94d049bb133111ebul);
        return value^(value>>31);
    }
    private static uint Integer(ulong value){ulong mixed=Mix(value);return (uint)mixed^(uint)(mixed>>32);}
    private static uint Combine(ulong first,ulong second)=>Integer(Mix(unchecked(first+0x9e3779b97f4a7c15ul))^second);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FullWidthOrderedCombinationAndExistingIntegerMatchIndependentOracle(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-hash-combine-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try {
            string path=Path.Combine(directory,"oracle.bin");
            ulong mask=IntPtr.Size==8?ulong.MaxValue:uint.MaxValue;
            var pairs=new List<(ulong First,ulong Second)>();
            ulong[] edges=[0,1,2,uint.MaxValue,1ul<<32,1ul<<63,ulong.MaxValue];
            foreach(ulong first in edges)foreach(ulong second in edges)pairs.Add((first&mask,second&mask));
            var random=new Random(99283);
            for(int i=0;i<256;i++){byte[] bytes=new byte[16];random.NextBytes(bytes);pairs.Add((BitConverter.ToUInt64(bytes)&mask,BitConverter.ToUInt64(bytes,8)&mask));}
            using(var output=new BinaryWriter(File.Create(path))){
                ulong sample=0;
                for(int i=0;i<4096;i++){
                    output.Write(sample);output.Write(Integer(sample));
                    sample=unchecked(sample*6364136223846793005ul+1442695040888963407ul);
                }
                foreach(var pair in pairs){output.Write(pair.First);output.Write(pair.Second);output.Write(Combine(pair.First,pair.Second));}
            }
            string code=$$"""
                extern void*? fopen(readonly(char)* path,readonly(char)* mode) nothrow;
                extern nuint fread(void* output,nuint size,nuint count,void* input) nothrow;
                extern int fclose(void* input) nothrow;
                ulong Word(readonly(char)* bytes,nuint count)nothrow{
                    ulong value=0ul;for(nuint i=0;i<count;i++)value=value|((ulong)(byte)bytes[i]<<(int)(i*8));return value;
                }
                int main()nothrow{
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;
                    defer{fclose(file!);}char[20] row;
                    for(int i=0;i<4096;i++){
                        if(fread((void*)&row[0],1,12,file!)!=12)return 2;
                        if(std::Hashing::Integer(Word(row,8))!=(nuint)Word(&row[8],4))return 3;
                    }
                    for(int i=0;i<{{pairs.Count}};i++){
                        if(fread((void*)&row[0],1,20,file!)!=20)return 4;
                        nuint first=(nuint)Word(row,8);nuint second=(nuint)Word(&row[8],8);
                        if(std::Hashing::Combine(first,second)!=(nuint)Word(&row[16],4))return 5;
                    }
                    if(fread((void*)&row[0],1,1,file!)!=0)return 6;return 0;
                }
                """;
            SourceFile[] sources=[new("Hashing.gf",Read("Hashing.gf")),new("main.gf",code)];
            string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
            foreach(string forbidden in new[]{"__gflat_eh","@malloc","@free","gflat$Memory$","llvm.memcpy"})Assert.DoesNotContain(forbidden,ir);
            foreach(string name in new[]{"Integer","Combine"}){
                Match definition=Regex.Match(ir,@"(?m)^define [^\r\n]*\$Hashing\$"+name+@"(?:\$|\()[^\r\n]*");
                Assert.True(definition.Success);Assert.Contains("nounwind",definition.Value);
            }
            var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
        } finally{Directory.Delete(directory,true);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CompositeKeysAndStructuredPairsDistributeWithoutQuadraticEquality(int optimization)
    {
        string code="""
            using std;
            static class State{public static int Comparisons;}
            struct Point:IHashable,IEquatable<Point>{
                public ulong X;public ulong Y;
                public Point(ulong x,ulong y){X=x;Y=y;}
                public immutable nuint GetHashCode()=>std::Hashing::Combine(std::Hashing::Integer(X),std::Hashing::Integer(Y));
                public immutable bool Equals(immutable(Point)* other){State::Comparisons++;return X==other.X&&Y==other.Y;}
            }
            Point Make(int i,int family){
                ulong value=(ulong)i;
                if(family==0)return new(value,value);
                if(family==1)return new(value,value+1ul);
                if(family==2)return new(value+1ul,value);
                if(family==3)return new(0ul,value);
                if(family==4)return new(value,0ul);
                if(family==5)return new(value<<32,7ul);
                value=(value<<32)|value;return new(value,value);
            }
            int Check(int family){
                nuint[1024] hashes;int[256] buckets;for(int i=0;i<256;i++)buckets[i]=0;
                int unique=0;int swapped=0;
                for(int i=0;i<1024;i++){
                    Point value=Make(i,family);nuint hash=value.GetHashCode();bool seen=false;
                    for(int j=0;j<i;j++)if(hashes[j]==hash)seen=true;
                    if(!seen)unique++;hashes[i]=hash;buckets[hash%256]++;
                    if(family==1&&hash==std::Hashing::Combine(std::Hashing::Integer(value.Y),std::Hashing::Integer(value.X)))swapped++;
                }
                int occupied=0;int maximum=0;
                for(int i=0;i<256;i++){if(buckets[i]!=0)occupied++;if(buckets[i]>maximum)maximum=buckets[i];}
                // Loose bounds for this finite corpus, not collision freedom.
                if(unique<900||occupied<200||maximum>24||swapped>100)return 1;
                HashMap<Point,int> map=new();HashSet<Point> set=new();map.Reserve(1024);set.Reserve(1024);
                State::Comparisons=0;
                for(int i=0;i<1024;i++){map.Add(Make(i,family),i);if(!set.Add(Make(i,family)))return 2;}
                if(State::Comparisons>16384)return 3;
                State::Comparisons=0;
                for(int i=0;i<1024;i++){
                    Point key=Make(i,family);Point equal=new(key.X,key.Y);
                    if(!key.Equals(&equal)||key.GetHashCode()!=equal.GetHashCode())return 4;
                    if(!map.ContainsKey(&equal)||map[&equal]!=i||!set.Contains(&equal))return 5;
                    if(map.TryAdd(equal,-1)||set.Add(equal))return 6;
                }
                if(State::Comparisons>16384||map.Count!=1024||set.Count!=1024)return 7;
                State::Comparisons=0;
                for(int i=1024;i<2048;i++){Point key=Make(i,family);if(map.ContainsKey(&key)||set.Contains(&key))return 8;}
                if(State::Comparisons>16384)return 9;
                for(int i=0;i<1024;i++){Point key=Make(i,family);if(!map.Remove(&key)||!set.Remove(&key))return 10;}
                if(!map.IsEmpty||!set.IsEmpty)return 11;return 0;
            }
            int main(){for(int family=0;family<7;family++){int result=Check(family);if(result!=0)return result;}return 0;}
            """;
        var sources=new[]{"Hashing.gf","IHashable.gf","IEquatable.gf","HashMap.gf","HashSet.gf"}
            .Select(name=>new SourceFile(name,Read(name))).Append(new SourceFile("main.gf",code));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
    private static string Read(string name)=>File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name));
    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
}

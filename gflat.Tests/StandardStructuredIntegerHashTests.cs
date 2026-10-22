namespace gflat.Tests;

public sealed class StandardStructuredIntegerHashTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StructuredIntegerFamiliesDistributeAndAvoidQuadraticMapEquality(int optimization)
    {
        string code = """
            using std;
            static class State { public static int EqualCalls; public static bool Wide; }
            nuint Hash<T:IHashable>(ref immutable(T) value)=>value.GetHashCode();
            struct Key:IHashable,IEquatable<Key> {
                public extralong Bits;
                public Key(extralong bits){Bits=bits;}
                public immutable nuint GetHashCode(){
                    if(State::Wide)return Hash(ref Bits);
                    ulong low=(ulong)Bits;return Hash(ref low);
                }
                public immutable bool Equals(immutable(Key)* other){State::EqualCalls++;return Bits==other.Bits;}
            }
            extralong Bits(int index,int family){
                extralong low=(extralong)index;
                if(family==0)return low;
                extralong high=low;
                if(family==2)high++;
                if(family==3)low++;
                int shift=32;if(State::Wide)shift=64;
                return(high<<shift)|low;
            }
            int Check(bool wide,int family){
                State::Wide=wide;
                nuint[1024] hashes;int[256] buckets;
                for(int i=0;i<256;i++)buckets[i]=0;
                int unique=0;
                for(int i=0;i<1024;i++){
                    Key key=new(Bits(i,family));nuint hash=key.GetHashCode();bool seen=false;
                    for(int j=0;j<i;j++)if(hashes[j]==hash)seen=true;
                    if(!seen)unique++;
                    hashes[i]=hash;buckets[hash%256]++;
                }
                int occupied=0;int maximum=0;
                for(int i=0;i<256;i++){if(buckets[i]!=0)occupied++;if(buckets[i]>maximum)maximum=buckets[i];}
                // Loose quality bounds for these specific structured fixtures, not a
                // general collision-free promise or a stable exact hash value.
                if(unique<900||occupied<200||maximum>20)return 1;
                HashMap<Key,int> map=new();map.Reserve(1024);
                State::EqualCalls=0;
                for(int i=0;i<1024;i++)map.Add(new Key(Bits(i,family)),i);
                if(State::EqualCalls>8192)return 2;
                State::EqualCalls=0;
                for(int i=0;i<1024;i++){
                    Key key=new(Bits(i,family));
                    if(!map.ContainsKey(&key)||map[&key]!=i)return 3;
                }
                if(State::EqualCalls>8192)return 4;
                State::EqualCalls=0;
                for(int i=1024;i<2048;i++){
                    Key key=new(Bits(i,family));if(map.ContainsKey(&key))return 5;
                }
                if(State::EqualCalls>8192)return 6;
                return 0;
            }
            int main(){
                for(int family=0;family<4;family++){
                    int result=Check(false,family);if(result!=0)return result;
                    result=Check(true,family);if(result!=0)return result+10;
                }
                return 0;
            }
            """;
        var sources = new[] { "IHashable.gf", "IEquatable.gf", "Hashing.gf", "HashMap.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("main.gf",code));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}

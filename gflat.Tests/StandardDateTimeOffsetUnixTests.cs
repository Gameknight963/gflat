using System.Text;

namespace gflat.Tests;

public sealed class StandardDateTimeOffsetUnixTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnixFactoriesAndFlooringMatchDotNetAcrossOffsetsEpochFractionsAndExtremeBounds(int optimization)
    {
        const long epoch=621355968000000000L,maximum=3155378975999999999L,minute=600000000L;
        var instants=new HashSet<long>{0,1,9999,10000,9999999,10000000,epoch-10000001,epoch-10000000,epoch-9999999,
            epoch-10001,epoch-10000,epoch-9999,epoch-1,epoch,epoch+1,epoch+9999,epoch+10000,epoch+10000000,maximum-1,maximum};
        var random=new Random(683159);for(int i=0;i<16;i++)instants.Add(random.NextInt64(maximum));
        var rows=new List<System.DateTimeOffset>();foreach(long ticks in instants)foreach(int offset in new[]{-840,-1,0,1,840}){
            long wall=ticks+offset*minute;if(wall<0||wall>maximum)continue;
            rows.Add(new System.DateTimeOffset(new System.DateTime(wall,DateTimeKind.Unspecified),TimeSpan.FromMinutes(offset)));
        }
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var value in rows){
            long seconds=value.ToUnixTimeSeconds(),milliseconds=value.ToUnixTimeMilliseconds();
            writer.Write(value.Ticks);writer.Write(value.Offset.Ticks);writer.Write(value.UtcTicks);writer.Write(seconds);writer.Write(milliseconds);
            writer.Write(System.DateTimeOffset.FromUnixTimeSeconds(seconds).UtcTicks);writer.Write(System.DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcTicks);
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        string program=$$$"""
            using std;extern void*? malloc(nuint count);extern void free(void* storage);
            int attempts=0;int allocated=0;int released=0;bool deny=false;
            namespace Memory{
                public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? result=malloc(count);if(result!=null)allocated++;return result;}
                public replace void Free(void* storage)nothrow{released++;free(storage);}
            }
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* data,ref nuint at)nothrow{int value=(Digit(data[at])<<4)|Digit(data[at+1]);at+=2;return value;}
            long ReadLong(readonly(char)* data,ref nuint at)nothrow{ulong value=0ul;for(int shift=0;shift<64;shift+=8)value=value|((ulong)ReadByte(data,ref at)<<shift);return (long)value;}
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;int before=attempts;deny=true;
                for(int row=0;row<{{{rows.Count}}};row++){
                    long wall=ReadLong(packet,ref at);long offset=ReadLong(packet,ref at);long utc=ReadLong(packet,ref at);
                    long seconds=ReadLong(packet,ref at);long milliseconds=ReadLong(packet,ref at);long fromSeconds=ReadLong(packet,ref at);long fromMilliseconds=ReadLong(packet,ref at);
                    DateTimeOffset value=new(DateTime::FromTicks(wall),TimeSpan::FromTicks(offset));immutable(DateTimeOffset)* view=&value;
                    if(view.ToUnixTimeSeconds()!=seconds||view.ToUnixTimeMilliseconds()!=milliseconds||view.UtcDateTime.Ticks!=utc)return 1;
                    DateTimeOffset restoredSeconds=DateTimeOffset::FromUnixTimeSeconds(seconds);DateTimeOffset restoredMilliseconds=DateTimeOffset::FromUnixTimeMilliseconds(milliseconds);
                    if(restoredSeconds.UtcDateTime.Ticks!=fromSeconds||restoredSeconds.DateTime.Ticks!=fromSeconds||restoredSeconds.Offset.Ticks!=0l)return 2;
                    if(restoredMilliseconds.UtcDateTime.Ticks!=fromMilliseconds||restoredMilliseconds.DateTime.Ticks!=fromMilliseconds||restoredMilliseconds.Offset.Ticks!=0l)return 3;
                    if(restoredSeconds.ToUnixTimeSeconds()!=seconds||restoredMilliseconds.ToUnixTimeMilliseconds()!=milliseconds||value.DateTime.Ticks!=wall||value.Offset.Ticks!=offset)return 4;
                }
                if(at!={{{hex.Length}}}||attempts!=before)return 5;deny=false;
                DateTimeOffset target=DateTimeOffset::Parse("2000-01-02T03:04:05+05:45");DateTimeOffset saved=target;
                long[4] badSeconds=[-62135596801l,253402300800l,(-9223372036854775807l-1l),9223372036854775807l];
                long[4] badMilliseconds=[-62135596800001l,253402300800000l,(-9223372036854775807l-1l),9223372036854775807l];
                for(nuint i=0;i<4;i++){
                    bool caught=false;try{target=DateTimeOffset::FromUnixTimeSeconds(badSeconds[i]);}catch(ref Exception error){caught=true;}if(!caught||!target.EqualsExact(&saved))return 6;
                    caught=false;try{target=DateTimeOffset::FromUnixTimeMilliseconds(badMilliseconds[i]);}catch(ref Exception error){caught=true;}if(!caught||!target.EqualsExact(&saved))return 7;
                }
                if(allocated!=released)return 8;return 0;
            }
            """;
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("date-time-offset-unix.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"Unix conversions exit {result.ExitCode}: {result.StandardError}");
    }
}

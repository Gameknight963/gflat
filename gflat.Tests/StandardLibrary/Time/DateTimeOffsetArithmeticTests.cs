using System.Text;

namespace gflat.Tests.Std.Time;

public sealed class DateTimeOffsetArithmeticTests
{
    private const long Maximum=3155378975999999999L,Epoch=621355968000000000L,Minute=600000000L;
    private const string Support="""
        using Standard; using Standard::Text; using Standard::Time;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? result=malloc(count);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
        int ReadByte(readonly(char)* data,ref nuint at)nothrow{int value=(Digit(data[at])<<4)|Digit(data[at+1]);at+=2;return value;}
        long ReadLong(readonly(char)* data,ref nuint at)nothrow{ulong value=0ul;for(int shift=0;shift<64;shift+=8)value=value|((ulong)ReadByte(data,ref at)<<shift);return (long)value;}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DurationArithmeticMatchesDotNetAndChecksBothWallAndUtcBeforeOverflow(int optimization)
    {
        var values=new List<System.DateTimeOffset>();
        foreach(long utc in new[]{0L,1L,Epoch-1,Epoch,Epoch+1,Maximum-1,Maximum})
            foreach(int minutes in new[]{-840,-1,0,1,840}){
                long wall=utc+minutes*Minute;if(wall<0||wall>Maximum)continue;
                values.Add(new(new System.DateTime(wall,DateTimeKind.Unspecified),TimeSpan.FromMinutes(minutes)));
            }
        // Explicitly isolate wall-bound and UTC-bound failures at either end.
        values.Add(new(new System.DateTime(0),TimeSpan.FromMinutes(-1)));
        values.Add(new(new System.DateTime(Minute),TimeSpan.FromMinutes(1)));
        values.Add(new(new System.DateTime(Maximum),TimeSpan.FromMinutes(1)));
        values.Add(new(new System.DateTime(Maximum-Minute),TimeSpan.FromMinutes(-1)));
        long[] deltas=[0,1,-1,Minute+1,-Minute-1,864000000000L,-864000000000L,long.MinValue,long.MaxValue];
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var value in values)foreach(long delta in deltas){
            writer.Write(value.Ticks);writer.Write(value.Offset.Ticks);writer.Write(delta);
            WriteOutcome(writer,()=>value.Add(TimeSpan.FromTicks(delta)));
            WriteOutcome(writer,()=>value.Subtract(TimeSpan.FromTicks(delta)));
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Support+$$$"""
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;
                for(int row=0;row<{{{values.Count*deltas.Length}}};row++){
                    long wall=ReadLong(packet,ref at);long offset=ReadLong(packet,ref at);long delta=ReadLong(packet,ref at);
                    bool addValid=ReadByte(packet,ref at)!=0;long addWall=ReadLong(packet,ref at);long addUtc=ReadLong(packet,ref at);
                    bool subtractValid=ReadByte(packet,ref at)!=0;long subtractWall=ReadLong(packet,ref at);long subtractUtc=ReadLong(packet,ref at);
                    DateTimeOffset value=new(DateTime::FromTicks(wall),TimeSpan::FromTicks(offset));DateTimeOffset saved=value;
                    immutable(DateTimeOffset)* view=&value;TimeSpan duration=TimeSpan::FromTicks(delta);
                    int before=attempts;deny=true;
                    if(addValid){DateTimeOffset result=view.Add(duration);if(result.DateTime.Ticks!=addWall||result.UtcDateTime.Ticks!=addUtc||result.Offset.Ticks!=offset)return 1;}
                    if(subtractValid){DateTimeOffset result=view.Subtract(duration);if(result.DateTime.Ticks!=subtractWall||result.UtcDateTime.Ticks!=subtractUtc||result.Offset.Ticks!=offset)return 2;}
                    deny=false;if(attempts!=before||!value.EqualsExact(&saved))return 3;
                    DateTimeOffset target=saved;
                    if(!addValid){bool caught=false;try{target=view.Add(duration);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"DateTimeOffset addition is outside years 1 through 9999"))return 4;caught=true;}if(!caught||!target.EqualsExact(&saved))return 5;}
                    if(!subtractValid){bool caught=false;try{target=view.Subtract(duration);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"DateTimeOffset subtraction is outside years 1 through 9999"))return 6;caught=true;}if(!caught||!target.EqualsExact(&saved))return 7;}
                    if(!value.EqualsExact(&saved)||allocated!=released)return 8;
                }
                if(at!={{{hex.Length}}})return 9;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DifferenceUsesOnlyUtcInstantsAndHandlesTheFullDateRangeWithoutAllocation(int optimization)
    {
        var firstOffset=new System.DateTimeOffset(new System.DateTime(2024,6,1,12,30,0,DateTimeKind.Unspecified),TimeSpan.FromHours(2));
        System.DateTimeOffset[] values=[System.DateTimeOffset.MinValue,System.DateTimeOffset.MaxValue,
            System.DateTimeOffset.UnixEpoch.AddTicks(-1),System.DateTimeOffset.UnixEpoch,System.DateTimeOffset.UnixEpoch.AddTicks(1),
            firstOffset,firstOffset.ToOffset(TimeSpan.FromMinutes(-345)),firstOffset.AddTicks(1)];
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var first in values)foreach(var second in values){
            writer.Write(first.Ticks);writer.Write(first.Offset.Ticks);writer.Write(second.Ticks);writer.Write(second.Offset.Ticks);writer.Write(first.Subtract(second).Ticks);
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Support+$$$"""
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;int before=attempts;deny=true;
                for(int row=0;row<{{{values.Length*values.Length}}};row++){
                    long firstWall=ReadLong(packet,ref at);long firstOffset=ReadLong(packet,ref at);
                    long secondWall=ReadLong(packet,ref at);long secondOffset=ReadLong(packet,ref at);long expected=ReadLong(packet,ref at);
                    DateTimeOffset first=new(DateTime::FromTicks(firstWall),TimeSpan::FromTicks(firstOffset));
                    DateTimeOffset second=new(DateTime::FromTicks(secondWall),TimeSpan::FromTicks(secondOffset));
                    immutable(DateTimeOffset)* view=&first;TimeSpan actual=view.Difference(second);TimeSpan reverse=second.Difference(first);TimeSpan self=view.Difference(first);
                    if(actual.Ticks!=expected||reverse.Ticks!=-expected||self.Ticks!=0l)return 1;
                    if(first.DateTime.Ticks!=firstWall||first.Offset.Ticks!=firstOffset||second.DateTime.Ticks!=secondWall||second.Offset.Ticks!=secondOffset)return 2;
                }
                deny=false;if(at!={{{hex.Length}}}||attempts!=before||allocated!=released)return 3;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CalendarShiftsMatchDotNetAndPreserveOffsetTimeAndOperands(int optimization)
    {
        System.DateTime[] clocks=[System.DateTime.MinValue,System.DateTime.MaxValue,
            new(1,2,1),new(2,1,1),new(9999,10,31,23,59,59),new(9998,12,31,23,59,59),
            new System.DateTime(2024,1,31,12,34,56).AddTicks(1234567),new(2024,2,29),new(1900,3,31)];
        var values=new List<System.DateTimeOffset>();
        foreach(var clock in clocks)foreach(int minutes in new[]{-840,0,345,840}){
            try{values.Add(new(clock,TimeSpan.FromMinutes(minutes)));}catch(ArgumentException){}
        }
        int[] deltas=[int.MinValue,-120000,-9999,-13,-1,0,1,13,9999,120000,int.MaxValue];
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var value in values)foreach(int delta in deltas){
            writer.Write(value.Ticks);writer.Write(value.Offset.Ticks);writer.Write((long)delta);
            WriteOutcome(writer,()=>value.AddMonths(delta));WriteOutcome(writer,()=>value.AddYears(delta));
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Support+$$$"""
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;
                for(int row=0;row<{{{values.Count*deltas.Length}}};row++){
                    long wall=ReadLong(packet,ref at);long offset=ReadLong(packet,ref at);int delta=(int)ReadLong(packet,ref at);
                    bool monthValid=ReadByte(packet,ref at)!=0;long monthWall=ReadLong(packet,ref at);long monthUtc=ReadLong(packet,ref at);
                    bool yearValid=ReadByte(packet,ref at)!=0;long yearWall=ReadLong(packet,ref at);long yearUtc=ReadLong(packet,ref at);
                    DateTimeOffset value=new(DateTime::FromTicks(wall),TimeSpan::FromTicks(offset));DateTimeOffset saved=value;
                    immutable(DateTimeOffset)* view=&value;int before=attempts;deny=true;
                    if(monthValid){DateTimeOffset result=view.AddMonths(delta);if(result.DateTime.Ticks!=monthWall||result.UtcDateTime.Ticks!=monthUtc||result.Offset.Ticks!=offset)return 1;}
                    if(yearValid){DateTimeOffset result=view.AddYears(delta);if(result.DateTime.Ticks!=yearWall||result.UtcDateTime.Ticks!=yearUtc||result.Offset.Ticks!=offset)return 2;}
                    deny=false;if(attempts!=before||!value.EqualsExact(&saved))return 3;
                    DateTimeOffset target=saved;
                    if(!monthValid){bool caught=false;try{target=view.AddMonths(delta);}catch(ref Exception error){caught=true;}if(!caught||!target.EqualsExact(&saved))return 4;}
                    if(!yearValid){bool caught=false;try{target=view.AddYears(delta);}catch(ref Exception error){caught=true;}if(!caught||!target.EqualsExact(&saved))return 5;}
                    if(!value.EqualsExact(&saved)||allocated!=released)return 6;
                }
                if(at!={{{hex.Length}}})return 7;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CalendarUtcBoundsErrorsAndChainedClampingUseExistingContracts(int optimization)
        =>Run(Support+"""
            int main(){
                readonly(char)*[4] inputs=["0001-02-01T00:00:00+14:00","0002-01-01T00:00:00+14:00",
                    "9999-10-31T23:59:59.9999999-14:00","9998-12-31T23:59:59.9999999-14:00"];
                int[4] shifts=[-1,-1,2,1];
                for(nuint i=0;i<4;i++){
                    DateTimeOffset source=DateTimeOffset::Parse(inputs[i]);DateTimeOffset saved=source;DateTimeOffset target=saved;
                    immutable(DateTimeOffset)* view=&source;DateTime wall=source.DateTime;
                    DateTime shifted=new();if(i%2==0)shifted=wall.AddMonths(shifts[i]);else shifted=wall.AddYears(shifts[i]);
                    long utc=shifted.Ticks-source.Offset.Ticks;if(utc>=0l&&utc<=3155378975999999999l)return 1;
                    bool caught=false;try{if(i%2==0)target=view.AddMonths(shifts[i]);else target=view.AddYears(shifts[i]);}
                    catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"DateTimeOffset clock or offset is out of range"))return 2;caught=true;}
                    if(!caught||!source.EqualsExact(&saved)||!target.EqualsExact(&saved))return 3;
                }
                DateTimeOffset start=DateTimeOffset::Parse("2024-01-31T12:34:56.1234567+05:45");DateTimeOffset saved=start;
                DateTimeOffset february=DateTimeOffset::Parse("2024-02-29T12:34:56.1234567+05:45");
                DateTimeOffset anniversary=DateTimeOffset::Parse("2025-02-28T12:34:56.1234567+05:45");
                int[2] extremes=[(-2147483647-1),2147483647];
                for(nuint kind=0;kind<2;kind++)for(nuint i=0;i<2;i++){
                    DateTimeOffset target=saved;bool caught=false;
                    try{if(kind==0)target=start.AddMonths(extremes[i]);else target=start.AddYears(extremes[i]);}
                    catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Date addition is outside the supported range"))return 4;caught=true;}
                    if(!caught||!target.EqualsExact(&saved)||!start.EqualsExact(&saved))return 5;
                }
                immutable(DateTimeOffset)* view=&start;int before=attempts;deny=true;
                DateTimeOffset next=view.AddMonths(1);DateTimeOffset repeated=next.AddMonths(1);DateTimeOffset direct=view.AddMonths(2);
                DateTimeOffset year=next.AddYears(1);DateTimeOffset same=view.AddMonths(0).AddYears(0);
                if(!next.EqualsExact(&february)||!year.EqualsExact(&anniversary)||!same.EqualsExact(&saved))return 6;
                if(repeated.DateTime.Date.Day!=29||direct.DateTime.Date.Day!=31)return 7;
                if(repeated.DateTime.TimeOfDay.Ticks!=start.DateTime.TimeOfDay.Ticks||direct.Offset.Ticks!=start.Offset.Ticks)return 8;
                deny=false;if(attempts!=before||!start.EqualsExact(&saved)||allocated!=released)return 9;return 0;
            }
            """,optimization);

    private static void WriteOutcome(BinaryWriter writer,Func<System.DateTimeOffset> operation)
    {
        System.DateTimeOffset result;
        try{result=operation();}catch(ArgumentException){writer.Write(false);writer.Write(0L);writer.Write(0L);return;}
        writer.Write(true);writer.Write(result.Ticks);writer.Write(result.UtcTicks);
    }
    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("date-time-offset-arithmetic.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"DateTimeOffset arithmetic exit {result.ExitCode}: {result.StandardError}");
    }
}

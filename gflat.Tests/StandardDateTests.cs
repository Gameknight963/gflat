using System.Globalization;

namespace gflat.Tests;

public sealed class StandardDateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CalendarAndFixedFormattingMatchDateOnlyAcrossGregorianBoundaries(int optimization)
    {
        var values=new SortedSet<int>{0,DateOnly.MaxValue.DayNumber};
        foreach(int year in new[]{1,4,99,100,399,400,401,1582,1600,1900,2000,2100,2400,9999})
            for(int month=1;month<=12;month++)
            {
                var first=new DateOnly(year,month,1);values.Add(first.DayNumber);
                values.Add(first.AddDays(DateTime.DaysInMonth(year,month)-1).DayNumber);
            }
        var random=new Random(55109);for(int i=0;i<120;i++)values.Add(random.Next(DateOnly.MaxValue.DayNumber+1));
        DateOnly[] dates=values.Select(DateOnly.FromDayNumber).ToArray();
        int[] offsets=dates.Select(date=>{int offset=random.Next(-3000,3001);return date.DayNumber+offset<0||date.DayNumber+offset>DateOnly.MaxValue.DayNumber?0:offset;}).ToArray();
        string code=$$"""
            using std; using std::Time; using std::Collections;int main(){char[13] output;nuint written=0;Date baseline=new();
             int[{{dates.Length}}] numbers=[{{string.Join(",",dates.Select(d=>d.DayNumber))}}];
             int[{{dates.Length}}] years=[{{string.Join(",",dates.Select(d=>d.Year))}}];
             int[{{dates.Length}}] months=[{{string.Join(",",dates.Select(d=>d.Month))}}];
             int[{{dates.Length}}] days=[{{string.Join(",",dates.Select(d=>d.Day))}}];
             int[{{dates.Length}}] limits=[{{string.Join(",",dates.Select(d=>DateTime.DaysInMonth(d.Year,d.Month)))}}];
             int[{{dates.Length}}] leap=[{{string.Join(",",dates.Select(d=>DateTime.IsLeapYear(d.Year)?1:0))}}];
             int[{{dates.Length}}] offsets=[{{string.Join(",",offsets)}}];
             readonly(char)* expectedText="{{string.Concat(dates.Select(d=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)))}}";
             for(nuint sample=0;sample<{{dates.Length}};sample++){
                 Date value=new(years[sample],months[sample],days[sample]);Date from=Date::FromDayNumber(numbers[sample]);
                 if(value.DayNumber!=numbers[sample]||value.Year!=years[sample]||value.Month!=months[sample]||value.Day!=days[sample])return 1;
                 if(!value.Equals(&from)||value.CompareTo(&from)!=0||value.GetHashCode()!=from.GetHashCode())return 2;
                 if(Date::IsLeapYear(years[sample])!=(leap[sample]==1)||Date::DaysInMonth(years[sample],months[sample])!=limits[sample])return 3;
                 Date later=value.AddDays(offsets[sample]);if(later.DayNumber!=numbers[sample]+offsets[sample]||value.DayNumber!=numbers[sample])return 4;
                 if(value.CompareTo(&baseline)<0||baseline.CompareTo(&value)>0)return 5;
                 readonly(char)* expected=expectedText+sample*10;
                 for(nuint capacity=0;capacity<=11;capacity++){
                  for(nuint i=0;i<13;i++)output[i]='Z';written=99;
                  bool success=value.TryFormat(&output[1],capacity,ref written);
                  if(success!=(capacity>=10))return 6;
                  if(!success){if(written!=0)return 7;for(nuint i=0;i<13;i++)if(output[i]!='Z')return 8;}
                  else{if(written!=10||output[0]!='Z'||output[11]!='Z'||output[12]!='Z')return 9;
                   for(nuint i=0;i<10;i++)if(output[i+1]!=expected[i])return 10;}
                 } }
             if(baseline.Year!=1||baseline.Month!=1||baseline.Day!=1||baseline.DayNumber!=0)return 11;return 0;}
            """;
        Run(code,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CalendarAndCallerWriterFormattingNeedNoHeapAllocation(int optimization)
        => Run("""
            using std; using std::Time; using std::Collections;int allocations=0;
            namespace Memory{public replace void*? Allocate(nuint count){allocations++;return null;} public replace void Free(void* storage) nothrow{allocations++;}}
            int main(){Date value=new(2000,2,29);Date next=value.AddDays(1);
                char[11] storage;BufferWriter output=new(storage,11);value.Format(&output);
                if(output.Length!=10||!CString::Equals(output.Data,"2000-02-29"))return 1;
                if(next.Year!=2000||next.Month!=3||next.Day!=1||allocations!=0)return 2;return 0;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidArgumentsThrowWithoutChangingValuesAndWriterInterpolationWorks(int optimization)
        => Run("""
            using std; using std::Time; using std::Collections;
            struct FailWriter:ITextWriter{public void Write(readonly(char)* data,nuint count){throw new Exception("writer failed");}}
            int main(){
                int caught=0;
                try{Date value=new(0,1,1);}catch(ref Exception error){caught++;}
                try{Date value=new(10000,1,1);}catch(ref Exception error){caught++;}
                try{Date value=new(2024,0,1);}catch(ref Exception error){caught++;}
                try{Date value=new(2024,13,1);}catch(ref Exception error){caught++;}
                try{Date value=new(2023,2,29);}catch(ref Exception error){caught++;}
                try{Date value=new(1900,2,29);}catch(ref Exception error){caught++;}
                try{Date value=new(2000,2,30);}catch(ref Exception error){caught++;}
                try{Date value=new(2024,4,31);}catch(ref Exception error){caught++;}
                try{Date value=new(2024,1,0);}catch(ref Exception error){caught++;}
                try{Date::IsLeapYear(-1);}catch(ref Exception error){caught++;}
                try{Date::DaysInMonth(2024,13);}catch(ref Exception error){caught++;}
                try{Date::FromDayNumber(-1);}catch(ref Exception error){caught++;}
                try{Date::FromDayNumber(3652059);}catch(ref Exception error){caught++;}
                Date first=new();Date last=new(9999,12,31);
                try{first.AddDays(-1);}catch(ref Exception error){caught++;}
                try{last.AddDays(1);}catch(ref Exception error){caught++;}
                try{first.AddDays(-2147483648);}catch(ref Exception error){caught++;}
                try{last.AddDays(2147483647);}catch(ref Exception error){caught++;}
                if(caught!=17||first.DayNumber!=0||last.DayNumber!=3652058)return 1;
                immutable(Date) date=new(2024,2,29);String text=s$"Date: {date}";
                if(!CString::Equals(text.Data,"Date: 2024-02-29"))return 2;
                String direct=new();date.Format(&direct);if(!CString::Equals(direct.Data,"2024-02-29"))return 3;
                FailWriter output=new();try{date.Format(&output);return 4;}catch(ref Exception error){}
                if(date.Year!=2024||date.Month!=2||date.Day!=29)return 5;
                nuint written=99;if(date.TryFormat((char*)1,0,ref written)||written!=0)return 6;
                HashMap<Date,int> map=new();Date key=new(2024,2,29);map.Add(key,17);
                if(map[&key]!=17)return 7;return 0;
            }
            """,optimization);

    private static void Run(string code,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",code));
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}

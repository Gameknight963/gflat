using System.Globalization;

namespace gflat.Tests;

public sealed class StandardIsoWeekTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FullGregorianCyclesAndAllYearBoundariesMatchIndependentIsoWeekOracles(int optimization)
    {
        var days=new SortedSet<int>();
        foreach((int first,int last) in new[]{(1,400),(9600,9999)})
            for(int day=new DateOnly(first,1,1).DayNumber;day<=new DateOnly(last,12,31).DayNumber;day++)days.Add(day);
        var random=new Random(865901);
        for(int sample=0;sample<512;sample++)days.Add(random.Next(DateOnly.MaxValue.DayNumber+1));
        foreach(int year in new[]{1,4,100,400,1582,1600,1900,2000,2020,2021,2100,2400,9999})
            for(int offset=0;offset<10;offset++){
                days.Add(new DateOnly(year,1,1).AddDays(offset).DayNumber);
                days.Add(new DateOnly(year,12,31).AddDays(-offset).DayNumber);
            }
        var reverse=new List<(int Year,int Week,int Weekday,int Day)>();
        for(int year=1;year<=9999;year++){
            int lastWeek=ISOWeek.GetWeeksInYear(year);
            foreach(int week in new[]{1,lastWeek})for(int weekday=0;weekday<7;weekday++){
                // ISOWeek.ToDateTime can overflow DateTime only for the two final
                // days of ISO year9999. Those are strict negatives below.
                if(year==9999&&week==lastWeek&&(weekday==0||weekday==6))continue;
                DateTime date=ISOWeek.ToDateTime(year,week,(System.DayOfWeek)weekday);
                reverse.Add((year,week,weekday,DateOnly.FromDateTime(date).DayNumber));
            }
        }
        for(int sample=0;sample<512;sample++){
            int year=random.Next(1,9999),week=random.Next(1,ISOWeek.GetWeeksInYear(year)+1),weekday=random.Next(7);
            reverse.Add((year,week,weekday,DateOnly.FromDateTime(ISOWeek.ToDateTime(year,week,(System.DayOfWeek)weekday)).DayNumber));
        }
        string path=Path.Combine(Path.GetTempPath(),"gflat_iso_week_"+Guid.NewGuid().ToString("N")+".bin");
        try{
            using(var writer=new BinaryWriter(File.Create(path))){
                writer.Write(days.Count);writer.Write(reverse.Count);
                for(int year=1;year<=9999;year++)writer.Write(ISOWeek.GetWeeksInYear(year));
                foreach(int day in days){
                    DateTime date=DateOnly.FromDayNumber(day).ToDateTime(TimeOnly.MinValue);
                    writer.Write(day);writer.Write(ISOWeek.GetYear(date));writer.Write(ISOWeek.GetWeekOfYear(date));writer.Write((int)date.DayOfWeek);
                }
                foreach(var item in reverse){writer.Write(item.Year);writer.Write(item.Week);writer.Write(item.Weekday);writer.Write(item.Day);}
            }
            Run($$"""
                using std;int allocations=0;
                namespace Memory{
                    public replace void*? Allocate(nuint count){allocations++;return null;}
                    public replace void Free(void* storage)nothrow{allocations++;}
                }
                extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* buffer,nuint size,nuint count,void* file);
                extern int ferror(void* file);extern int fclose(void* file);
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;defer{fclose(file!);}
                    int[2] counts;int[4] record;
                    if(fread((void*)&counts[0],4,2,file!)!=2)return 2;
                    if(counts[0]!={{days.Count}}||counts[1]!={{reverse.Count}}||sizeof(Date)!=sizeof(int))return 3;
                    for(int year=1;year<=9999;year++){
                        int expected=0;if(fread(&expected,4,1,file!)!=1)return 4;
                        int actual=Date::GetIsoWeeksInYear(year);
                        if(actual!=expected||(actual!=52&&actual!=53))return 5;
                    }
                    for(int sample=0;sample<counts[0];sample++){
                        if(fread((void*)&record[0],4,4,file!)!=4)return 6;
                        immutable(Date) value=Date::FromDayNumber(record[0]);
                        if(value.IsoWeekYear!=record[1]||value.IsoWeekOfYear!=record[2]||(int)value.DayOfWeek!=record[3])return 7;
                        Date restored=Date::FromIsoWeek(record[1],record[2],(DayOfWeek)record[3]);
                        if(restored.DayNumber!=record[0]||value.DayNumber!=record[0])return 8;
                    }
                    for(int sample=0;sample<counts[1];sample++){
                        if(fread((void*)&record[0],4,4,file!)!=4)return 9;
                        Date value=Date::FromIsoWeek(record[0],record[1],(DayOfWeek)record[2]);
                        if(value.DayNumber!=record[3]||value.IsoWeekYear!=record[0]||value.IsoWeekOfYear!=record[1]||(int)value.DayOfWeek!=record[2])return 10;
                    }
                    int extra=0;if(fread(&extra,1,1,file!)!=0||ferror(file!)!=0||allocations!=0)return 11;
                    return 0;
                }
                """,optimization);
        }finally{File.Delete(path);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidIsoCoordinatesThrowWithoutReplacingTheDestination(int optimization)
    {
        int[] years=[0,10000,int.MinValue,int.MaxValue];
        (int Year,int Week,int Weekday)[] invalid=[
            (0,1,1),(10000,1,1),(int.MinValue,1,1),(int.MaxValue,1,1),
            (2021,0,1),(2021,-1,1),(2021,int.MinValue,1),(2021,int.MaxValue,1),
            (2021,53,1),(2020,54,1),(2020,1,-1),(2020,1,7),(2020,1,int.MinValue),(2020,1,int.MaxValue),
            (9999,52,6),(9999,52,0)];
        Run($$"""
            using std;int main(){
                int[4] years=[{{string.Join(',',years)}}];
                int[{{invalid.Length*3}}] invalid=[{{string.Join(',',invalid.SelectMany(item=>new[]{item.Year,item.Week,item.Weekday}))}}];
                Date baseline=new(2024,2,29);int original=baseline.DayNumber;
                for(nuint sample=0;sample<4;sample++){
                    bool caught=false;try{Date::GetIsoWeeksInYear(years[sample]);}catch(ref Exception error){caught=true;}
                    if(!caught)return 1;
                }
                for(nuint sample=0;sample<{{invalid.Length}};sample++){
                    Date result=baseline;bool caught=false;nuint row=sample*3;
                    try{result=Date::FromIsoWeek(invalid[row],invalid[row+1],(DayOfWeek)invalid[row+2]);}
                    catch(ref Exception error){caught=true;}
                    if(!caught||result.DayNumber!=original||baseline.DayNumber!=original)return 2;
                }
                Date first=Date::FromIsoWeek(1,1,DayOfWeek::Monday);
                Date last=Date::FromIsoWeek(9999,52,DayOfWeek::Friday);
                if(first.DayNumber!=0||first.IsoWeekYear!=1||first.IsoWeekOfYear!=1)return 3;
                if(last.DayNumber!=3652058||last.IsoWeekYear!=9999||last.IsoWeekOfYear!=52)return 4;
                return 0;
            }
            """,optimization);
    }

    [Fact]
    public void IsoPropertiesAreNothrowWithoutStringGcOrLibcSourceDependencies()
    {
        string[] names=["Date.gf","DayOfWeek.gf","Text.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing.gf","Exception.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("iso-contract.gf","""
                using std;
                namespace Memory{public void*? Allocate(nuint count){return null;}public void Free(void* storage)nothrow{}}
                int Read(immutable(Date)* value)nothrow=>value.IsoWeekYear+value.IsoWeekOfYear;
                """));
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true});
        string[] definitions=ir.Split('\n').Where(line=>line.StartsWith("define ")&&(line.Contains("IsoWeekYear")||line.Contains("IsoWeekOfYear"))).ToArray();
        Assert.Equal(2,definitions.Length);Assert.All(definitions,line=>Assert.Contains("nounwind",line));
    }

    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static void Run(string code,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("iso-week.gf",code));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"ISO week exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

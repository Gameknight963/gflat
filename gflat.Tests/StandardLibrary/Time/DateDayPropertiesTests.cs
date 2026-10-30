using System.Text;

namespace gflat.Tests.Std.Time;

public sealed class DateDayPropertiesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DayPropertiesMatchDateOnlyForEveryDayOfAGregorianCycleAndLastYear(int optimization)
    {
        // A fixed native loop exercises all 146097 days without generating one
        // AST statement/expected-value literal per day. Compare every result,
        // rather than only checksums or a sparse set of leap-year examples.
        const int cycleDays=146097;
        int lastYearStart=new DateOnly(9999,1,1).DayNumber;
        string source=$$"""
            using Standard; using Standard::Time;extern int printf(readonly(char)* format,...);
            void Emit(int first,int count){
                for(int day=first;day<first+count;day++){
                    Date value=Date::FromDayNumber(day);immutable(Date)* view=&value;
                    printf("%d %d\n",(int)view.DayOfWeek,view.DayOfYear);
                }
            }
            int main(){Emit(0,{{cycleDays}});Emit({{lastYearStart}},365);return 0;}
            """;
        var result=CompilerTestHelper.Run(Sources(source),optimization);
        Assert.Equal(0,result.ExitCode);
        var expected=new StringBuilder();
        Append(0,cycleDays);Append(lastYearStart,365);
        Assert.Equal(expected.ToString(),result.StandardOutput.Replace("\r\n","\n"));
        void Append(int first,int count){
            for(int day=first;day<first+count;day++){
                DateOnly value=DateOnly.FromDayNumber(day);
                expected.Append((int)value.DayOfWeek).Append(' ').Append(value.DayOfYear).Append('\n');
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImmutableNonthrowingDayPropertiesDoNotAllocateOrChangeDateLayout(int optimization)
    {
        const string source="""
            using Standard; using Standard::Time;int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){allocations++;return null;}
                public replace void Free(void* storage) nothrow{allocations++;}
            }
            int Read(immutable(Date)* value)nothrow=>value.DayOfYear+(int)value.DayOfWeek;
            int main(){
                if(sizeof(Date)!=sizeof(int))return 1;
                if((int)DayOfWeek::Sunday!=0||(int)DayOfWeek::Monday!=1||(int)DayOfWeek::Tuesday!=2
                    ||(int)DayOfWeek::Wednesday!=3||(int)DayOfWeek::Thursday!=4
                    ||(int)DayOfWeek::Friday!=5||(int)DayOfWeek::Saturday!=6)return 2;
                immutable(Date) first=new();if(Read(&first)!=2)return 3;
                immutable(Date) leapEnd=new(2000,12,31);
                if(leapEnd.DayOfYear!=366||leapEnd.DayOfWeek!=DayOfWeek::Sunday)return 4;
                immutable(Date) last=new(9999,12,31);
                if(last.DayOfYear!=365||last.DayOfWeek!=DayOfWeek::Friday)return 5;
                if(first.DayNumber!=0||leapEnd.Year!=2000||last.Year!=9999||allocations!=0)return 6;
                return 0;
            }
            """;
        Assert.Equal(0,CompilerTestHelper.Run(Sources(source),optimization).ExitCode);
    }

    private static IEnumerable<SourceFile> Sources(string source)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("date-day-properties.gf",source));
}

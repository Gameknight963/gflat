namespace gflat.Tests;

public sealed class StandardDateArithmeticTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CalendarShiftsMatchDateOnlyIncludingClampingAndOverflow(int optimization)
    {
        var dates = new SortedSet<int> { DateOnly.MinValue.DayNumber, DateOnly.MaxValue.DayNumber };
        foreach (int year in new[] { 1, 4, 100, 400, 1900, 2000, 2023, 2024, 2100, 2400, 9999 })
            foreach (int month in new[] { 1, 2, 3, 12 })
            {
                dates.Add(new DateOnly(year, month, 1).DayNumber);
                dates.Add(new DateOnly(year, month, DateTime.DaysInMonth(year, month)).DayNumber);
            }
        var samples = new List<(int Number, int Offset, int Expected, bool Years)>();
        foreach (int number in dates)
            foreach (int offset in new[] { int.MinValue, -119988, -9999, -401, -13, -12, -1, 0, 1, 12, 13, 401, 9999, 119988, int.MaxValue })
            {
                Add(number, offset, false);
                Add(number, offset, true);
            }
        var random = new Random(93827);
        for (int i = 0; i < 300; i++)
        {
            int number = random.Next(DateOnly.MaxValue.DayNumber + 1);
            Add(number, random.Next(-119987, 119988), false);
            Add(number, random.Next(-9998, 9999), true);
        }
        // Exact longest valid shifts and their one-step-invalid neighbors.
        foreach (int months in new[] { 119986, 119987, 119988 }) Add(0, months, false);
        foreach (int months in new[] { -119986, -119987, -119988 }) Add(DateOnly.MaxValue.DayNumber, months, false);
        foreach (int years in new[] { 9998, 9999 }) Add(0, years, true);
        foreach (int years in new[] { -9998, -9999 }) Add(DateOnly.MaxValue.DayNumber, years, true);

        string code = $$"""
            using std;
            int main(){
                int[{{samples.Count}}] numbers=[{{string.Join(",",samples.Select(s=>s.Number))}}];
                int[{{samples.Count}}] offsets=[{{string.Join(",",samples.Select(s=>s.Offset))}}];
                int[{{samples.Count}}] expected=[{{string.Join(",",samples.Select(s=>s.Expected))}}];
                int[{{samples.Count}}] years=[{{string.Join(",",samples.Select(s=>s.Years?1:0))}}];
                for(nuint i=0;i<{{samples.Count}};i++){
                    Date source=Date::FromDayNumber(numbers[i]);
                    immutable(Date)* view=&source;
                    try{
                        Date result=new();
                        if(years[i]==1)result=view.AddYears(offsets[i]);
                        else result=view.AddMonths(offsets[i]);
                        if(expected[i]<0)return 1;
                        if(result.DayNumber!=expected[i])return 2;
                    }catch(ref Exception error){if(expected[i]>=0)return 3;}
                    if(source.DayNumber!=numbers[i])return 4;
                }
                return 0;
            }
            """;
        Run(code, optimization);

        void Add(int number, int offset, bool years)
        {
            int expected;
            try
            {
                DateOnly date = DateOnly.FromDayNumber(number);
                expected = (years ? date.AddYears(offset) : date.AddMonths(offset)).DayNumber;
            }
            catch (ArgumentOutOfRangeException) { expected = -1; }
            samples.Add((number, offset, expected, years));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SuccessfulCalendarShiftsAllocateNothingAndDoNotRememberClampedDays(int optimization)
        => Run("""
            using std;
            int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint bytes){allocations++;return null;}
                public replace void Free(void* address) nothrow{allocations++;}
            }
            int main(){
                immutable(Date) invoice=new(2024,1,31);
                Date february=invoice.AddMonths(1);
                Date march=invoice.AddMonths(2);
                Date repeated=february.AddMonths(1);
                if(february.Year!=2024||february.Month!=2||february.Day!=29)return 1;
                if(march.Month!=3||march.Day!=31||repeated.Month!=3||repeated.Day!=29)return 2;
                Date anniversary=february.AddYears(1);
                Date previous=february.AddYears(-1);
                if(anniversary.Year!=2025||anniversary.Month!=2||anniversary.Day!=28)return 3;
                if(previous.Year!=2023||previous.Month!=2||previous.Day!=28)return 4;
                Date unchanged=invoice.AddYears(0).AddMonths(0);
                if(unchanged.DayNumber!=invoice.DayNumber||invoice.Day!=31||allocations!=0)return 5;
                return 0;
            }
            """, optimization);

    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("calendar-shifts.gf", code));
        Assert.Equal(0, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }
}

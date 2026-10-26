namespace gflat.Tests;

public sealed class StandardDateLayoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CalendarBoundDoesNotOccupyPerDateStorage(int optimization)
    {
        const string code = """
            using std; using std::Time;
            int main(){
                if(sizeof(Date)!=sizeof(int)||alignof(Date)!=alignof(int))return 1;
                Date first=Date::FromDayNumber(0);
                Date last=Date::FromDayNumber(3652058);
                if(first.Year!=1||last.Year!=9999||last.Month!=12||last.Day!=31)return 2;
                int caught=0;
                try{Date::FromDayNumber(3652059);}catch(ref Exception error){caught++;}
                try{last.AddDays(1);}catch(ref Exception error){caught++;}
                try{first.AddDays(-1);}catch(ref Exception error){caught++;}
                if(caught!=3)return 3;
                return 0;
            }
            """;
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("date-layout.gf", code));
        Assert.Equal(0, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }
}

namespace gflat.Tests.Std.Time;

public sealed class DateFormattingAliasTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BackedReceiverOverlapFormatsSnapshotAndPreservesAllBytesOnShortCapacity(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("date-formatting-alias.gf", """
                using Standard; using Standard::Time;
                int main(){
                    readonly(char)*[3] expected=["0001-01-01","2000-02-29","9999-12-31"];
                    int[3] numbers=[0,730178,3652058];
                    for(nuint sample=0;sample<3;sample++){
                        // Each receiver is a real aligned Date in a 20-byte region.
                        // Destination offsets exercise both directions of overlap.
                        for(nuint offset=0;offset<=5;offset++){
                            for(nuint capacity=0;capacity<=11;capacity++){
                                Date[5] aligned;char* bytes=(&aligned[0]).reinterpret<char*>();
                                for(nuint i=0;i<20;i++)bytes[i]='!';
                                aligned[1]=Date::FromDayNumber(numbers[sample]);
                                char[20] before;for(nuint i=0;i<20;i++)before[i]=bytes[i];
                                nuint written=99;
                                bool success=aligned[1].TryFormat(bytes+offset,capacity,ref written);
                                if(success!=(capacity>=10))return 1;
                                if(!success){
                                    if(written!=0)return 2;
                                    for(nuint i=0;i<20;i++)if(bytes[i]!=before[i])return 3;
                                    if(aligned[1].DayNumber!=numbers[sample])return 4;
                                }
                                else{
                                    if(written!=10)return 5;
                                    for(nuint i=0;i<20;i++){
                                        if(i>=offset&&i<offset+10){if(bytes[i]!=expected[sample][i-offset])return 6;}
                                        else if(bytes[i]!=before[i])return 7;
                                    }
                                    // The overlapped receiver now holds output bytes;
                                    // only the preserved snapshot supplies expectations.
                                }
                            }
                        }
                    }
                    return 0;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}

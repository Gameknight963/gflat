using System.Globalization;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardDateParsingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StrictCountedAndCStringFormsMatchInvariantDateOnlyOracle(int optimization)
    {
        var cases = new List<string>();
        foreach (int year in new[] { 1, 4, 100, 400, 1582, 1900, 2000, 2024, 2100, 2400, 9999 })
            for (int month = 1; month <= 12; month++)
            {
                cases.Add(new DateOnly(year, month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                cases.Add(new DateOnly(year, month, DateTime.DaysInMonth(year, month)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
        var random = new Random(550371);
        for (int i = 0; i < 128; i++)
            cases.Add(DateOnly.FromDayNumber(random.Next(DateOnly.MaxValue.DayNumber + 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cases.AddRange(["0000-01-01", "10000-01-01", "2024-00-01", "2024-13-01", "2024-01-00", "2024-01-32",
            "1900-02-29", "2100-02-29", "2023-02-29", "2000-02-30", "2024-04-31", "2024-02-2x", "2024/02/29",
            "2024-2-29", "2024-02-9", "+2024-02-29", "-001-01-01", " 2024-02-29", "2024-02-29 ", "2024-02-29\n",
            "2024-02-29T00:00:00", "2024-02-29Z", "2024-02-29\0suffix", "2024-02-29\0", "2024-0\0-29"]);
        for (int count = 0; count < 10; count++) cases.Add("2024-02-29"[..count]);
        var offsets = new List<int>();
        string blob = "";
        foreach (string text in cases) { offsets.Add(blob.Length); blob += text + '\0'; }
        int[] counted = cases.Select(Expected).ToArray();
        int[] terminated = cases.Select(text => Expected(text.Split('\0')[0])).ToArray();
        string code = $$"""
            using std;
            int main(){
                readonly(char)* text={{Literal(blob)}};
                int[{{cases.Count}}] offsets=[{{string.Join(",",offsets)}}];
                int[{{cases.Count}}] counts=[{{string.Join(",",cases.Select(text=>text.Length))}}];
                int[{{cases.Count}}] counted=[{{string.Join(",",counted)}}];
                int[{{cases.Count}}] terminated=[{{string.Join(",",terminated)}}];
                for(nuint i=0;i<{{cases.Count}};i++){
                    readonly(char)* input=text+offsets[i];
                    Date result=new(2024,1,1);
                    bool valid=Date::TryParse(input,(nuint)counts[i],&result);
                    if(valid!=(counted[i]>=0))return 1;
                    int expected=0;if(valid)expected=counted[i];
                    if(result.DayNumber!=expected)return 2;
                    result=new(2024,1,1);
                    valid=Date::TryParse(input,&result);
                    if(valid!=(terminated[i]>=0))return 3;
                    expected=0;if(valid)expected=terminated[i];
                    if(result.DayNumber!=expected)return 4;
                    bool caught=false;
                    try{result=Date::Parse(input,(nuint)counts[i]);if(result.DayNumber!=counted[i])return 5;}
                    catch(ref Exception error){caught=true;}
                    if(caught!=(counted[i]<0))return 6;
                    caught=false;
                    try{result=Date::Parse(input);if(result.DayNumber!=terminated[i])return 7;}
                    catch(ref Exception error){caught=true;}
                    if(caught!=(terminated[i]<0))return 8;
                }
                return 0;
            }
            """;
        Run(code, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryByteAtEveryPositionUsesAsciiDigitsAndGregorianValidation(int optimization)
    {
        const string original = "2024-02-29";
        var expected = new List<int>();
        for (int at = 0; at < 10; at++)
            for (int value = 0; value < 256; value++)
            {
                char[] characters = original.ToCharArray(); characters[at] = (char)value;
                expected.Add(Expected(new string(characters)));
            }
        Run($$"""
            using std;
            int main(){
                int[2560] expected=[{{string.Join(",",expected)}}];
                readonly(char)* original="2024-02-29";
                char[12] input;
                for(nuint at=0;at<10;at++)for(int value=0;value<256;value++){
                    input[0]='L';input[11]='R';
                    for(nuint i=0;i<10;i++)input[i+1]=original[i];
                    input[at+1]=(char)value;
                    Date result=new(2024,1,1);
                    int number=expected[at*256+(nuint)value];
                    bool valid=Date::TryParse(&input[1],10,&result);
                    if(valid!=(number>=0))return 1;
                    if(!valid)number=0;
                    if(result.DayNumber!=number||input[0]!='L'||input[11]!='R')return 2;
                    for(nuint i=0;i<10;i++){
                        char before=original[i];if(i==at)before=(char)value;
                        if(input[i+1]!=before)return 3;
                    }
                }
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverlappingResultsAndWrongLengthsAreAllocationFree(int optimization)
        => Run("""
            using std;
            int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){allocations++;return null;}
                public replace void Free(void* storage){allocations++;}
            }
            int main(){
                long[4] aligned;
                char* bytes=(char*)&aligned[0];
                char[32] before;
                readonly(char)* text="2000-02-29";
                Date expected=new(2000,2,29);
                nuint size=(nuint)sizeof(Date);
                if(size>32||(nuint)alignof(Date)>(nuint)alignof(long))return 7;
                for(nuint input=0;input<=16;input++)for(nuint output=0;output<=32-size;output+=(nuint)alignof(Date)){
                    for(int variant=0;variant<4;variant++){
                        for(nuint i=0;i<32;i++)bytes[i]=(char)165;
                        for(nuint i=0;i<11;i++)bytes[input+i]=text[i];
                        if(variant==1||variant==3)bytes[input+9]='x';
                        for(nuint i=0;i<32;i++)before[i]=bytes[i];
                        Date* result=(Date*)(bytes+output);
                        bool valid=false;
                        if(variant<2)valid=Date::TryParse(bytes+input,10,result);
                        else valid=Date::TryParse(bytes+input,result);
                        bool success=variant==0||variant==2;
                        if(valid!=success)return 1;
                        int number=0;if(success)number=expected.DayNumber;
                        if(result.DayNumber!=number)return 2;
                        for(nuint i=0;i<32;i++){
                            if(i>=output&&i<output+size)continue;
                            if(bytes[i]!=before[i])return 3;
                        }
                    }
                }
                Date reset=new(2024,1,1);
                if(Date::TryParse((readonly(char)*)1,0,&reset)||reset.DayNumber!=0)return 4;
                reset=new(2024,1,1);
                nuint maximum=0;maximum--;
                if(Date::TryParse((readonly(char)*)1,maximum,&reset)||reset.DayNumber!=0)return 5;
                if(allocations!=0)return 6;
                return 0;
            }
            """, optimization);

    private static int Expected(string text)
        => Regex.IsMatch(text, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}\z") &&
            DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly value)
            ? value.DayNumber : -1;

    private static string Literal(string text)
        => "\"" + text.Replace("\\", "\\\\").Replace("\0", "\\0").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t").Replace("\"", "\\\"") + "\"";

    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("date-parsing.gf", code));
        Assert.Equal(0, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }
}

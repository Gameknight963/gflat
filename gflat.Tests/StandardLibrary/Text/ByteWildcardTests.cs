using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class ByteWildcardTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StandaloneMatcherAgreesWithIndependentDynamicProgrammingForEscapesAndBinaryBytes(int optimization)
    {
        var rows = new List<(byte[] Text, byte[] Pattern)>();
        byte[][] texts = Words([(byte)'a', (byte)'b'], 3).ToArray();
        byte[][] patterns = Words([(byte)'a', (byte)'b', (byte)'*', (byte)'?', (byte)'\\'], 3).ToArray();
        foreach (byte[] text in texts)
        foreach (byte[] pattern in patterns) rows.Add((text, pattern));
        foreach (int length in new[] { 0, 1, 7, 32, 128 })
        {
            byte[] text = [.. Enumerable.Repeat((byte)'a', length), (byte)'b'];
            rows.Add((text, "*aaaaab"u8.ToArray()));
            rows.Add((text, "*aaaaac"u8.ToArray()));
            rows.Add((text, "***a**?*b***"u8.ToArray()));
        }
        rows.Add(("a/b\\c"u8.ToArray(), "a?b\\\\c"u8.ToArray()));
        rows.Add(([0, 255, 128, 0], [(byte)'?', (byte)'*', 0]));
        rows.Add(([0], [(byte)'\\', 0]));
        rows.Add(([0, 1], [(byte)'\\', 0, (byte)'?']));
        byte[] everyByte = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        rows.Add((everyByte, Enumerable.Repeat((byte)'?', 256).ToArray()));
        rows.Add((everyByte, everyByte.SelectMany(value => new[] { (byte)'\\', value }).ToArray()));
        for (int value = 0; value < 256; value++)
        {
            rows.Add(([(byte)value], [(byte)'\\', (byte)value]));
            rows.Add(([(byte)(value ^ 128)], [(byte)'\\', (byte)value]));
        }
        var packet = new List<byte>();
        foreach (var row in rows)
        {
            Assert.True(row.Text.Length <= 512 && row.Pattern.Length <= 512);
            packet.Add((byte)row.Text.Length);packet.Add((byte)(row.Text.Length >> 8));
            packet.Add((byte)row.Pattern.Length);packet.Add((byte)(row.Pattern.Length >> 8));
            packet.Add(Oracle(row.Text, row.Pattern) ? (byte)1 : (byte)0);
            packet.AddRange(row.Text);packet.AddRange(row.Pattern);
        }
        string hex = Convert.ToHexString(packet.ToArray());
        string program = $$"""
            using Standard::Text;
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            byte ReadByte(readonly(char)* data,ref nuint at)nothrow{
                byte value=(byte)((Digit(data[at])<<4)|Digit(data[at+1]));at+=2;return value;
            }
            nuint ReadSize(readonly(char)* data,ref nuint at)nothrow{
                nuint low=(nuint)ReadByte(data,ref at);nuint high=(nuint)ReadByte(data,ref at);return low|(high<<8);
            }
            int main()nothrow{
                readonly(char)* records="{{hex}}";nuint at=0;
                for(int row=0;row<{{rows.Count}};row++){
                    nuint length=ReadSize(records,ref at);nuint count=ReadSize(records,ref at);
                    bool expected=ReadByte(records,ref at)!=0;
                    char[514] text;char[514] pattern;char[514] savedText;char[514] savedPattern;
                    for(nuint i=0;i<514;i++){text[i]=(char)165;pattern[i]=(char)90;}
                    for(nuint i=0;i<length;i++)text[i+1]=(char)ReadByte(records,ref at);
                    for(nuint i=0;i<count;i++)pattern[i+1]=(char)ReadByte(records,ref at);
                    for(nuint i=0;i<514;i++){savedText[i]=text[i];savedPattern[i]=pattern[i];}
                    if(MatchesWildcard(&text[1],length,&pattern[1],count)!=expected)return 1;
                    for(nuint i=0;i<514;i++)if(text[i]!=savedText[i]||pattern[i]!=savedPattern[i])return 2;
                }
                if(at!={{hex.Length}})return 3;
                readonly(char)* none=((nuint)1).reinterpret<readonly(char)*>();
                if(!MatchesWildcard(none,0,none,0)||!MatchesWildcard(none,0,"***",3))return 4;
                if(MatchesWildcard(none,0,"?",1)||MatchesWildcard(none,0,"\\",1))return 5;
                return 0;
            }
            """;
        string path = Path.Combine(AppContext.BaseDirectory, "Standard/core/Text/wildcard_matching.gf");
        string ir = Compiler.Emit([new SourceFile(path, File.ReadAllText(path)), new SourceFile("byte-wildcard-core.gf", program)],
            options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "invoke ", "personality", "__gflat_eh_", "gflat$Memory$", "@malloc", "@free", "__gflat_gc" })
            Assert.DoesNotContain(forbidden, ir);
        string definition = Assert.Single(ir.Split('\n'), line => line.StartsWith("define ", StringComparison.Ordinal)
            && line.Contains("$MatchesWildcard(", StringComparison.Ordinal));
        Assert.Contains("nounwind", definition);
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedAdaptersHandleEmptySpansOverlapAndCountedZerosWithoutAllocation(int optimization)
    {
        string program = """
            using Standard;using Standard::Text;using Standard::Memory;
            int attempts=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){attempts++;return null;}
                public replace void Free(void* storage)nothrow{}
            }
            int main(){
                Span<readonly(char)> empty=new();
                if(!empty.MatchesWildcard(empty)||!empty.MatchesWildcard("*")||!empty.MatchesWildcard("",0))return 1;
                if(empty.MatchesWildcard("?")||empty.MatchesWildcard("\\",1))return 2;
                char[8] backing=['a','*','b',(char)0,(char)255,'x','x','x'];
                char[8] original=['a','*','b',(char)0,(char)255,'x','x','x'];
                Span<char> all=new(backing,8);Span<readonly(char)> view=all.AsReadOnly();
                if(!view.Slice(0,3).MatchesWildcard(view.Slice(0,3)))return 3;
                if(!view.Slice(0,3).MatchesWildcard(&backing[1],1))return 4;
                if(view.Slice(0,5).MatchesWildcard(view.Slice(0,3)))return 5;
                if(!view.Slice(0,5).MatchesWildcard("a?b??")||view.Slice(0,5).MatchesWildcard("a?b?"))return 6;
                if(!MatchesWildcard(backing,"a?b")||!MatchesWildcard(backing,"a\\*b"))return 7;
                if(MatchesWildcard("A","a")||!MatchesWildcard("a/b","a?b")||!MatchesWildcard("a/b","*"))return 8;
                if(!MatchesWildcard("*?\\","\\*\\?\\\\"))return 9;
                for(nuint i=0;i<8;i++)if(backing[i]!=original[i])return 10;
                if(attempts!=0)return 11;return 0;
            }
            """;
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("byte-wildcard-adapters.gf", program));
        string ir = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    private static IEnumerable<byte[]> Words(byte[] alphabet, int maximumLength)
    {
        byte[][] previous = [[]];
        yield return [];
        for (int length = 1; length <= maximumLength; length++)
        {
            previous = previous.SelectMany(prefix => alphabet.Select(value => prefix.Append(value).ToArray())).ToArray();
            foreach (byte[] word in previous) yield return word;
        }
    }

    // Independently tokenize escapes, then solve prefix reachability by dynamic programming.
    private static bool Oracle(byte[] text, byte[] pattern)
    {
        var tokens = new List<(int Kind, byte Value)>();
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (value == (byte)'\\')
            {
                if (++index == pattern.Length) return false;
                tokens.Add((0, pattern[index]));
            }
            else tokens.Add((value == (byte)'*' ? 2 : value == (byte)'?' ? 1 : 0, value));
        }
        bool[,] reachable = new bool[text.Length + 1, tokens.Count + 1];reachable[0, 0] = true;
        for (int token = 1; token <= tokens.Count; token++)
        for (int count = 0; count <= text.Length; count++)
        {
            var current = tokens[token - 1];
            reachable[count, token] = current.Kind == 2
                ? reachable[count, token - 1] || count > 0 && reachable[count - 1, token]
                : count > 0 && reachable[count - 1, token - 1] && (current.Kind == 1 || text[count - 1] == current.Value);
        }
        return reachable[text.Length, tokens.Count];
    }
}

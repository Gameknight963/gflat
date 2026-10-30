using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class LastSplitOnceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedPatternsAndCharactersMatchHostLastIndexOf(int optimization)
    {
        List<(byte[] Text, byte[] Pattern)> cases = [([], []), ([], [61]), ([61], [61]),
            ("plain"u8.ToArray(), [61]), ("a=b=c"u8.ToArray(), [61]),
            ("::value::"u8.ToArray(), "::"u8.ToArray()), ("aaaaa"u8.ToArray(), "aaa"u8.ToArray()),
            ("ababa"u8.ToArray(), "aba"u8.ToArray()), ("abc"u8.ToArray(), []),
            ([255, 0, 128, 0, 128], [0, 128]), ([255, 1, 255], [255]), ([0], [0])];
        var random = new Random(71403);
        for (int i = 0; i < 64; i++)
        {
            byte[] text = new byte[random.Next(25)], pattern = new byte[random.Next(9)];
            random.NextBytes(text); random.NextBytes(pattern);
            if (pattern.Length <= text.Length && i % 2 == 0)
                pattern.CopyTo(text, random.Next(text.Length - pattern.Length + 1));
            cases.Add((text, pattern));
        }
        var code = new StringBuilder(Preamble + "int main(){char[28] data;char[12] pattern;\n");
        foreach (var item in cases)
        {
            int index = item.Text.AsSpan().LastIndexOf(item.Pattern);
            code.AppendLine($"{{data[0]='!';data[{item.Text.Length + 1}]='?';pattern[{item.Pattern.Length}]='\\0';");
            for (int i = 0; i < item.Text.Length; i++) code.AppendLine($"data[{i + 1}]=(char){item.Text[i]};");
            for (int i = 0; i < item.Pattern.Length; i++) code.AppendLine($"pattern[{i}]=(char){item.Pattern[i]};");
            code.AppendLine($$"""
                Span<readonly(char)> text=new(&data[1],{{item.Text.Length}});
                Span<readonly(char)> separator=new(pattern,{{item.Pattern.Length}});
                Span<readonly(char)> before=new("stale",5);Span<readonly(char)> after=new("stale",5);
                bool found=text.TrySplitLastOnce(pattern,{{item.Pattern.Length}},ref before,ref after);
                """);
            AppendChecks(code, item.Text, item.Pattern.Length, index);
            code.AppendLine("found=text.TrySplitLastOnce(separator,ref before,ref after);");
            AppendChecks(code, item.Text, item.Pattern.Length, index);
            int cLength = Array.IndexOf(item.Pattern, (byte)0);
            if (cLength < 0) cLength = item.Pattern.Length;
            code.AppendLine("found=text.TrySplitLastOnce(pattern,ref before,ref after);");
            AppendChecks(code, item.Text, cLength, item.Text.AsSpan().LastIndexOf(item.Pattern.AsSpan(0, cLength)));
            byte value = item.Pattern.Length == 0 ? (byte)61 : item.Pattern[0];
            code.AppendLine($"found=text.TrySplitLastOnce((char){value},ref before,ref after);");
            AppendChecks(code, item.Text, 1, item.Text.AsSpan().LastIndexOf(value));
            for (int i = 0; i < item.Text.Length; i++) code.AppendLine($"if(data[{i + 1}]!=(char){item.Text[i]})return 7;");
            for (int i = 0; i < item.Pattern.Length; i++) code.AppendLine($"if(pattern[{i}]!=(char){item.Pattern[i]})return 8;");
            code.AppendLine($"if(data[0]!='!'||data[{item.Text.Length + 1}]!='?'||pattern[{item.Pattern.Length}]!='\\0')return 9;}}");
        }
        code.AppendLine("if(allocations!=0)return 10;return 0;}");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MetadataAliasesAndEmptyCountsDoNotReadOrAllocate(int optimization)
        => Run(Preamble + """
            int main(){
                char[9] bytes=['a',':',':','b',':',':','c',':',':'];
                Span<readonly(char)> original=new(bytes,9);Span<readonly(char)> text=original;
                Span<readonly(char)> before=new();Span<readonly(char)> after=new();
                if(!text.TrySplitLastOnce("::",ref text,ref after)||text.Length!=7||after.Data!=null)return 1;
                text=original;if(!text.TrySplitLastOnce("::",2,ref before,ref text)||before.Length!=7||text.Data!=null)return 2;
                Span<readonly(char)> separator=new(&bytes[1],2);
                if(!original.TrySplitLastOnce(separator,ref separator,ref after)||separator.Length!=7)return 3;
                separator=new(&bytes[1],2);
                if(!original.TrySplitLastOnce(separator,ref before,ref separator)||before.Length!=7||separator.Data!=null)return 4;
                text=original;if(!text.TrySplitLastOnce(':',ref text,ref after)||text.Length!=8||after.Data!=null)return 5;
                text=original;if(!text.TrySplitLastOnce(':',ref before,ref text)||before.Length!=8||text.Data!=null)return 6;
                bytes[0]='A';if(before[0]!='A')return 7;
                Span<readonly(char)> empty=new();Span<readonly(char)> fake=new(((nuint)1).reinterpret<readonly(char)*>(),0);
                if(!empty.TrySplitLastOnce(((nuint)1).reinterpret<readonly(char)*>(),0,ref before,ref after)||before.Data!=null||after.Data!=null)return 8;
                if(!fake.TrySplitLastOnce(empty,ref before,ref after)||before.Data!=((nuint)1).reinterpret<readonly(char)*>()||after.Data!=null)return 9;
                if(!original.TrySplitLastOnce(fake,ref before,ref after)||before.Data!=original.Data||before.Length!=9||after.Data!=null)return 10;
                text=original;if(!text.TrySplitLastOnce(empty,ref text,ref after)||text.Length!=9||after.Data!=null)return 11;
                separator=empty;if(!original.TrySplitLastOnce(separator,ref before,ref separator)||before.Length!=9||separator.Data!=null)return 12;
                if(empty.TrySplitLastOnce(((nuint)1).reinterpret<readonly(char)*>(),1,ref before,ref after)||before.Data!=null||after.Data!=null)return 13;
                if(original.TrySplitLastOnce(((nuint)1).reinterpret<readonly(char)*>(),10,ref before,ref after)||before.Data!=null||after.Data!=null)return 14;
                if(original.TrySplitLastOnce(((nuint)1).reinterpret<readonly(char)*>(),(nuint)0-(nuint)1,ref before,ref after))return 15;
                if(fake.TrySplitLastOnce(':',ref before,ref after)||before.Data!=null||after.Data!=null)return 16;
                if(allocations!=0)return 17;return 0;
            }
            """, optimization);

    private const string Preamble = """
        using Standard; using Standard::Text; using Standard::Memory;int allocations=0;
        namespace Memory{
            public replace void*? Allocate(nuint count){allocations++;return null;}
            public replace void Free(void* pointer) nothrow{}
        }
        """;

    private static void AppendChecks(StringBuilder code, byte[] text, int width, int index)
    {
        if (index < 0)
        {
            code.AppendLine("if(found||before.Length!=0||after.Length!=0||before.Data!=null||after.Data!=null)return 1;");
            return;
        }
        int remaining = text.Length - index - width;
        code.AppendLine($"if(!found||before.Length!={index}||after.Length!={remaining})return 2;");
        // Empty patterns preserve the complete receiver metadata, even for a nonnull empty receiver.
        code.AppendLine(index == 0 && width != 0 ? "if(before.Data!=null)return 3;" : "if(before.Data!=&data[1])return 3;");
        code.AppendLine(remaining == 0 ? "if(after.Data!=null)return 4;" : $"if(after.Data!=&data[{index + width + 1}])return 4;");
        for (int i = 0; i < index; i++) code.AppendLine($"if(before[{i}]!=(char){text[i]})return 5;");
        for (int i = index + width; i < text.Length; i++) code.AppendLine($"if(after[{i - index - width}]!=(char){text[i]})return 6;");
    }

    private static void Run(string code, int optimization)
    {
        string[] files = ["Memory/Span.gf", "Collections/IEnumerable.gf", "Queries/Enumerable.gf",
            "Text/CString.gf", "Text/ByteSearch.gf", "Text/text_split_last_once_span.gf", "Text/text_split_last_once_pattern_span.gf"];
        var sources = files.Select(name => new SourceFile(name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", name))))
            .Append(new SourceFile("last-split-once.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}

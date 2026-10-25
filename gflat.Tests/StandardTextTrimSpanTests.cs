using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardTextTrimSpanTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryByteBoundaryUsesExactlyAsciiWhitespaceWithoutChangingStorage(int optimization)
        => Run("""
            using std; using std::Queries; using std::Collections;
            int main(){
                char[5] data;
                for(int value=0;value<256;value++){
                    data[0]=(char)value;data[1]=(char)value;data[2]='x';data[3]=(char)value;data[4]=(char)value;
                    Span<char> text=new(data,5);Span<readonly(char)> view=text.AsImmutable();
                    bool space=value==32||(value>=9&&value<=13);
                    nuint start=0;nuint end=5;if(space){start=2;end=3;}
                    Span<char> both=text.Trim();Span<char> left=text.TrimStart();Span<char> right=text.TrimEnd();
                    Span<readonly(char)> readBoth=view.Trim();Span<readonly(char)> readLeft=view.TrimStart();Span<readonly(char)> readRight=view.TrimEnd();
                    if(both.Length!=end-start||left.Length!=5-start||right.Length!=end)return 1;
                    if(readBoth.Length!=both.Length||readLeft.Length!=left.Length||readRight.Length!=right.Length)return 2;
                    if(both.Data!=&data[start]||left.Data!=&data[start]||right.Data!=&data[0])return 3;
                    if(readBoth.Data!=&data[start]||readLeft.Data!=&data[start]||readRight.Data!=&data[0])return 4;
                    for(nuint i=0;i<both.Length;i++)if(both[i]!=data[start+i]||readBoth[i]!=data[start+i])return 5;
                    for(int i=0;i<5;i++){
                        if(i==2){if(data[i]!='x')return 6;}
                        else if((byte)data[i]!=(byte)value)return 7;
                    }
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyAndWhitespaceViewsAreCanonicalAndMutableResultsStillAliasWithoutAllocation(int optimization)
        => Run("""
            using std; using std::Queries; using std::Collections;
            int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){allocations++;return null;}
                public replace void Free(void* data) nothrow{}
            }
            int main(){
                Span<char> empty=new();Span<readonly(char)> readEmpty=new();
                if(empty.Trim().Data!=null||empty.TrimStart().Data!=null||empty.TrimEnd().Data!=null)return 1;
                if(readEmpty.Trim().Data!=null||readEmpty.TrimStart().Data!=null||readEmpty.TrimEnd().Data!=null)return 2;
                char[6] spaces=[' ','\t','\r','\n',(char)11,(char)12];
                for(nuint count=0;count<=6;count++){
                    Span<char> text=new(spaces,count);Span<readonly(char)> view=text.AsImmutable();
                    if(text.Trim().Length!=0||text.TrimStart().Length!=0||text.TrimEnd().Length!=0)return 3;
                    if(view.Trim().Length!=0||view.TrimStart().Length!=0||view.TrimEnd().Length!=0)return 4;
                    if(text.Trim().Data!=null||text.TrimStart().Data!=null||text.TrimEnd().Data!=null)return 5;
                    if(view.Trim().Data!=null||view.TrimStart().Data!=null||view.TrimEnd().Data!=null)return 6;
                }
                char[5] data=[' ','x','\0','y',' '];Span<char> text=new(data,5);
                Span<char> trimmed=text.Trim();Span<readonly(char)> readonlyView=text.AsImmutable().Trim();
                if(trimmed.Length!=3||trimmed[1]!='\0'||readonlyView.Length!=3)return 7;
                trimmed[0]='z';trimmed[2]='!';
                if(data[1]!='z'||data[3]!='!'||readonlyView[0]!='z'||readonlyView[2]!='!')return 8;
                if(data[0]!=' '||data[2]!='\0'||data[4]!=' ')return 9;
                char[6] utf8=[' ',(char)194,(char)160,(char)194,(char)133,' '];
                Span<char> unicode=new(utf8,6);Span<char> kept=unicode.Trim();
                if(kept.Length!=4||(byte)kept[0]!=(byte)194||(byte)kept[3]!=(byte)133)return 10;
                return allocations;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TrimComposesWithBorrowedLineAndDelimiterViews(int optimization)
        => Run("""
            using std; using std::Queries; using std::Collections;
            int main(){
                readonly(char)* input=" name = value \r\n\tflag= yes\t\n \t = \t \n";
                Span<readonly(char)> text=new(input,36);int lineIndex=0;
                foreach(Span<readonly(char)> line in text.Lines()){
                    int partIndex=0;
                    foreach(Span<readonly(char)> part in line.Split('=')){
                        Span<readonly(char)> token=part.Trim();
                        if(lineIndex==0){
                            if(partIndex==0){if(token.Length!=4||token[0]!='n'||token[3]!='e')return 1;}
                            else if(token.Length!=5||token[0]!='v'||token[4]!='e')return 2;
                        }
                        if(lineIndex==1){
                            if(partIndex==0){if(token.Length!=4||token[0]!='f'||token[3]!='g')return 3;}
                            else if(token.Length!=3||token[0]!='y'||token[2]!='s')return 4;
                        }
                        if(lineIndex==2&&token.Length!=0)return 5;
                        partIndex++;
                    }
                    if(partIndex!=2)return 6;lineIndex++;
                }
                if(lineIndex!=3)return 7;return 0;
            }
            """, optimization);

    [Fact]
    public void ReadonlyElementsRemainReadonlyAfterEveryTrimOperation()
    {
        foreach (string method in new[] { "Trim", "TrimStart", "TrimEnd" })
        {
            var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(Sources($$"""
                using std; using std::Queries; using std::Collections;
                int main(){Span<readonly(char)> text=new(" x ",3);text.{{method}}()[0]='!';return 0;}
                """)));
            Assert.Contains("read", error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<SourceFile> Sources(string program)
    {
        string[] files = ["Span.gf", "Ascii.gf", "TextTrimSpan.gf", "TextParts.gf", "TextPartsSpan.gf", "TextLines.gf", "TextLinesSpan.gf",
            "Collections/IEnumerable.gf", "Collections/IEnumerator.gf", "Queries/Enumerable.gf"];
        return files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("main.gf", program));
    }
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"Native text trim exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}

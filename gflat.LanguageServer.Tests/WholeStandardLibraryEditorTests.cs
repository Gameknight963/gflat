using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public class WholeStandardLibraryEditorTests
{
    [Fact]
    public void BuildingTheLibraryEditorIndexStaysWithinAnAllocationBudget()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "StandardLibrary");
        var sources = Directory.GetFiles(directory, "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        sources.Add(new SourceFile(Path.GetFullPath("std-editor/allocation.gf"), """
            using std;
            int main() {
                Vector<int> values = new(); values.Add(42);
                HashMap<String,int> map = new(); map.Add(s"key",42);
                String key = s"key"; map.Find(&key);
                return values[0] - 42;
            }
            """));
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var model = new EditorModel(snapshot, sources);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(model);
        // The previous index allocated almost 900 MB through repeated symbol
        // sorting and qualified-name construction. Leave ample room for runtime
        // differences and library growth without using a wall-clock assertion.
        Assert.InRange(allocated, 0L, 512L * 1024 * 1024);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualLibrarySymbolsRemainNavigableEvenWithAnErrorInAnotherFile(bool brokenFile)
    {
        string directory=Path.Combine(AppContext.BaseDirectory,"StandardLibrary");
        var sources=Directory.GetFiles(directory,"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).ToList();
        const string text="""
            using std;
            int main() {
                LinkedList<int> list=new();
                LinkedList<int>::Node* node=list.AddLast(42);
                String text=s"ad"; text.Insert(1,"bc");
                HashMap<String,int> map=new(); map.Add(s"key",42);
                String key=s"key"; map.Find(&key);
                CRC32 checksum=new(); checksum.Append("123456789",9);
                Utf8Decoder decoder=new(); uint scalar=0u; nuint consumed=0;
                decoder.Read("a",1,true,&scalar,&consumed);
                Utf8Scalars characters=new(text.Data,text.Length);
                Utf8Scalars::Enumerator iterator=characters.GetEnumerator();
                ITextWriter* output=Console::Out;
                readonly(char)*? setting=Environment::GetVariable("GFLAT_TEST_UNSET");
                byte[4] bytes; BinaryPrimitives::TryWriteUInt32BigEndian(bytes,4,42u);
                BitSet flags=new(65); flags.SetRange(1,63,true);
                uint bits=BitConverter::FloatToUInt32Bits(1.0f);
                int[3] numbers=[1,2,3]; Span<int> values=new(numbers,3);
                PseudoRandom random=new(42ul); Shuffle<int>(values,ref random);
                SortedMap<int,String> ordered=new(Comparer<int>::Compare);
                ordered.Add(3,s"three"); int lookup=3; ordered.FindReadOnly(&lookup);
                SortedMap<int,String>::Enumerator orderedItems=ordered.GetEnumerator();
                ushort[2] units=[(ushort)65,(ushort)0];
                std::Utf16::TryDecode(units,1,&scalar,&consumed);
                nuint unitCount=0; UnicodeTranscoding::TryGetUtf16Length(text.Data,text.Length,ref unitCount);
                char[21] digits; nuint digitsWritten=0;
                IntegerFormatting::TryFormat(42,digits,21,ref digitsWritten);
                File temporary=File::OpenTemporary(); temporary.Close();
                return node.Value-42;
            }
            """;
        var source=new SourceFile(Path.GetFullPath("std-editor/complete.gf"),text);
        sources.Add(source);
        if(brokenFile) sources.Add(new SourceFile(Path.GetFullPath("std-editor/broken.gf"),"void Broken(){does_not_exist();}"));
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        if(brokenFile) Assert.Contains(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        else Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        var sourceTokens=Lexer.Tokenize(source);
        foreach(string member in new[]{"AddLast","Insert","Find","Append","Read","GetEnumerator","Out","GetVariable","TryWriteUInt32BigEndian","SetRange","FloatToUInt32Bits","Shuffle","FindReadOnly","TryDecode","TryGetUtf16Length","TryFormat","OpenTemporary","Value"})
        {
            int offset=sourceTokens.Last(token=>token.Text==member).Start;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.NotEqual(JsonValueKind.Null,hover.ValueKind);
            string signature=hover.GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(member,signature);
            Assert.DoesNotContain('$',signature);
            Assert.NotNull(model.Definition(source.Path,offset));
        }
    }
}

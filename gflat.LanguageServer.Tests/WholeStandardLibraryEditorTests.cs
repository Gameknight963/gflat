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
            bool Contains(int value) => value == 42;
            bool RemoveZero(readonly(int)* value) => *value == 0;
            int main() {
                LinkedList<int> list=new();
                LinkedList<int>::Node* node=list.AddLast(42);
                String text=s"ad"; text.Insert(1,"bc");
                Span<readonly(char)> textView=text.AsImmutable(); Span<char> writableText=text.AsSpan();
                Span<readonly(char)> shallowTextView=text.AsReadOnly();
                HashMap<String,int> map=new(); map.Add(s"key",42);
                String key=s"key"; map.Find(&key);
                map.FindReadOnly(&key); map.FindImmutable(&key);
                HashMap<String,int>::Entry detached=map.Extract(&key);
                detached.ValueReadOnly; detached.ValueImmutable;
                CRC32 checksum=new(); checksum.Append("123456789",9);
                Utf8Decoder decoder=new(); uint scalar=0u; nuint consumed=0;
                decoder.Read("a",1,true,&scalar,&consumed);
                Utf8Scalars characters=new(text.Data,text.Length);
                Utf8Scalars::Enumerator iterator=characters.GetEnumerator();
                ITextWriter* output=Console::Out;
                output.WriteJsonString(text.Data,text.Length);
                char[8] jsonText; nuint jsonLength=0;
                JsonString::TryUnquote("\"value\"",7,jsonText,8,ref jsonLength);
                JsonReader document=new("{\"ok\":true}",11);
                while(document.Read()) {
                    JsonTokenKind token=document.Kind;
                    nuint offset=document.TokenOffset; nuint size=document.TokenLength;
                    nuint depth=document.Depth;
                }
                document.Reset();
                document.Read(); document.Skip();
                String serialized=new(); JsonWriter jsonOutput=new(&serialized);
                jsonOutput.WriteStartObject(); jsonOutput.WritePropertyName("value");
                jsonOutput.WriteNumber(42); jsonOutput.WriteEndObject(); jsonOutput.Complete();
                String prettyText=new(); JsonWriter pretty=new(&prettyText,true);
                pretty.WriteStartArray(); pretty.WriteEndArray(); pretty.Complete();
                String compactText=new(); JsonText::Minify(" [ true ] ",10,&compactText);
                nuint last=text.LastIndexOf('d');
                text.TrimStart(); text.TrimEnd(); text.PadLeft(8); text.PadRight(10);
                HashSet<int> first=new(); HashSet<int> second=new();
                first.UnionWith(&second); first.ExceptWith(&second);
                first.IntersectWith(&second); first.SymmetricExceptWith(&second);
                readonly(char)*? setting=Environment::GetVariable("GFLAT_TEST_UNSET");
                byte[4] bytes; BinaryPrimitives::TryWriteUInt32BigEndian(bytes,4,42u);
                float number=0.0f; BinaryPrimitives::TryReadFloatLittleEndian(bytes,4,ref number);
                BinaryBufferWriter binary=new(bytes,4); binary.TryWriteFloatBigEndian(number);
                binary.Reset(); binary.TryWriteBytes(bytes,4);
                BinaryBufferReader binaryInput=new(bytes,4); binaryInput.TryReadBytes(bytes,4);
                char[3] urlText; char[2] urlBytes; nuint urlCount=0;
                Base64Url::TryGetEncodedLength(1,ref urlCount);
                Base64Url::TryEncode("x",1,urlText,3,ref urlCount);
                Base64Url::TryGetDecodedLength(urlText,urlCount,ref urlCount);
                Base64Url::TryDecode("eA",2,urlBytes,2,ref urlCount);
                nuint encoded=0; VarInt::TryWriteUInt32(bytes,4,42u,ref encoded);
                uint zigzag=VarInt::ZigZagEncode(-42); int signedNumber=VarInt::ZigZagDecode(zigzag);
                BitSet flags=new(65); flags.SetRange(1,63,true);
                bool any=flags.HasAnySet(); bool all=flags.HasAllSet();
                flags.ShiftLeft(1); flags.ShiftRight(32);
                uint bits=BitConverter::FloatToUInt32Bits(1.0f);
                int[3] numbers=[1,2,3]; Span<int> values=new(numbers,3);
                int searchKey=2; nuint found=BinarySearch<int>(values.AsImmutable(),&searchKey,Comparer<int>::Compare);
                Rotate<int>(values,1);
                Vector<int> removedRange=new(); removedRange.Add(1); removedRange.RemoveRange(0,1); removedRange.ShrinkToFit(); removedRange.RemoveAll(RemoveZero);
                PseudoRandom random=new(42ul); Shuffle<int>(values,ref random);
                values.Fill(7);
                float fraction=random.NextFloat();
                float root=Math::Sqrt(4.0f);
                float signed=Math::CopySign(root,-0.0f);
                extralong wide=(extralong)1<<100;
                extralong wideMinimum=Math::Min(wide+1,wide+2);
                extralong wideMaximum=Math::Max(wide+1,wide+2);
                extralong wideClamped=Math::Clamp(wide+3,wide+1,wide+2);
                extralong wideAbsolute=Math::Abs(-wide);
                Date calendar=new(2024,2,29); Date tomorrow=calendar.AddDays(1);
                Date anniversary=calendar.AddYears(1); Date nextMonth=calendar.AddMonths(1);
                DayOfWeek weekday=calendar.DayOfWeek; int ordinalDay=calendar.DayOfYear;
                int calendarDay=tomorrow.DayNumber; int monthDays=Date::DaysInMonth(2024,2);
                String dated=s$"Next day: {tomorrow}";
                Unique<int> owner=new(new* int(41));
                readonly(Unique<int>)* ownerView=&owner; ownerView.Value++;
                immutable(Unique<int>)* ownerDeep=&owner; int protectedValue=ownerDeep.ValueImmutable;
                bool emptyOwner=owner.IsEmpty;
                Vector<String> parts=new(); parts.Add(s"left"); parts.Add(s"right");
                String joined=parts.AsImmutable().Join(",");
                Date parsedDate=Date::Parse("2024-02-29");
                Date::TryParse("2024-02-29",&parsedDate);
                ushort[1] nativeText=[(ushort)65];
                String utf8Text=UnicodeText::FromUtf16(nativeText,1);
                Vector<ushort> utf16Text=UnicodeText::ToUtf16(utf8Text.Data,utf8Text.Length);
                SortedMap<int,String> ordered=new(Comparer<int>::Compare);
                ordered.Add(3,s"three"); int lookup=3; ordered.FindReadOnly(&lookup);
                readonly(SortedMap<int,String>)* orderedRead=&ordered; orderedRead.FindReadOnly(&lookup);
                immutable(SortedMap<int,String>)* orderedDeep=&ordered; orderedDeep.FindImmutable(&lookup);
                SortedMap<int,String>::Enumerator orderedItems=ordered.GetEnumerator();
                SortedMap<int,String>::Entry transferred=ordered.Extract(&lookup);
                transferred.ValueReadOnly; transferred.ValueImmutable;
                SortedSet<int> unique=new(Comparer<int>::Compare); unique.Add(42);
                SortedSet<int>::Enumerator uniqueItems=unique.GetEnumerator();
                ushort[2] units=[(ushort)65,(ushort)0];
                std::Utf16::TryDecode(units,1,&scalar,&consumed);
                Utf16Decoder wideDecoder=new(); wideDecoder.Read(units,1,true,&scalar,&consumed); wideDecoder.Reset();
                uint reversed=BitOperations::ReverseEndianness(42u);
                bool absolute=Paths::IsFullyQualified("/tmp/file",PathStyle::Unix);
                bool localResult=Contains(42);
                Deque<int> ends=new(); ends.AddFirst(1); ends.AddLast(2); int removed=ends.RemoveLast(); ends.ShrinkToFit();
                Queue<int> pending=new(); pending.Enqueue(42); pending.ShrinkToFit();
                TextLines lines=new(new Span<readonly(char)>("a\r\nb",4));
                TextLines::Enumerator lineIterator=lines.GetEnumerator();
                nuint cLength=0; CString::TryGetLength("text",5,ref cLength);
                byte[2] pattern=[(byte)0,(byte)0]; ByteSearch::LastIndexOf(bytes,4,pattern,2);
                nuint unitCount=0; UnicodeTranscoding::TryGetUtf16Length(text.Data,text.Length,ref unitCount);
                char[21] digits; nuint digitsWritten=0;
                IntegerFormatting::TryFormat(42,digits,21,ref digitsWritten);
                IntegerFormatting::TryFormatHex(42,digits,21,ref digitsWritten,8,false);
                BufferWriter textOutput=new(digits,21); textOutput.TryWrite("ok");
                uint parsed=0u; TryParseHexUInt("0x2a",&parsed);
                bool enabled=false; TryParseBool("True",&enabled);
                char[8] terminated; CString::TryCopy("ok",terminated,8);
                Arena scratch=new(); scratch.AllocateZeroed(16,8);
                File temporary=File::OpenTemporary(); temporary.Close();
                int next=Console::PeekByte();
                Console::ClearInputError();
                return node.Value-42;
            }
            """;
        var source=new SourceFile(Path.GetFullPath("std-editor/complete.gf"),text);
        sources.Add(source);
        string brokenPath=Path.GetFullPath("std-editor/broken.gf");
        if(brokenFile) sources.Add(new SourceFile(brokenPath,"void Broken(){does_not_exist();}"));
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        if(brokenFile) {
            Assert.Contains(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
            Assert.All(snapshot.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error),d=>Assert.Equal(brokenPath,d.FilePath));
        }
        else Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var model=new EditorModel(snapshot,sources);
        var sourceTokens=Lexer.Tokenize(source);
        foreach(string member in new[]{"AddDays","DayNumber","DaysInMonth","AsSpan","AsReadOnly","AsImmutable","RemoveAll","ShrinkToFit","ZigZagEncode","ZigZagDecode","WriteStartObject","WritePropertyName","WriteNumber","WriteEndObject","Complete","Kind","TokenOffset","TokenLength","Depth","Fill","RemoveRange","CopySign","TryParseBool","TryCopy","Sqrt","Min","Max","Clamp","Abs","NextFloat","TryWrite","AllocateZeroed","ClearInputError","TryParseHexUInt","BinarySearch","HasAnySet","HasAllSet","TryUnquote","TryFormatHex","TryReadFloatLittleEndian","TryWriteFloatBigEndian","Extract","PadLeft","PadRight","TryWriteUInt32","PeekByte","Rotate","TrimStart","TrimEnd","WriteJsonString","UnionWith","ExceptWith","AddFirst","RemoveLast","TryGetLength","LastIndexOf","AddLast","Insert","Find","Append","Read","Reset","ReverseEndianness","IsFullyQualified","GetEnumerator","Out","GetVariable","TryWriteUInt32BigEndian","SetRange","FloatToUInt32Bits","Shuffle","FindReadOnly","FindImmutable","ValueReadOnly","ValueImmutable","TryDecode","TryGetUtf16Length","TryFormat","OpenTemporary","Value"})
        foreach(var token in sourceTokens.Where(token=>token.Text==member))
        {
            int offset=token.Start;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.NotEqual(JsonValueKind.Null,hover.ValueKind);
            string signature=hover.GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains(member,signature);
            Assert.DoesNotContain('$',signature);
            Assert.NotNull(model.Definition(source.Path,offset));
        }
        foreach(string member in new[]{"IsEmpty","AddMonths","AddYears","DayOfWeek","DayOfYear","Join","Parse","TryParse","FromUtf16","ToUtf16","Skip","IntersectWith","SymmetricExceptWith","Minify","ShiftLeft","ShiftRight","TryReadBytes","TryWriteBytes","Base64Url","TryGetEncodedLength","TryGetDecodedLength"})
        {
            int offset=sourceTokens.Last(token=>token.Text==member).Start;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.NotNull(model.Definition(source.Path,offset));
        }
        int localCall=sourceTokens.Last(token=>token.Text=="Contains").Start;
        var localHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,localCall),Protocol.JsonOptions);
        Assert.Contains("Contains(int value)",localHover.GetProperty("contents").GetProperty("value").GetString());
    }
}

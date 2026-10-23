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
            Unique<int> FoldOwned(Unique<int> total,int value) { total.Value+=value; return move total; }
            int main() {
                LinkedList<int> list=new();
                LinkedList<int>::Node* node=list.AddLast(42);
                var linkedQuery=list.AsEnumerable().Take(1);
                String text=s"ad"; text.Insert(1,"bc");
                Span<readonly(char)> textView=text.AsImmutable(); Span<char> writableText=text.AsSpan();
                Span<readonly(char)> shallowTextView=text.AsReadOnly();
                Span<readonly(char)> trimmedText=textView.Trim();
                bool booleanValue=false;
                bool booleanParsed=trimmedText.TryParseBool(&booleanValue);
                bool requiredBoolean=trimmedText.ParseBool();
                String resizedText=new(); resizedText.Resize(4,'x');
                String ownedQuotedText=JsonStringText::Quote(trimmedText);
                String ownedUnquotedText=JsonStringText::Unquote(ownedQuotedText.AsReadOnly());
                BigInteger arbitraryInteger=new(42l);
                BigInteger arbitraryFactor=BigInteger::Parse("1000000000000000000000000000000");
                BigInteger arbitraryProduct=arbitraryInteger.Multiply(&arbitraryFactor);
                BigInteger arbitrarySum=arbitraryProduct.Add(&arbitraryInteger);
                BigInteger arbitraryDifference=arbitrarySum.Subtract(&arbitraryInteger);
                int arbitrarySign=arbitraryDifference.Sign;
                bool prefix=trimmedText.StartsWith("a"); bool suffix=trimmedText.EndsWith("d",1);
                Span<char> trimmedWritable=writableText.TrimStart().TrimEnd();
                Span<readonly(char)> textKey=new(); Span<readonly(char)> textValue=new();
                bool hasSeparator=trimmedText.TrySplitOnce('=',ref textKey,ref textValue);
                SpanChunks<readonly(char)> textChunks=trimmedText.Chunks(2);
                SpanChunks<readonly(char)>::Enumerator chunkIterator=textChunks.GetEnumerator();
                immutable(TextParts) textParts=trimmedText.Split(',');
                TextParts::Enumerator partIterator=textParts.GetEnumerator();
                immutable(TextLines) textLines=new(trimmedText);
                TextLines::Enumerator immutableLines=textLines.GetEnumerator();
                HashMap<String,int> map=new(); map.Add(s"key",42);
                String key=s"key"; map.Find(&key);
                map.FindReadOnly(&key); map.FindImmutable(&key);
                HashMap<String,int>::Entry detached=map.Extract(&key);
                detached.ValueReadOnly; detached.ValueImmutable;
                CRC32 checksum=new(); checksum.Append("123456789",9);
                SHA256 contentHash=new();
                bool hashed=contentHash.TryAppend(text.Data,text.Length);
                byte[32] digest=contentHash.GetHash();
                contentHash.Reset();
                byte[32] fileDigest=FileHash::SHA256("input.bin");
                Utf8Decoder decoder=new(); uint scalar=0u; nuint consumed=0;
                decoder.Read("a",1,true,&scalar,&consumed);
                Utf8Scalars characters=new(text.Data,text.Length);
                Utf8Scalars::Enumerator iterator=characters.GetEnumerator();
                immutable(Utf8Scalars) protectedCharacters=characters;
                var scalarQuery=protectedCharacters.AsEnumerable().Where((uint scalar) => scalar != 0u).Take(2);
                ushort[2] scalarUnits=[(ushort)65,(ushort)90];
                immutable(Utf16Scalars) wideCharacters=new(scalarUnits,2);
                var wideScalarQuery=wideCharacters.AsEnumerable().Select((uint scalar) => (int)scalar);
                ITextWriter* output=Console::Out;
                Base64Encoder encodedOutput=new(output);
                encodedOutput.Write(text.Data,text.Length);
                encodedOutput.Complete();
                Base64Decoder decodedOutput=new(output);
                decodedOutput.Write("eA==",4);
                decodedOutput.Complete();
                CountingWriter measuredOutput=new();
                measuredOutput.TryWrite(text.Data,text.Length);
                nuint measuredLength=measuredOutput.Length;
                measuredOutput.Clear();
                bool hasInputLine=Console::TryReadLine(&measuredOutput);
                CsvWriter csvOutput=new(output);
                csvOutput.WriteField("a,b",3);
                csvOutput.WriteEndRecord();
                output.WriteJsonString(text.Data,text.Length);
                char[8] jsonText; nuint jsonLength=0;
                JsonString::TryUnquote("\"value\"",7,jsonText,8,ref jsonLength);
                JsonReader document=new("{\"ok\":true}",11);
                readonly(char)* csvText="\"a,b\",42\n";
                CsvReader table=new(csvText,CString::Length(csvText));
                char[16] csvValue; nuint csvWritten=0;
                while(table.Read()) {
                    Span<readonly(char)> rawField=table.RawField;
                    nuint fieldOffset=table.FieldOffset; nuint fieldLength=table.ValueLength;
                    bool quotedField=table.IsQuoted; bool recordEnd=table.EndOfRecord;
                    table.TryCopyValue(csvValue,16,ref csvWritten);
                }
                table.Reset();
                while(document.Read()) {
                    JsonTokenKind token=document.Kind;
                    nuint offset=document.TokenOffset; nuint size=document.TokenLength;
                    nuint depth=document.Depth;
                    Span<readonly(char)> rawToken=document.RawToken;
                    bool knownName=document.ValueTextEquals("ok",2);
                }
                document.Reset();
                document.Read(); document.Skip();
                String serialized=new(); JsonWriter jsonOutput=new(&serialized);
                jsonOutput.WriteStartObject(); jsonOutput.WritePropertyName("value");
                jsonOutput.WriteNumber(42); jsonOutput.WritePropertyName("wide");
                jsonOutput.WriteNumber((extralong)1 << 100); jsonOutput.WriteEndObject(); jsonOutput.Complete();
                String prettyText=new(); JsonWriter pretty=new(&prettyText,true);
                pretty.WriteStartArray(); pretty.WriteEndArray(); pretty.Complete();
                String compactText=new(); JsonText::Minify(" [ true ] ",10,&compactText);
                nuint last=text.LastIndexOf('d');
                text.TrimStart(); text.TrimEnd(); text.PadLeft(8); text.PadRight(10);
                HashSet<int> first=new(); HashSet<int> second=new();
                first.UnionWith(&second); first.ExceptWith(&second);
                first.IntersectWith(&second); first.SymmetricExceptWith(&second);
                readonly(char)*? setting=Environment::GetVariable("GFLAT_TEST_UNSET");
                bool missingSetting=CString::IsNullOrEmpty(setting);
                nuint compositeHash=Hashing::Combine(Hashing::Integer(1ul),Hashing::Integer(2ul));
                byte[4] bytes; BinaryPrimitives::TryWriteUInt32BigEndian(bytes,4,42u);
                float number=0.0f; BinaryPrimitives::TryReadFloatLittleEndian(bytes,4,ref number);
                BinaryBufferWriter binary=new(bytes,4); binary.TryWriteFloatBigEndian(number);
                binary.Reset(); binary.TryWriteBytes(bytes,4);
                BinaryBufferReader binaryInput=new(bytes,4); binaryInput.TryReadBytes(bytes,4);
                byte[16] wideBytes; extralong wideNumber=(extralong)42;
                BinaryBufferWriter wideOutput=new(wideBytes,16); wideOutput.TryWriteExtraLongBigEndian(wideNumber);
                BinaryBufferReader wideInput=new(wideBytes,16); wideInput.TryReadExtraLongBigEndian(ref wideNumber);
                char[3] urlText; char[2] urlBytes; nuint urlCount=0;
                Base64Url::TryGetEncodedLength(1,ref urlCount);
                Base64Url::TryEncode("x",1,urlText,3,ref urlCount);
                Base64Url::TryGetDecodedLength(urlText,urlCount,ref urlCount);
                Base64Url::TryDecode("eA",2,urlBytes,2,ref urlCount);
                String ownedUrlText=Base64UrlText::Encode("x",1);
                String ownedUrlBytes=Base64UrlText::Decode(ownedUrlText.Data,ownedUrlText.Length);
                nuint encoded=0; VarInt::TryWriteUInt32(bytes,4,42u,ref encoded);
                uint zigzag=VarInt::ZigZagEncode(-42); int signedNumber=VarInt::ZigZagDecode(zigzag);
                byte[16] variableBytes; BinaryBufferWriter variableOutput=new(variableBytes,16);
                variableOutput.TryWriteVarUInt32(300u); variableOutput.TryWriteVarUInt64(300ul);
                BinaryBufferReader variableInput=new(variableBytes,variableOutput.Position);
                uint variableSmall=0u; ulong variableLarge=0ul;
                variableInput.ReadVarUInt32(ref variableSmall); variableInput.ReadVarUInt64(ref variableLarge);
                BitSet flags=new(65); flags.SetRange(1,63,true);
                bool any=flags.HasAnySet(); bool all=flags.HasAllSet();
                flags.ShiftLeft(1); flags.ShiftRight(32);
                immutable(BitSet)* protectedFlags=&flags;
                var selectedBits=protectedFlags.AsEnumerable().Where((nuint bit) => bit > 1).Take(2);
                var pairedIndices=scalarQuery.Zip(selectedBits,(uint scalar,nuint bit) => (ulong)scalar+(ulong)bit);
                uint bits=BitConverter::FloatToUInt32Bits(1.0f);
                int[3] numbers=[1,2,3]; Span<int> values=new(numbers,3);
                int searchKey=2; nuint found=BinarySearch<int>(values.AsImmutable(),&searchKey,Comparer<int>::Compare);
                Rotate<int>(values,1);
                StableSort<int>(values,Comparer<int>::Compare);
                Vector<int> removedRange=new(); removedRange.Add(1); removedRange.RemoveRange(0,1); removedRange.ShrinkToFit(); removedRange.RemoveAll(RemoveZero);
                removedRange.InsertRange(0,removedRange.AsImmutable());
                SpanWindows<int> neighborWindows=removedRange.AsSpan().Windows(2);
                var windowQuery=neighborWindows.AsEnumerable();
                PseudoRandom random=new(42ul); Shuffle<int>(values,ref random);
                values.Fill(7);
                float fraction=random.NextFloat();
                float root=Math::Sqrt(4.0f);
                uint divisor=Math::GreatestCommonDivisor(48u,18u);
                ulong wideDivisor=Math::GreatestCommonDivisor(48ul,18ul);
                nuint nativeDivisor=Math::GreatestCommonDivisor((nuint)48,(nuint)18);
                uint multiple=0u; Math::TryLeastCommonMultiple(48u,18u,ref multiple);
                ulong wideMultiple=0ul; Math::TryLeastCommonMultiple(48ul,18ul,ref wideMultiple);
                nuint nativeMultiple=0; Math::TryLeastCommonMultiple((nuint)48,(nuint)18,ref nativeMultiple);
                float signed=Math::CopySign(root,-0.0f);
                float truncated=Math::Truncate(-1.5f); double rounded=Math::Round(2.5);
                float floored=Math::Floor(-1.5f); double ceiling=Math::Ceiling(1.5);
                extralong wide=(extralong)1<<100;
                extralong wideMinimum=Math::Min(wide+1,wide+2);
                extralong wideMaximum=Math::Max(wide+1,wide+2);
                extralong wideClamped=Math::Clamp(wide+3,wide+1,wide+2);
                extralong wideAbsolute=Math::Abs(-wide);
                extralong parsedWide=ParseExtraLong("1267650600228229401496703205376");
                TryParseExtraLong("-170141183460469231731687303715884105728",&parsedWide);
                Span<readonly(char)> integerToken=new("42",2);
                long integerValue=0l; integerToken.TryParseLong(&integerValue);
                long integerParsed=integerToken.ParseLong();
                extralong integerWide=integerToken.ParseExtraLong();
                char[3] caseBuffer=['A','b',(char)255];
                Ascii::ToLower(caseBuffer,3); Ascii::ToUpper(caseBuffer,3);
                char[40] wideDigits; nuint wideWritten=0;
                IntegerFormatting::TryFormat(parsedWide,wideDigits,40,ref wideWritten);
                String wideText=s$"Wide integer: {parsedWide}";
                Date calendar=new(2024,2,29); Date tomorrow=calendar.AddDays(1);
                Date anniversary=calendar.AddYears(1); Date nextMonth=calendar.AddMonths(1);
                DayOfWeek weekday=calendar.DayOfWeek; int ordinalDay=calendar.DayOfYear;
                int calendarDay=tomorrow.DayNumber; int monthDays=Date::DaysInMonth(2024,2);
                String dated=s$"Next day: {tomorrow}";
                Uuid identifier=Uuid::Parse("00112233-4455-6677-8899-aabbccddeeff");
                ulong identifierHigh=identifier.High; ulong identifierLow=identifier.Low;
                bool missingIdentifier=identifier.IsNil;
                String identifierText=s$"Id: {identifier}";
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
                uint roundedBits=BitOperations::RoundUpToPowerOfTwo(42u);
                ulong roundedWideBits=BitOperations::RoundUpToPowerOfTwo(42ul);
                bool absolute=Paths::IsFullyQualified("/tmp/file",PathStyle::Unix);
                bool countedRooted=Paths::IsRooted(textView,PathStyle::Unix);
                bool countedAbsolute=Paths::IsFullyQualified(textView,PathStyle::Unix);
                String countedJoinedPath=Paths::Join(textView,trimmedText,PathStyle::Unix);
                Span<readonly(char)> fileName=Paths::GetFileName(textView,PathStyle::Unix);
                var directoryName=Paths::GetDirectoryName(textView,PathStyle::Unix);
                var extension=Paths::GetExtension(textView,PathStyle::Unix);
                var stem=Paths::GetFileNameWithoutExtension(textView,PathStyle::Unix);
                var pathRoot=Paths::GetRoot(textView,PathStyle::Unix);
                bool localResult=Contains(42);
                Deque<int> ends=new(); ends.AddFirst(1); ends.AddLast(2); int removed=ends.RemoveLast(); ends.ShrinkToFit();
                var endQuery=ends.AsEnumerable().Where((int value) => value > 0);
                int firstEnd=endQuery.ElementAt(0);
                Queue<int> pending=new(); pending.Enqueue(42); pending.ShrinkToFit();
                Unique<int> foldedOwner=pending.AsEnumerable().Aggregate(new Unique<int>(new* int(0)),FoldOwned);
                HashSet<int> uniquePending=pending.AsEnumerable().ToHashSet();
                var distinctPending=pending.AsEnumerable().Distinct().Take(2);
                nuint distinctCount=distinctPending.Count();
                var pendingQuery=pending.AsEnumerable().Take(1);
                Stack<int> lifo=new(); lifo.Push(42);
                var lifoQuery=lifo.AsEnumerable().Take(1);
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
                CString::TryAppend("!",terminated,8);
                Arena scratch=new(); scratch.AllocateZeroed(16,8);
                File temporary=File::OpenTemporary();
                FileBinary::WriteDoubleBigEndian(&temporary,1.25);
                temporary.Close();
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
        foreach(var adapter in new[]{("encodedOutput","Base64Encoder.gf"),("decodedOutput","Base64Decoder.gf")})
        foreach(string member in new[]{"Write","Complete"})
        {
            int call=text.IndexOf(adapter.Item1+"."+member,StringComparison.Ordinal)+adapter.Item1.Length+1;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains(adapter.Item2,JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        foreach(string member in new[]{"TryWrite","Length","Clear"})
        {
            int call=text.IndexOf("measuredOutput."+member,StringComparison.Ordinal)+"measuredOutput.".Length;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("CountingWriter.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        int lineCall=text.IndexOf("Console::TryReadLine(&measuredOutput)",StringComparison.Ordinal)+"Console::".Length;
        var lineHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,lineCall),Protocol.JsonOptions);
        Assert.Contains("ITextWriter* output",lineHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("Console.gf",JsonSerializer.Serialize(model.Definition(source.Path,lineCall),Protocol.JsonOptions));
        foreach(string member in new[]{"TryAppend","GetHash","Reset"})
        {
            int call=text.IndexOf("contentHash."+member,StringComparison.Ordinal)+"contentHash.".Length;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("SHA256.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
            if(member=="GetHash") Assert.Contains("byte[32]",hover.GetProperty("contents").GetProperty("value").GetString());
        }
        int fileHashCall=text.IndexOf("FileHash::SHA256",StringComparison.Ordinal)+"FileHash::".Length;
        var fileHashHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,fileHashCall),Protocol.JsonOptions);
        Assert.Contains("byte[32]",fileHashHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("FileHash.gf",JsonSerializer.Serialize(model.Definition(source.Path,fileHashCall),Protocol.JsonOptions));
        int distinctCall=sourceTokens.Single(token=>token.Text=="Distinct").Start;
        var distinctHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,distinctCall),Protocol.JsonOptions);
        Assert.Contains("DistinctSource",distinctHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.DoesNotContain('$',distinctHover.GetProperty("contents").GetProperty("value").GetString()!);
        Assert.Contains("EnumerableDistinct.gf",JsonSerializer.Serialize(model.Definition(source.Path,distinctCall),Protocol.JsonOptions));
        int setCall=sourceTokens.Single(token=>token.Text=="ToHashSet").Start;
        var setHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,setCall),Protocol.JsonOptions);
        Assert.Contains("HashSet",setHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("EnumerableHashSet.gf",JsonSerializer.Serialize(model.Definition(source.Path,setCall),Protocol.JsonOptions));
        int aggregateCall=sourceTokens.Single(token=>token.Text=="Aggregate").Start;
        var aggregateHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,aggregateCall),Protocol.JsonOptions);
        Assert.Contains("Unique",aggregateHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("EnumerableExtensions.gf",JsonSerializer.Serialize(model.Definition(source.Path,aggregateCall),Protocol.JsonOptions));
        foreach(string marker in new[]{"Paths::IsRooted(textView","Paths::IsFullyQualified(textView","Paths::Join(textView"})
        {
            int call=text.IndexOf(marker,StringComparison.Ordinal)+"Paths::".Length;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains("Span",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("Paths.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        foreach(string member in new[]{"WriteField","WriteEndRecord"})
        {
            int call=sourceTokens.Single(token=>token.Text==member).Start;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("CsvWriter.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        foreach(var item in new[]{("trimmedText.TryParseBool","SpanBooleanParsing.gf"),("trimmedText.ParseBool","SpanBooleanParsingThrowing.gf")})
        {
            int call=text.IndexOf(item.Item1,StringComparison.Ordinal)+"trimmedText.".Length;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains("bool",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains(item.Item2,JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        foreach(string member in new[]{"Encode","Decode"})
        {
            int call=text.IndexOf("Base64UrlText::"+member,StringComparison.Ordinal)+"Base64UrlText::".Length;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("Base64UrlText.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        foreach(string member in new[]{"RawField","FieldOffset","ValueLength","IsQuoted","EndOfRecord","TryCopyValue"})
        {
            int call=sourceTokens.Single(token=>token.Text==member).Start;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("CsvReader.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        foreach(string member in new[]{"GreatestCommonDivisor","TryLeastCommonMultiple"})
        {
            var calls=sourceTokens.Where(token=>token.Text==member).ToArray();
            Assert.Equal(3,calls.Length);
            string[] widths=["uint","ulong","nuint"];
            for(int i=0;i<calls.Length;i++)
            {
                var call=calls[i];
                var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call.Start),Protocol.JsonOptions);
                string signature=hover.GetProperty("contents").GetProperty("value").GetString()!;
                Assert.Contains(member,signature);
                Assert.Contains($"{widths[i]} a",signature);
                if(member=="TryLeastCommonMultiple") Assert.Contains($"ref {widths[i]} result",signature);
                Assert.Contains("Math.gf",JsonSerializer.Serialize(model.Definition(source.Path,call.Start),Protocol.JsonOptions));
            }
        }
        int appendCall=sourceTokens.Last(token=>token.Text=="TryAppend").Start;
        var appendHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,appendCall),Protocol.JsonOptions);
        Assert.Contains("TryAppend",appendHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("CString.gf",JsonSerializer.Serialize(model.Definition(source.Path,appendCall),Protocol.JsonOptions));
        int chunksCall=sourceTokens.Last(token=>token.Text=="Chunks").Start;
        var chunksHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,chunksCall),Protocol.JsonOptions);
        Assert.Contains("SpanChunks",chunksHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("SpanChunks.gf",JsonSerializer.Serialize(model.Definition(source.Path,chunksCall),Protocol.JsonOptions));
        foreach(var item in new[]{("pending","Queue.gf"),("lifo","Stack.gf"),("list","LinkedList.gf")})
        {
            int call=text.IndexOf(item.Item1+".AsEnumerable",StringComparison.Ordinal)+item.Item1.Length+1;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains("AsEnumerable",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains(item.Item2,JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
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
        foreach(string member in new[]{"IsEmpty","AddMonths","AddYears","DayOfWeek","DayOfYear","Join","Parse","TryParse","FromUtf16","ToUtf16","Skip","IntersectWith","SymmetricExceptWith","Minify","ShiftLeft","ShiftRight","TryReadBytes","TryWriteBytes","Base64Url","TryGetEncodedLength","TryGetDecodedLength","Truncate","Round","Floor","Ceiling","ParseExtraLong","TryParseExtraLong","Trim","IsNullOrEmpty","RawToken","ValueTextEquals","StartsWith","EndsWith","TryWriteExtraLongBigEndian","TryReadExtraLongBigEndian","WriteDoubleBigEndian","High","Low","IsNil","Zip","TrySplitOnce","Combine","TryWriteVarUInt32","TryWriteVarUInt64","ReadVarUInt32","ReadVarUInt64","AsEnumerable","ElementAt","InsertRange","Windows","GetFileName","GetDirectoryName","GetExtension","GetFileNameWithoutExtension","GetRoot","RoundUpToPowerOfTwo","StableSort","Resize","Quote","Unquote","Multiply","Subtract","Sign","TryParseLong","ParseLong","ToLower","ToUpper"})
        {
            int offset=sourceTokens.Last(token=>token.Text==member).Start;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.NotNull(model.Definition(source.Path,offset));
        }
        int localCall=sourceTokens.Last(token=>token.Text=="Contains").Start;
        var localHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,localCall),Protocol.JsonOptions);
        Assert.Contains("Contains(int value)",localHover.GetProperty("contents").GetProperty("value").GetString());
        int wideFormat=sourceTokens.First(token=>token.Text=="TryFormat").Start;
        var wideHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,wideFormat),Protocol.JsonOptions);
        Assert.Contains("extralong value",wideHover.GetProperty("contents").GetProperty("value").GetString());
        int wideNumber=sourceTokens.Last(token=>token.Text=="WriteNumber").Start;
        var wideNumberHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,wideNumber),Protocol.JsonOptions);
        Assert.Contains("extralong value",wideNumberHover.GetProperty("contents").GetProperty("value").GetString());
        foreach(var item in new[]{("TryParseLong","SpanIntegerParsing.gf"),("ParseLong","SpanIntegerParsingThrowing.gf")})
        {
            int offset=sourceTokens.Last(token=>token.Text==item.Item1).Start;
            Assert.Contains(item.Item2,JsonSerializer.Serialize(model.Definition(source.Path,offset),Protocol.JsonOptions));
        }
    }
}

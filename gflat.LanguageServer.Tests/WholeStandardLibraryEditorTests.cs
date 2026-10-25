using System.Text.Json;
using gflat.diagnostics;
using Xunit.Abstractions;

namespace gflat.LanguageServer.Tests;

public class WholeStandardLibraryEditorTests(ITestOutputHelper output)
{
    [Fact]
    public void BuildingTheLibraryEditorIndexStaysWithinAnAllocationBudget()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "StandardLibrary");
        var sources = Directory.GetFiles(directory, "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        sources.Add(new SourceFile(Path.GetFullPath("std-editor/allocation.gf"), """
            using std; using std::Queries; using std::Collections;
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
        output.WriteLine($"Editor index allocated {allocated:N0} bytes.");
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
            using std; using std::Queries; using std::Collections;
            private extern void*? malloc(nuint bytes);
            private extern void free(void* storage);
            bool Contains(int value) => value == 42;
            bool RemoveZero(readonly(int)* value) => *value == 0;
            Unique<int> FoldOwned(Unique<int> total,int value) { total.Value+=value; return move total; }
            int main() {
                void*? localNative=malloc(8); if(localNative!=null) free(localNative!);
                void*?(nuint)* localMalloc=&malloc;
                LinkedList<int> list=new();
                nuint nativeCount=2; nuint[3] nativeCounts=[0,1,nativeCount];
                byte[3] byteValues=[0,127,255];
                byte[1] delimiterBytes=[255];
                nuint firstDelimiter=ByteSearch::IndexOfAny(byteValues,3,delimiterBytes,1);
                nuint lastDelimiter=ByteSearch::LastIndexOfAny(byteValues,3,delimiterBytes,1);
                char[8] base32Encoded; char[3] base32Decoded; nuint base32Written=0;
                Base32::TryGetEncodedLength(3,ref base32Written);
                Base32::TryEncode("foo",3,base32Encoded,8,ref base32Written);
                Base32::TryGetDecodedLength(base32Encoded,8,ref base32Written);
                Base32::TryDecode(base32Encoded,8,base32Decoded,3,ref base32Written);
                Vector<int> popValues=new(); popValues.Add(42); int poppedValue=0; bool popped=popValues.TryPop(ref poppedValue);
                alias NativeCount=nuint;
                NativeCount[] aliasedCounts=[0,nativeCount];
                NativeCount[2][] countGrid=[[0,nativeCount],[nativeCount,1]];
                LinkedList<int>::Node* node=list.AddLast(42);
                list.AddLast(0); nuint removedLinked=list.RemoveAll(RemoveZero);
                var linkedQuery=list.AsEnumerable().Take(1);
                var endpointQuery=linkedQuery.Prepend(0).Append(100);
                var backwardsQuery=linkedQuery.Reverse(); bool backwardsAny=backwardsQuery.Any();
                bool queryHasAny=linkedQuery.Any(Contains);
                nuint queryCount=linkedQuery.Count(Contains);
                int foldedValue=linkedQuery.Aggregate((int left,int right)=>left+right);
                int queryMinimum=linkedQuery.Min(); int queryMaximum=linkedQuery.Max();
                int queryMinimumCustom=linkedQuery.Min((immutable(int)* left,immutable(int)* right)=>*left-*right);
                int queryMaximumCustom=linkedQuery.Max((immutable(int)* left,immutable(int)* right)=>*left-*right);
                int queryElement=0; bool queryIndexed=linkedQuery.TryElementAt(0,ref queryElement);
                bool querySame=linkedQuery.SequenceEqual(linkedQuery);
                bool querySameCustom=linkedQuery.SequenceEqual(linkedQuery,(immutable(int)* left,immutable(int)* right)=>*left==*right);
                int queryKey=42;
                bool queryHasKey=linkedQuery.Contains(&queryKey);
                bool queryHasCustomKey=linkedQuery.Contains(&queryKey,(immutable(int)* left,immutable(int)* right)=>*left==*right);
                String text=s"ad"; text.Insert(1,"bc");
                bool matchesCString=text.Equals("abcd");
                bool matchesCountedText=text.Equals("abcd",4);
                Span<readonly(char)> textView=text.AsImmutable(); Span<char> writableText=text.AsSpan();
                Span<readonly(char)> shallowTextView=text.AsReadOnly();
                Span<readonly(char)> trimmedText=textView.Trim();
                bool booleanValue=false;
                bool booleanParsed=trimmedText.TryParseBool(&booleanValue);
                bool requiredBoolean=trimmedText.ParseBool();
                String resizedText=new(); resizedText.Resize(4,'x'); resizedText.Reserve(64); resizedText.ShrinkToFit();
                String ownedQuotedText=JsonStringText::Quote(trimmedText);
                String ownedUnquotedText=JsonStringText::Unquote(ownedQuotedText.AsReadOnly());
                BigInteger arbitraryInteger=new(42l);
                BigInteger arbitraryFactor=BigInteger::Parse("1000000000000000000000000000000");
                BigInteger::TryParse("+00042",ref arbitraryInteger);
                BigInteger arbitraryProduct=arbitraryInteger.Multiply(&arbitraryFactor);
                BigInteger arbitraryPower=arbitraryInteger.Pow(3u);
                long arbitraryLong=arbitraryInteger.ToLong(); ulong arbitraryULong=arbitraryInteger.ToULong();
                arbitraryInteger.TryToLong(ref arbitraryLong); arbitraryInteger.TryToULong(ref arbitraryULong);
                BigInteger arbitrarySum=arbitraryProduct.Add(&arbitraryInteger);
                BigInteger arbitraryDifference=arbitrarySum.Subtract(&arbitraryInteger);
                BigInteger arbitraryAbsolute=arbitraryDifference.Abs(); BigInteger arbitraryNegative=arbitraryDifference.Negate();
                BigInteger arbitraryGcd=BigInteger::GreatestCommonDivisor(&arbitraryInteger,&arbitraryFactor);
                BigInteger arbitraryLargeRemainder=new();
                BigInteger arbitraryLargeQuotient=arbitraryProduct.DivRem(&arbitraryFactor,ref arbitraryLargeRemainder);
                BigInteger arbitraryDivided=arbitraryProduct.Divide(&arbitraryFactor);
                BigInteger arbitraryResidual=arbitraryProduct.Remainder(&arbitraryFactor);
                int arbitraryRemainder=0;
                BigInteger arbitraryQuotient=arbitraryDifference.DivRem(7,ref arbitraryRemainder);
                int arbitrarySign=arbitraryDifference.Sign;
                bool prefix=trimmedText.StartsWith("a"); bool suffix=trimmedText.EndsWith("d",1);
                Span<char> trimmedWritable=writableText.TrimStart().TrimEnd();
                Span<readonly(char)> textKey=new(); Span<readonly(char)> textValue=new();
                bool hasSeparator=trimmedText.TrySplitOnce('=',ref textKey,ref textValue);
                bool hasSequenceCString=textView.TrySplitOnce("::",ref textKey,ref textValue);
                bool hasSequenceCounted=textView.TrySplitOnce("::",2,ref textKey,ref textValue);
                Span<readonly(char)> sequenceSeparator=new("::",2);
                bool hasSequenceSpan=textView.TrySplitOnce(sequenceSeparator,ref textKey,ref textValue);
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
                HMACSHA256 authentication=new("key",3);
                authentication.TryAppend(text.Data,text.Length);
                byte[32] authenticationTag=authentication.GetHash();
                authentication.Reset();
                byte[32] fileDigest=FileHash::SHA256("input.bin");
                Utf8Decoder decoder=new(); uint scalar=0u; nuint consumed=0;
                Rune unicodeScalar=new(0x1f600u); nuint scalarUtf8=unicodeScalar.Utf8Length;
                nuint scalarUtf16=unicodeScalar.Utf16Length; String scalarText=unicodeScalar.ToString();
                String hexOutput=new(); ITextWriter* hexWriter=&hexOutput; hexWriter.WriteHex(text.Data,text.Length); hexWriter.WriteRepeated('-',3);
                decoder.Read("a",1,true,&scalar,&consumed);
                Utf8Scalars characters=new(text.Data,text.Length);
                Utf8Scalars::Enumerator iterator=characters.GetEnumerator();
                immutable(Utf8Scalars) protectedCharacters=characters;
                var scalarQuery=protectedCharacters.AsEnumerable().Where((uint scalar) => scalar != 0u).Take(2);
                ushort[2] scalarUnits=[(ushort)65,(ushort)90];
                immutable(Utf16Scalars) wideCharacters=new(scalarUnits,2);
                var wideScalarQuery=wideCharacters.AsEnumerable().Select((uint scalar) => (int)scalar);
                ITextWriter* output=Console::Out;
                LinePrefixWriter labelledOutput=new(output,"> ",2);
                labelledOutput.Write(text.Data,text.Length);
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
                JsonText::PrettyPrint("[true]",6,&compactText);
                JsonText::PrettyPrint("{}",2,&compactText,0);
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
                BinaryBufferReader binaryInput=new(bytes,4); byte binaryPeek=0; bool hasBinaryByte=binaryInput.TryPeekByte(ref binaryPeek); binaryInput.TryReadBytes(bytes,4);
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
                variableOutput.TryWriteZigZagInt32(-42); variableOutput.TryWriteZigZagInt64(-300l);
                BinaryBufferReader variableInput=new(variableBytes,variableOutput.Position);
                uint variableSmall=0u; ulong variableLarge=0ul;
                variableInput.ReadVarUInt32(ref variableSmall); variableInput.ReadVarUInt64(ref variableLarge);
                int variableSignedSmall=0; long variableSignedLarge=0l;
                variableInput.ReadZigZagInt32(ref variableSignedSmall); variableInput.ReadZigZagInt64(ref variableSignedLarge);
                BitSet flags=new(65); flags.SetRange(1,63,true);
                BitSet copiedFlags=flags;
                bool equalFlags=flags.Equals(&copiedFlags); nuint flagHash=flags.GetHashCode();
                bool any=flags.HasAnySet(); bool all=flags.HasAllSet();
                bool overlappingBits=flags.Overlaps(&flags);
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
                float fraction=random.NextFloat(); int signedRandomValue=random.NextInt();
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
                int isoYear=calendar.IsoWeekYear; int isoWeek=calendar.IsoWeekOfYear;
                int isoWeeks=Date::GetIsoWeeksInYear(isoYear);
                Date isoRoundtrip=Date::FromIsoWeek(isoYear,isoWeek,weekday);
                Deque<int> endpoints=new(); endpoints.AddFirst(42); int endpoint=0;
                endpoints.TryRemoveFirst(ref endpoint); endpoints.TryRemoveLast(ref endpoint);
                int calendarDay=tomorrow.DayNumber; int monthDays=Date::DaysInMonth(2024,2);
                String dated=s$"Next day: {tomorrow}";
                TimeSpan elapsed=TimeSpan::FromMilliseconds(1250l);
                TimeSpan budget=TimeSpan::FromSeconds(2l); TimeSpan remainingTime=budget.Subtract(elapsed);
                long remainingTicks=remainingTime.Ticks; double remainingSeconds=remainingTime.TotalSeconds;
                String durationText=s$"Remaining: {remainingTime}";
                TimeSpan parsedDuration=TimeSpan::Parse("01:29:30.5");
                TimeSpan::TryParse("00:00:01",&parsedDuration);
                IpAddress networkAddress=IpAddress::Parse("2001:db8::1");
                IpNetwork subnet=IpNetwork::FromAddress(networkAddress,32);
                IpNetwork parsedSubnet=IpNetwork::Parse("2001:db8::/032");
                IpNetwork::TryParse("2001:db8::/32",&parsedSubnet);
                bool insideSubnet=subnet.Contains(&networkAddress);
                int subnetPrefix=subnet.PrefixLength; IpAddress subnetBase=subnet.NetworkAddress;
                bool ipv4Address=networkAddress.IsIpv4; char[39] addressText; nuint addressWritten=0;
                networkAddress.TryFormat(addressText,39,ref addressWritten);
                byte[16] addressBytes; nuint addressByteCount=0;
                networkAddress.TryWriteBytes(addressBytes,16,ref addressByteCount);
                IpAddress restoredNetworkAddress=IpAddress::FromBytes(addressBytes,addressByteCount);
                String networkMessage=s$"Host: {networkAddress}";
                Uuid identifier=Uuid::Parse("00112233-4455-6677-8899-aabbccddeeff");
                ulong identifierHigh=identifier.High; ulong identifierLow=identifier.Low;
                bool missingIdentifier=identifier.IsNil;
                String identifierText=s$"Id: {identifier}";
                Unique<int> owner=new(new* int(41));
                readonly(Unique<int>)* ownerView=&owner; ownerView.Value++;
                immutable(Unique<int>)* ownerDeep=&owner; int protectedValue=ownerDeep.ValueImmutable;
                bool emptyOwner=owner.IsEmpty;
                int*? releasedObject=owner.Release();
                if(releasedObject!=null) delete releasedObject!;
                int swapLeft=1; int swapRight=2; Swap(ref swapLeft,ref swapRight);
                String limitedFileText=FileText::ReadAll("sample.txt",1024);
                Vector<byte> limitedFileBytes=FileBytes::ReadAll("sample.bin",1024);
                FileBytes::WriteAll("copy.bin",ref limitedFileBytes);
                FileBytes::AppendAll("copy.bin",ref limitedFileBytes);
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
                FileVarInt::WriteUInt32(&temporary,300u);
                FileVarInt::WriteUInt64(&temporary,300ul);
                uint fileUnsigned=FileVarInt::ReadUInt32(&temporary);
                ulong fileWide=FileVarInt::ReadUInt64(&temporary);
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
        foreach(string method in new[]{"TryGetEncodedLength","TryEncode","TryGetDecodedLength","TryDecode"})
        {
            int call=text.IndexOf("Base32::"+method,StringComparison.Ordinal)+"Base32::".Length;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains("bool",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("Base32.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        int randomCall=text.IndexOf("random.NextInt",StringComparison.Ordinal)+"random.".Length;
        var randomHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,randomCall),Protocol.JsonOptions);
        Assert.Contains("int",randomHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("PseudoRandom.gf",JsonSerializer.Serialize(model.Definition(source.Path,randomCall),Protocol.JsonOptions));
        var randomHelp=JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path,text.IndexOf('(',randomCall)+1),Protocol.JsonOptions);
        Assert.Equal(3,randomHelp.GetProperty("signatures").GetArrayLength());
        foreach(string method in new[]{"IndexOfAny","LastIndexOfAny"})
        {
            int call=text.IndexOf("ByteSearch::"+method,StringComparison.Ordinal)+"ByteSearch::".Length;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains("nuint",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("ByteSearch.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        int repeatedCall=text.IndexOf("hexWriter.WriteRepeated",StringComparison.Ordinal)+"hexWriter.".Length;
        var repeatedHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,repeatedCall),Protocol.JsonOptions);
        Assert.Contains("WriteRepeated",repeatedHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("TextWriterExtensions.gf",JsonSerializer.Serialize(model.Definition(source.Path,repeatedCall),Protocol.JsonOptions));
        int nativeCall=text.IndexOf("malloc(8)",StringComparison.Ordinal);
        Assert.Contains("complete.gf",JsonSerializer.Serialize(model.Definition(source.Path,nativeCall),Protocol.JsonOptions));
        Assert.DoesNotContain("Memory.gf",JsonSerializer.Serialize(model.Definition(source.Path,nativeCall),Protocol.JsonOptions));
        int nativeAddress=text.IndexOf("&malloc",StringComparison.Ordinal)+1;
        Assert.Contains("complete.gf",JsonSerializer.Serialize(model.Definition(source.Path,nativeAddress),Protocol.JsonOptions));
        int linkedRemoval=text.IndexOf("list.RemoveAll",StringComparison.Ordinal)+"list.".Length;
        var linkedRemovalHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,linkedRemoval),Protocol.JsonOptions);
        Assert.Contains("nuint",linkedRemovalHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("LinkedList.gf",JsonSerializer.Serialize(model.Definition(source.Path,linkedRemoval),Protocol.JsonOptions));
        int peekCall=text.IndexOf("binaryInput.TryPeekByte",StringComparison.Ordinal)+"binaryInput.".Length;
        var peekHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,peekCall),Protocol.JsonOptions);
        Assert.Contains("bool",peekHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("BinaryBufferReader.gf",JsonSerializer.Serialize(model.Definition(source.Path,peekCall),Protocol.JsonOptions));
        int popCall=text.IndexOf("popValues.TryPop",StringComparison.Ordinal)+"popValues.".Length;
        var popHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,popCall),Protocol.JsonOptions);
        Assert.Contains("bool",popHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("Vector.gf",JsonSerializer.Serialize(model.Definition(source.Path,popCall),Protocol.JsonOptions));
        int gcdCall=text.IndexOf("GreatestCommonDivisor",text.IndexOf("arbitraryGcd",StringComparison.Ordinal),StringComparison.Ordinal);
        var gcdHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,gcdCall),Protocol.JsonOptions);
        Assert.Contains("GreatestCommonDivisor",gcdHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("BigInteger.gf",JsonSerializer.Serialize(model.Definition(source.Path,gcdCall),Protocol.JsonOptions));
        foreach(var signCall in new[]{("arbitraryAbsolute","Abs"),("arbitraryNegative","Negate")})
        {
            int offset=text.IndexOf("."+signCall.Item2,text.IndexOf(signCall.Item1,StringComparison.Ordinal),StringComparison.Ordinal)+1;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.Contains(signCall.Item2,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("BigInteger.gf",JsonSerializer.Serialize(model.Definition(source.Path,offset),Protocol.JsonOptions));
        }
        int largeDivision=text.IndexOf(".DivRem",text.IndexOf("arbitraryLargeQuotient",StringComparison.Ordinal),StringComparison.Ordinal)+1;
        var largeDivisionHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,largeDivision),Protocol.JsonOptions);
        Assert.Contains("BigInteger",largeDivisionHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("ref",largeDivisionHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("BigInteger.gf",JsonSerializer.Serialize(model.Definition(source.Path,largeDivision),Protocol.JsonOptions));
        var divisionHelp=JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path,text.IndexOf('(',largeDivision)+1),Protocol.JsonOptions);
        Assert.Equal(2,divisionHelp.GetProperty("signatures").GetArrayLength());
        foreach(string member in new[]{"Divide","Remainder"})
        {
            int offset=sourceTokens.Last(token=>token.Text==member).Start;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("BigInteger.gf",JsonSerializer.Serialize(model.Definition(source.Path,offset),Protocol.JsonOptions));
        }
        int hexWrite=sourceTokens.Last(token=>token.Text=="WriteHex").Start;
        var hexHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,hexWrite),Protocol.JsonOptions);
        Assert.Contains("uppercase = true",hexHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("HexWriter.gf",JsonSerializer.Serialize(model.Definition(source.Path,hexWrite),Protocol.JsonOptions));
        foreach(string member in new[]{"Utf8Length","Utf16Length"})
        {
            int offset=sourceTokens.Last(token=>token.Text==member).Start;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("nuint",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("Rune.gf",JsonSerializer.Serialize(model.Definition(source.Path,offset),Protocol.JsonOptions));
        }
        foreach(string member in new[]{"IsoWeekYear","IsoWeekOfYear","GetIsoWeeksInYear","FromIsoWeek","TryRemoveFirst","TryRemoveLast","TryElementAt","Reverse"})
        {
            int offset=sourceTokens.Last(token=>token.Text==member).Start;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            string definition=member=="TryElementAt"?"enumerable_element_at.gf":member=="Reverse"?"enumerable_reverse.gf":member.StartsWith("TryRemove",StringComparison.Ordinal)?"Deque.gf":"Date.gf";
            Assert.Contains(definition,JsonSerializer.Serialize(model.Definition(source.Path,offset),Protocol.JsonOptions));
        }
        foreach(var query in new[]{("queryHasAny","Any","EnumerableExtensions.gf"),("queryCount","Count","EnumerableExtensions.gf"),
            ("querySame","SequenceEqual","enumerable_sequence_equal.gf"),("querySameCustom","SequenceEqual","enumerable_sequence_equal.gf"),
            ("queryHasKey","Contains","enumerable_contains.gf"),("queryHasCustomKey","Contains","enumerable_contains.gf")})
        {
            int call=text.IndexOf("."+query.Item2,text.IndexOf(query.Item1,StringComparison.Ordinal),StringComparison.Ordinal)+1;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains(query.Item2=="Count"?"nuint":"bool",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.DoesNotContain('$',hover.GetProperty("contents").GetProperty("value").GetString()!);
            Assert.Contains(query.Item3,JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
            var help=JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path,text.IndexOf('(',call)+1),Protocol.JsonOptions);
            Assert.Equal(2,help.GetProperty("signatures").GetArrayLength());
        }
        int prefixCall=text.IndexOf("labelledOutput.Write",StringComparison.Ordinal)+"labelledOutput.".Length;
        int bigIntegerParser=text.IndexOf("BigInteger::TryParse",StringComparison.Ordinal)+"BigInteger::".Length;
        var bigIntegerParserHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,bigIntegerParser),Protocol.JsonOptions);
        Assert.Contains("bool",bigIntegerParserHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("ref",bigIntegerParserHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("BigInteger.gf",JsonSerializer.Serialize(model.Definition(source.Path,bigIntegerParser),Protocol.JsonOptions));
        foreach(string member in new[]{"ToLong","ToULong","TryToLong","TryToULong"})
        {
            int offset=text.IndexOf("arbitraryInteger."+member,StringComparison.Ordinal)+"arbitraryInteger.".Length;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("BigInteger.gf",JsonSerializer.Serialize(model.Definition(source.Path,offset),Protocol.JsonOptions));
        }
        int divisionCall=text.IndexOf("arbitraryDifference.DivRem",StringComparison.Ordinal)+"arbitraryDifference.".Length;
        int powerCall=text.IndexOf("arbitraryInteger.Pow",StringComparison.Ordinal)+"arbitraryInteger.".Length;
        var powerHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,powerCall),Protocol.JsonOptions);
        Assert.Contains("BigInteger",powerHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("Pow",powerHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("BigInteger.gf",JsonSerializer.Serialize(model.Definition(source.Path,powerCall),Protocol.JsonOptions));
        var divisionHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,divisionCall),Protocol.JsonOptions);
        Assert.Contains("DivRem",divisionHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("ref int remainder",divisionHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("BigInteger.gf",JsonSerializer.Serialize(model.Definition(source.Path,divisionCall),Protocol.JsonOptions));
        foreach(string marker in new[]{"matchesCString=","matchesCountedText="})
        {
            int call=text.IndexOf("Equals",text.IndexOf(marker,StringComparison.Ordinal),StringComparison.Ordinal);
            var equalityHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains("Equals",equalityHover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("String.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        int overlapCall=text.IndexOf("flags.Overlaps",StringComparison.Ordinal)+"flags.".Length;
        var overlapHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,overlapCall),Protocol.JsonOptions);
        Assert.Contains("Overlaps",overlapHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("BitSet.gf",JsonSerializer.Serialize(model.Definition(source.Path,overlapCall),Protocol.JsonOptions));
        foreach(string member in new[]{"Equals","GetHashCode"})
        {
            int call=text.IndexOf("flags."+member,StringComparison.Ordinal)+"flags.".Length;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains("nothrow",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("BitSet.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
            var help=JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path,text.IndexOf('(',call)+1),Protocol.JsonOptions);
            Assert.Single(help.GetProperty("signatures").EnumerateArray());
        }
        foreach(string marker in new[]{"hasSequenceCString=","hasSequenceCounted=","hasSequenceSpan="})
        {
            int call=text.IndexOf("TrySplitOnce",text.IndexOf(marker,StringComparison.Ordinal),StringComparison.Ordinal);
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains("TrySplitOnce",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("TextSplitOncePatternSpan.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        var prefixHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,prefixCall),Protocol.JsonOptions);
        Assert.Contains("Write",prefixHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("LinePrefixWriter.gf",JsonSerializer.Serialize(model.Definition(source.Path,prefixCall),Protocol.JsonOptions));
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
        foreach(string member in new[]{"TryAppend","GetHash","Reset"})
        {
            int call=text.IndexOf("authentication."+member,StringComparison.Ordinal)+"authentication.".Length;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("HMACSHA256.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
            if(member=="GetHash") Assert.Contains("byte[32]",hover.GetProperty("contents").GetProperty("value").GetString());
        }
        var fileHashHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,fileHashCall),Protocol.JsonOptions);
        Assert.Contains("byte[32]",fileHashHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("FileHash.gf",JsonSerializer.Serialize(model.Definition(source.Path,fileHashCall),Protocol.JsonOptions));
        foreach(string member in new[]{"ReadUInt32","ReadUInt64","WriteUInt32","WriteUInt64"})
        {
            int call=text.IndexOf("FileVarInt::"+member,StringComparison.Ordinal)+"FileVarInt::".Length;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("FileVarInt.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        int distinctCall=sourceTokens.Single(token=>token.Text=="Distinct").Start;
        var distinctHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,distinctCall),Protocol.JsonOptions);
        Assert.Contains("DistinctSource",distinctHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.DoesNotContain('$',distinctHover.GetProperty("contents").GetProperty("value").GetString()!);
        Assert.Contains("enumerable_distinct.gf",JsonSerializer.Serialize(model.Definition(source.Path,distinctCall),Protocol.JsonOptions));
        int setCall=sourceTokens.Single(token=>token.Text=="ToHashSet").Start;
        var setHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,setCall),Protocol.JsonOptions);
        Assert.Contains("HashSet",setHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("enumerable_to_hash_set.gf",JsonSerializer.Serialize(model.Definition(source.Path,setCall),Protocol.JsonOptions));
        int aggregateCall=text.IndexOf(".Aggregate",text.IndexOf("foldedOwner",StringComparison.Ordinal),StringComparison.Ordinal)+1;
        var aggregateHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,aggregateCall),Protocol.JsonOptions);
        Assert.Contains("Unique",aggregateHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("EnumerableExtensions.gf",JsonSerializer.Serialize(model.Definition(source.Path,aggregateCall),Protocol.JsonOptions));
        int seedlessCall=text.IndexOf(".Aggregate",text.IndexOf("foldedValue",StringComparison.Ordinal),StringComparison.Ordinal)+1;
        var seedlessHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,seedlessCall),Protocol.JsonOptions);
        Assert.Contains("public int",seedlessHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("EnumerableExtensions.gf",JsonSerializer.Serialize(model.Definition(source.Path,seedlessCall),Protocol.JsonOptions));
        var aggregateHelp=JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path,text.IndexOf('(',seedlessCall)+1),Protocol.JsonOptions);
        Assert.Equal(2,aggregateHelp.GetProperty("signatures").GetArrayLength());
        foreach(var array in new[]{("nativeCounts","nuint[3]"),("byteValues","byte[3]"),
            ("aliasedCounts","NativeCount[2]"),("countGrid","NativeCount[2][2]")})
        {
            int offset=text.IndexOf(array.Item1,StringComparison.Ordinal);
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.Contains(array.Item2,hover.GetProperty("contents").GetProperty("value").GetString());
        }
        foreach(string expression in new[]{"IpNetwork::FromAddress","IpNetwork::Parse","IpNetwork::TryParse",
            "subnet.Contains","subnet.PrefixLength","subnet.NetworkAddress"})
        {
            int offset=text.IndexOf(expression,StringComparison.Ordinal)+expression.LastIndexOfAny([':', '.'])+1;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.Contains(expression[(expression.LastIndexOfAny([':', '.'])+1)..],hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("IpNetwork.gf",JsonSerializer.Serialize(model.Definition(source.Path,offset),Protocol.JsonOptions));
        }
        var prettyCalls=sourceTokens.Where(token=>token.Text=="PrettyPrint").ToArray();
        Assert.Equal(2,prettyCalls.Length);
        foreach(var token in prettyCalls)
        {
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,token.Start),Protocol.JsonOptions);
            Assert.Contains("indentSpaces",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("JsonText.gf",JsonSerializer.Serialize(model.Definition(source.Path,token.Start),Protocol.JsonOptions));
            var help=JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path,text.IndexOf('(',token.Start)+1),Protocol.JsonOptions);
            Assert.Single(help.GetProperty("signatures").EnumerateArray());
        }
        foreach(string member in new[]{"Prepend","Append"})
        {
            int call=text.IndexOf("."+member,text.IndexOf("endpointQuery",StringComparison.Ordinal),StringComparison.Ordinal)+1;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains("EndpointSource",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("enumerable_endpoints.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
            var help=JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path,text.IndexOf('(',call)+1),Protocol.JsonOptions);
            Assert.Single(help.GetProperty("signatures").EnumerateArray());
        }
        foreach(var query in new[]{("queryMinimum","Min"),("queryMaximum","Max"),
            ("queryMinimumCustom","Min"),("queryMaximumCustom","Max")})
        {
            int call=text.IndexOf("."+query.Item2,text.IndexOf(query.Item1,StringComparison.Ordinal),StringComparison.Ordinal)+1;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains("public int",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("enumerable_extrema.gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
            var help=JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path,text.IndexOf('(',call)+1),Protocol.JsonOptions);
            Assert.Equal(2,help.GetProperty("signatures").GetArrayLength());
        }
        int releaseCall=text.IndexOf("owner.Release",StringComparison.Ordinal)+"owner.".Length;
        var releaseHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,releaseCall),Protocol.JsonOptions);
        Assert.Contains("Release",releaseHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("nothrow",releaseHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("Unique.gf",JsonSerializer.Serialize(model.Definition(source.Path,releaseCall),Protocol.JsonOptions));
        int swapCall=sourceTokens.Single(token=>token.Text=="Swap").Start;
        var swapHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,swapCall),Protocol.JsonOptions);
        Assert.Contains("ref int left",swapHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("span_algorithms.gf",JsonSerializer.Serialize(model.Definition(source.Path,swapCall),Protocol.JsonOptions));
        foreach(string fileApi in new[]{"FileText","FileBytes"})
        {
            int call=text.IndexOf(fileApi+"::ReadAll",StringComparison.Ordinal)+fileApi.Length+2;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,call),Protocol.JsonOptions);
            Assert.Contains("nuint maximumBytes",hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains(fileApi+".gf",JsonSerializer.Serialize(model.Definition(source.Path,call),Protocol.JsonOptions));
        }
        int durationFactory=text.IndexOf("TimeSpan::FromMilliseconds",StringComparison.Ordinal)+"TimeSpan::".Length;
        var durationHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,durationFactory),Protocol.JsonOptions);
        Assert.Contains("TimeSpan",durationHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("TimeSpan.gf",JsonSerializer.Serialize(model.Definition(source.Path,durationFactory),Protocol.JsonOptions));
        int binaryWrite=text.IndexOf("FileBytes::WriteAll",StringComparison.Ordinal)+"FileBytes::".Length;
        var binaryWriteHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,binaryWrite),Protocol.JsonOptions);
        Assert.Contains("Vector",binaryWriteHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("FileBytes.gf",JsonSerializer.Serialize(model.Definition(source.Path,binaryWrite),Protocol.JsonOptions));
        int durationSeconds=text.IndexOf("remainingTime.TotalSeconds",StringComparison.Ordinal)+"remainingTime.".Length;
        var secondsHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,durationSeconds),Protocol.JsonOptions);
        Assert.Contains("double",secondsHover.GetProperty("contents").GetProperty("value").GetString());
        int durationParser=text.IndexOf("TimeSpan::TryParse",StringComparison.Ordinal)+"TimeSpan::".Length;
        var durationParserHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,durationParser),Protocol.JsonOptions);
        Assert.Contains("bool",durationParserHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("nothrow",durationParserHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("TimeSpan.gf",JsonSerializer.Serialize(model.Definition(source.Path,durationParser),Protocol.JsonOptions));
        int addressFactory=text.IndexOf("IpAddress::Parse",StringComparison.Ordinal)+"IpAddress::".Length;
        var addressHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,addressFactory),Protocol.JsonOptions);
        Assert.Contains("IpAddress",addressHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("IpAddress.gf",JsonSerializer.Serialize(model.Definition(source.Path,addressFactory),Protocol.JsonOptions));
        int binaryAddressFactory=text.IndexOf("IpAddress::FromBytes",StringComparison.Ordinal)+"IpAddress::".Length;
        var binaryAddressHover=JsonSerializer.SerializeToElement(model.Hover(source.Path,binaryAddressFactory),Protocol.JsonOptions);
        Assert.Contains("nuint count",binaryAddressHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("IpAddress.gf",JsonSerializer.Serialize(model.Definition(source.Path,binaryAddressFactory),Protocol.JsonOptions));
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
            var calls=sourceTokens.Where(token=>token.Text==member&&token.Start>=6&&text.AsSpan(token.Start-6,6).SequenceEqual("Math::")).ToArray();
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
        foreach(string member in new[]{"TryWriteZigZagInt32","TryWriteZigZagInt64","ReadZigZagInt32","ReadZigZagInt64"})
        {
            int offset=sourceTokens.Last(token=>token.Text==member).Start;
            var hover=JsonSerializer.SerializeToElement(model.Hover(source.Path,offset),Protocol.JsonOptions);
            Assert.Contains(member,hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.Contains("nothrow",hover.GetProperty("contents").GetProperty("value").GetString());
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

#requires -PSEdition Desktop
# The installed Visual Studio SDK checks require Windows PowerShell/.NET Framework.
param([string]$Package = (Join-Path $PSScriptRoot 'bin/Release/net472/gflat.VisualStudio.vsix'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = if ($env:GFLAT_TEST_OUTPUT_ROOT) { $env:GFLAT_TEST_OUTPUT_ROOT } else { Join-Path $PSScriptRoot 'obj' }
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$extracted = Join-Path $testRoot ('package-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($Package), $extracted)
$oldExecutable = $env:GFLAT_LSP_EXECUTABLE
$oldEditorAssets = $env:GFLAT_EDITOR_ASSETS
$oldHoverCatalog = $env:GFLAT_HOVER_CATALOG
$oldHoverCapture = $env:GFLAT_HOVER_CAPTURE
try {
    $env:GFLAT_LSP_EXECUTABLE = Join-Path $extracted 'Server/gflat.LanguageServer.exe'
    if (-not (Test-Path -LiteralPath $env:GFLAT_LSP_EXECUTABLE)) { throw 'VSIX is missing the server executable' }
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'gflat.VisualStudio.dll'))) { throw 'VSIX is missing the language client' }
    if (Get-ChildItem -LiteralPath $extracted -Filter 'MonoMod*.dll' -File) { throw 'VSIX still contains the removed runtime patching dependency' }
    foreach ($asset in @('gflat.pkgdef', 'gflat.VisualStudio.pkgdef', 'gflat.VisualStudio.vsdconfig', 'gflat-language-configuration.json', 'Grammars/gflat.tmLanguage.json', 'Grammars/gflat.tmLanguage.tmTheme')) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted $asset))) { throw "VSIX is missing $asset" }
    }
    foreach ($asset in @('BUILD-INFO.json', 'Licenses/LICENSE', 'Licenses/MSBuild.txt', 'Licenses/dotnet/LICENSE.TXT', 'Licenses/dotnet/THIRD-PARTY-NOTICES.TXT', 'Sdk/build/tasks/net472/gflat.Build.Tasks.dll', 'Sdk/build/tasks/net10.0/gflat.Build.Tasks.dll', 'Compiler/MSBuild.dll', 'Server/MSBuild.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted $asset))) { throw "VSIX is missing $asset" }
    }
    foreach ($tool in @('Compiler', 'Server')) {
        foreach ($file in @('Sdk.props', 'Sdk.targets')) {
            if (-not (Test-Path -LiteralPath (Join-Path $extracted "$tool/Sdks/Gflat.Sdk/Sdk/$file"))) { throw "VSIX is missing $tool's private SDK entry $file" }
        }
        foreach ($source in @('core/TypeInfo.gf', 'core/Attribute.gf', 'core/Exception.gf', 'libc/Exceptions/ExceptionHandling.gf', 'core/Text.gf', 'libc/Memory/Memory.gf')) {
            if (-not (Test-Path -LiteralPath (Join-Path $extracted "$tool/Std/$source"))) { throw "VSIX is missing $tool/Std/$source" }
        }
    }
    # The complete optional source library lives in the project SDK. Compiler and
    # server Std folders contain only the built-in runtime contract subset.
    foreach ($source in @('core/String.gf', 'core/ByteSearch.gf', 'core/Deque.gf', 'core/SortedSet.gf', 'core/HashMap.gf', 'core/Collections/HashSetExtensions.gf', 'core/JsonString.gf', 'core/JsonStringWriter.gf', 'core/VarInt.gf', 'core/BinaryPrimitives.gf', 'core/BinaryBufferReader.gf', 'core/BinaryBufferWriter.gf', 'core/IntegerFormatting.gf')) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted "Sdk/std/$source"))) { throw "VSIX is missing Sdk/std/$source" }
    }
    $buildInfo = Get-Content -Raw -LiteralPath (Join-Path $extracted 'BUILD-INFO.json') | ConvertFrom-Json
    if ($buildInfo.commit -notmatch '^[0-9a-f]{40}$') { throw 'VSIX source commit is missing' }
    # Exercise the grammar and configuration actually shipped in the archive.
    Copy-Item -LiteralPath (Join-Path $extracted 'gflat-language-configuration.json') -Destination (Join-Path $extracted 'Grammars')
    $env:GFLAT_EDITOR_ASSETS = Join-Path $extracted 'Grammars'
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $extracted 'extension.vsixmanifest')
    if (-not ($manifest.PackageManifest.Assets.Asset | Where-Object { $_.Type -eq 'DebuggerEngineExtension' -and $_.Path -eq 'gflat.VisualStudio.vsdconfig' })) {
        throw 'VSIX does not register the native debugger component'
    }
    if (Test-Path (Join-Path $extracted 'Microsoft.VisualStudio.Debugger.Engine.dll')) { throw 'The VS debugger implementation must be supplied by Visual Studio.' }
    [xml]$sourceManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source.extension.vsixmanifest')
    if ($manifest.PackageManifest.Metadata.Identity.Version -ne $sourceManifest.PackageManifest.Metadata.Identity.Version) {
        throw 'Packaged manifest is stale. Rebuild the VSIX.'
    }
    if (-not ($manifest.PackageManifest.Assets.Asset | Where-Object { $_.Type -eq 'Microsoft.VisualStudio.VsPackage' -and $_.Path -eq 'gflat.pkgdef' })) {
        throw 'VSIX does not register the editor package definition'
    }
    if (-not ($manifest.PackageManifest.Assets.Asset | Where-Object { $_.Type -eq 'Microsoft.VisualStudio.VsPackage' -and $_.Path -eq 'gflat.VisualStudio.pkgdef' })) {
        throw 'VSIX does not register SDK-generated language options'
    }
    $optionRegistration = Get-Content -Raw -LiteralPath (Join-Path $extracted 'gflat.VisualStudio.pkgdef')
    if (-not $optionRegistration.Contains('Languages\Language Services\gflat\EditorToolsOptions\IntelliSense') -or
        -not $optionRegistration.Contains('{a48bdf16-daf5-4e05-92bc-f9f89c923a91}')) {
        throw 'IntelliSense options must be registered under the gflat language'
    }
    $catalog = Join-Path $extracted 'hover-icons.json'
    $capture = Join-Path $extracted 'hover-response.json'
    $nativeHoverCheck = & (Join-Path $PSScriptRoot 'test-hover.ps1') -Mode Prepare -PackageDirectory $extracted -CatalogPath $catalog -CapturePath $capture
    if ($nativeHoverCheck) {
        $env:GFLAT_HOVER_CATALOG = $catalog
        $env:GFLAT_HOVER_CAPTURE = $capture
    }
    dotnet test (Join-Path $repoRoot 'gflat.LanguageServer.Tests/gflat.LanguageServer.Tests.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged server tests failed' }
    if ($nativeHoverCheck) { & (Join-Path $PSScriptRoot 'test-hover.ps1') -Mode Validate -PackageDirectory $extracted -CatalogPath $catalog -CapturePath $capture }
    $compiler = Join-Path $extracted 'Compiler/gflat.exe'
    & (Join-Path $PSScriptRoot 'test-sdk-installation.ps1') -PackageDirectory $extracted
    & (Join-Path $PSScriptRoot 'test-sdk-elevation.ps1') -PackageDirectory $extracted
    if (-not (Test-Path -LiteralPath $compiler)) { throw 'VSIX is missing the project build compiler' }
    & (Join-Path $repoRoot 'build/test-bundled-msbuild.ps1') -ToolchainDirectory (Join-Path $extracted 'Sdk') -CompilerDirectory (Join-Path $extracted 'Compiler')
    & (Join-Path $PSScriptRoot 'test-project-templates.ps1') -PackageDirectory $extracted
    & (Join-Path $PSScriptRoot 'test-launch.ps1') -PackageDirectory $extracted
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'Compiler/Runtime/Exceptions.cpp'))) { throw 'VSIX is missing the native exception runtime source' }
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'Compiler/Runtime/Stdio.cpp'))) { throw 'VSIX is missing the native stdio bridge source' }
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'Compiler/Std/core/IGarbageCollector.gf'))) { throw 'VSIX is missing the managed collector contract' }
    $noThrowSource = Join-Path $extracted 'nothrow-no-stdlib.gf'
    $noThrowIr = Join-Path $extracted 'nothrow-no-stdlib.ll'
    @'
struct Counter { public int Value; public Counter(int value) nothrow { Value=value; } }
int Pure() nothrow => 42;
int main() { Counter value=new(Pure()); return value.Value; }
'@ | Set-Content -LiteralPath $noThrowSource
    & $compiler $noThrowSource --no-stdlib --emit-ir -o $noThrowIr
    if ($LASTEXITCODE -ne 0) { throw 'Packaged compiler failed the runtime-free nothrow check' }
    if ((Get-Content -Raw -LiteralPath $noThrowIr) -match '@__gflat_eh_|\bpersonality\b|\binvoke\b') {
        throw 'Known nothrow calls unexpectedly require exception support'
    }
    $smoke = Join-Path $extracted 'unwinding-ref.gf'
    @'
void Set(ref int value) { value = 42; throw new Exception(); }
int main() { int value = 0; try { Set(ref value); } catch (ref Exception error) { if (value == 42) return 0; return 1; } return 2; }
'@ | Set-Content -LiteralPath $smoke
    & $compiler $smoke --run -o (Join-Path $extracted 'unwinding-ref.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged exception/ref smoke test failed' }
    $primitive = Join-Path $extracted 'primitive-interface.gf'
    @'
interface IHash {
    readonly int Hash();
    implementation int { public readonly int Hash() => *this; }
}
int Hash<T : IHash>(ref readonly(T) value) => value.Hash();
int main() { int value = 21; IHash* view = &value; return Hash(ref value) + view.Hash() - 42; }
'@ | Set-Content -LiteralPath $primitive
    & $compiler $primitive --optimization 2 --run -o (Join-Path $extracted 'primitive-interface.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged primitive interface smoke test failed' }
    $console = Join-Path $extracted 'console.gf'
    'int main() { Console::Write("console"); Console::WriteLine(); Console::Flush(); return 0; }' | Set-Content -LiteralPath $console
    $std = Join-Path $extracted 'Sdk/std'
    $managed = Join-Path $extracted 'managed.gf'
    @'
void Drop() { int^ value = new(42); }
int main() { Drop(); if (GC::Collect() != (nuint)1) return 1; int^ value = new(7); GC::Collect(); return *value - 7; }
'@ | Set-Content -LiteralPath $managed
    & $compiler $managed (Join-Path $std 'libc/GC.gf') (Join-Path $std 'libc/TracingCollector.gf') --optimization 2 --run -o (Join-Path $extracted 'managed.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged managed allocation/root/collector smoke test failed' }
    $consoleResult = & $compiler $console (Join-Path $std 'core/IOException.gf') (Join-Path $std 'core/EndOfStreamException.gf') (Join-Path $std 'core/BufferTooSmallException.gf') (Join-Path $std 'libc/Console/Console.gf') --run -o (Join-Path $extracted 'console.exe')
    if ($LASTEXITCODE -ne 0 -or $consoleResult -ne 'console') { throw 'Packaged Console/std bridge smoke test failed' }
    $collections = Join-Path $extracted 'collections.gf'
    @'
using std;
int WordLength(String word) => (int)word.Length;
int main() {
    Vector<String> words = new();
    words.Add(s"abc");
    foreach (String word in words) {
        if (word.Data == words[(nuint)0].Data) return 1;
        word[(nuint)0] = 'z';
    }
    foreach (immutable(String) word in words.AsImmutable()) {
        if (word.Data[0] != 'a') return 2;
    }
    var lengths = words.AsEnumerable().Select(WordLength).Take(1).ToVector();
    if (lengths.Count != 1 || lengths[0] != 3) return 3;
    return 0;
}
'@ | Set-Content -LiteralPath $collections
    $bundledStd = Join-Path $extracted 'Sdk/std/core'
    $querySources = @(Get-ChildItem -LiteralPath (Join-Path $bundledStd 'Collections') -Filter '*.gf' -File | ForEach-Object FullName)
    $contractSources = @('IHashable.gf', 'IEquatable.gf', 'IComparable.gf', 'Hashing.gf') | ForEach-Object { Join-Path $bundledStd $_ }
    & $compiler $collections (Join-Path $bundledStd 'Span.gf') (Join-Path $bundledStd 'Vector.gf') (Join-Path $bundledStd 'String.gf') (Join-Path $bundledStd 'ByteSearch.gf') (Join-Path $bundledStd 'IStringConvertable.gf') @contractSources @querySources --run -o (Join-Path $extracted 'collections.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged collection iteration and owning-element copy smoke test failed' }
    $expandedStd = Join-Path $extracted 'expanded-std.gf'
    @'
using std;
int smokeDestroyed = 0;
Vector<int>*? smokeVector = null;
struct SmokeTemporary {
    public int Value;
    public SmokeTemporary() { Value = 42; }
    ~SmokeTemporary() { smokeDestroyed++; }
}
SmokeTemporary Tick(ref int value) { value++; return new SmokeTemporary(); }
bool SmokeRemove(readonly(int)* value) => *value == 9;
int main() {
    HashMap<String, int> map = new();
    map.Add(s"answer", 42);
    String key = s"answer";
    if (map[&key] != 42) return 1;
    Queue<int> queue = new(); queue.Enqueue(42);
    if (queue.Dequeue() != 42) return 2;
    queue.ShrinkToFit();
    if (queue.Capacity != 0) return 39;
    char[32] storage;
    BufferWriter writer = new(storage, 32);
    map[&key].Format(&writer);
    if (writer.Length != 2 || writer.Data[0] != '4' || writer.Data[1] != '2') return 3;
    if (!std::Utf8::IsValid(writer.Data, writer.Length)) return 4;
    String hex = HexText::Encode(writer.Data, writer.Length);
    if (!hex.StartsWith("3432") || hex.Length != 4) return 5;
    HashMap<String, int>::Entry entry = map.Extract(&key);
    if (map.Count != 0 || entry.Value != 42 || !entry.Key.Equals(&key)) return 6;
    SortedSet<int> ordered = new(Comparer<int>::Compare);
    ordered.Add(9); ordered.Add(3); int search = 3;
    if (!ordered.Contains(&search)) return 7;
    Deque<int> ends = new(); ends.AddFirst(42);
    if (ends.RemoveLast() != 42) return 8;
    ends.ShrinkToFit();
    if (ends.Capacity != 0) return 40;
    Stack<int> held = new(); held.Push(42); bool rejected = false;
    try { held.TryPop(ref held.Top); } catch { rejected = true; }
    if (!rejected || held.Count != 1 || held.Top != 42) return 9;
    char[8] plain; nuint written = 0;
    if (!JsonString::TryUnquote("\"ok\"", 4, plain, 8, ref written) || written != 2 || plain[0] != 'o' || plain[1] != 'k') return 10;
    ITextWriter* target = &writer; target.WriteJsonString(plain, written);
    if (writer.Length != 6 || writer.Data[2] != '"' || writer.Data[5] != '"') return 11;
    byte[10] packet; uint number = 0u; nuint consumed = 0;
    if (!VarInt::TryWriteUInt32(packet, 10, 300u, ref written) || written != 2) return 12;
    if (VarInt::ReadUInt32(packet, written, ref number, ref consumed) != VarInt::Status::Done || number != 300u || consumed != 2) return 13;
    if (!BinaryPrimitives::TryWriteFloatBigEndian(packet, 10, 1.0f)) return 14;
    float restored = 0.0f;
    if (!BinaryPrimitives::TryReadFloatBigEndian(packet, 4, ref restored) || restored != 1.0f) return 15;
    char[16] digits;
    if (!IntegerFormatting::TryFormatHex((sbyte)-1, digits, 16, ref written, 4, false) || written != 4) return 16;
    if (digits[0] != '0' || digits[1] != '0' || digits[2] != 'f' || digits[3] != 'f') return 17;
    uint parsed = 0u;
    if (!TryParseHexUInt("0x2a", &parsed) || parsed != 42u || ParseHexUInt("2a") != 42u) return 18;
    BitSet occupancy = new(65); occupancy.SetRange(0, 65, true);
    if (!occupancy.HasAllSet() || !occupancy.HasAnySet()) return 19;
    Arena scratch = new(); byte* cleared = (byte*)scratch.AllocateZeroed(4);
    for (int i = 0; i < 4; i++) if (cleared[i] != (byte)0) return 20;
    if (!writer.TryWrite("!") || writer.Length != 7) return 21;
    PseudoRandom random = new(42ul); float fraction = random.NextFloat();
    if (fraction < 0.0f || fraction >= 1.0f) return 22;
    int[3] sorted = [1, 2, 3]; Span<int> searchValues = new(sorted, 3);
    if (BinarySearch<int>(searchValues.AsReadOnly(), &search, Comparer<int>::Compare) != 2) return 23;
    int iterations = 99;
    for (iterations = 0; iterations < 3; iterations++) {}
    if (iterations != 3) return 24;
    bool enabled = false;
    if (!TryParseBool("True", &enabled) || !enabled || ParseBool("false")) return 25;
    char[3] terminated;
    if (!CString::TryCopy("ok", terminated, 3) || !CString::Equals("ok", terminated)) return 26;
    if (Math::Sqrt(4.0f) != 2.0f || Math::Sqrt(9.0d) != 3.0d) return 27;
    if (Math::CopySign(2.0f, -0.0f) != -2.0f || Math::CopySign(-3.0d, 0.0d) != 3.0d) return 31;
    for (int ticks = 0; ticks < 3; Tick(ref ticks)) {}
    if (smokeDestroyed != 3) return 28;
    for (new SmokeTemporary(); smokeDestroyed < 4;) return 30;
    if (smokeDestroyed != 4) return 29;
    Vector<int> window = new(); window.Add(1); window.Add(2); window.Add(3);
    window.RemoveRange(1, 1);
    if (window.Count != 2 || window[0] != 1 || window[1] != 3) return 32;
    window.AsSpan().Fill(9);
    if (window[0] != 9 || window[1] != 9) return 33;
    window.ShrinkToFit();
    if (window.Capacity != window.Count || window[0] != 9 || window[1] != 9) return 38;
    int[1] extra = [5]; Span<readonly(int)> extraRange = new(extra, 1);
    for (int i = 0; i < 3; i++) window.AddRange(extraRange);
    if (window.Count != 5 || window.Capacity != 8 || window[4] != 5) return 41;
    if (window.RemoveAll(SmokeRemove) != 2 || window.Count != 3 || window[0] != 5 || window[2] != 5) return 45;
    JsonReader document = new("{\"ok\":true}", 11); int tokens = 0;
    while (document.Read()) tokens++;
    if (tokens != 4) return 34;
    JsonReader invalid = new("[1,]", 4); bool jsonRejected = false;
    try { while (invalid.Read()) {} }
    catch (JsonParseException* error) { jsonRejected = error.Offset == 3; }
    if (!jsonRejected) return 35;
    String structuredOutput = new(); JsonWriter structuredWriter = new(&structuredOutput);
    structuredWriter.WriteStartArray(); structuredWriter.WriteNumber(42);
    structuredWriter.WriteBoolean(true); structuredWriter.WriteNull();
    structuredWriter.WriteEndArray(); structuredWriter.Complete();
    if (!CString::Equals(structuredOutput.Data, "[42,true,null]")) return 36;
    uint signedEncoding = VarInt::ZigZagEncode(-42);
    if (signedEncoding != 83u || VarInt::ZigZagDecode(signedEncoding) != -42) return 37;
    char[32] wideHex; nuint wideLength = 0;
    if (!IntegerFormatting::TryFormatHex((extralong)1 << 127, wideHex, 32, ref wideLength) || wideLength != 32) return 42;
    if (wideHex[0] != '8') return 43;
    for (nuint i = 1; i < 32; i++) if (wideHex[i] != '0') return 44;
    SortedMap<int, int> sortedOwners = new(Comparer<int>::Compare); sortedOwners.Add(3, 42);
    SortedMap<int, int>::Entry transferred = sortedOwners.Extract(&search);
    if (sortedOwners.Count != 0 || *transferred.Key != 3 || transferred.Value != 42) return 46;
    smokeVector = &window;
    if (smokeVector!.Count != 3) return 47;
    smokeVector = null;
    String viewed = s"abc"; Span<char> writableView = viewed.AsSpan(); writableView[1] = 'B';
    Span<readonly(char)> borrowedView = viewed.AsReadOnly();
    if (viewed.Length != 3 || borrowedView.Length != 3 || borrowedView[1] != 'B') return 48;
    Date leapDay = new(2024, 2, 29); Date nextDay = leapDay.AddDays(1);
    String calendarText = s$"{nextDay}";
    if (nextDay.Year != 2024 || nextDay.Month != 3 || nextDay.Day != 1 || !CString::Equals(calendarText.Data, "2024-03-01")) return 49;
    Date monthEnd = new(2024, 1, 31);
    if (leapDay.AddYears(1).Day != 28 || monthEnd.AddMonths(1).Day != 29) return 51;
    Unique<int> firstOwner = new(new* int(41));
    readonly(Unique<int>)* borrowedOwner = &firstOwner;
    borrowedOwner.Value++;
    Unique<int> transferredOwner = move firstOwner;
    if (transferredOwner.IsEmpty || transferredOwner.Value != 42) return 50;
    Vector<String> fragments = new(); fragments.Add(s"left"); fragments.Add(s"right");
    String joined = fragments.AsImmutable().Join(",");
    if (!CString::Equals(joined.Data, "left,right")) return 52;
    Date parsedDate = Date::Parse("2024-02-29");
    if (parsedDate.DayNumber != leapDay.DayNumber) return 53;
    ushort[3] utf16Input = [(ushort)65, (ushort)0, (ushort)233];
    String utf8Text = UnicodeText::FromUtf16(utf16Input, 3);
    Vector<ushort> utf16Output = UnicodeText::ToUtf16(utf8Text.Data, utf8Text.Length);
    if (utf8Text.Length != 4 || utf16Output.Count != 3 || utf16Output[0] != (ushort)65 || utf16Output[1] != (ushort)0 || utf16Output[2] != (ushort)233) return 54;
    HashSet<int> overlap = new(); overlap.Add(1); overlap.Add(2);
    HashSet<int> otherSet = new(); otherSet.Add(2); otherSet.Add(3);
    overlap.IntersectWith(&otherSet); int shared = 2;
    if (overlap.Count != 1 || !overlap.Contains(&shared)) return 55;
    overlap.SymmetricExceptWith(&otherSet); int different = 3;
    if (overlap.Count != 1 || !overlap.Contains(&different) || otherSet.Count != 2) return 56;
    readonly(char)* skipDocument = "{\"ignored\":[1,{\"x\":2}],\"kept\":3}";
    JsonReader skipped = new(skipDocument, CString::Length(skipDocument));
    skipped.Read(); skipped.Read(); skipped.Skip();
    if (skipped.Kind != JsonTokenKind::EndArray || skipped.Depth != 1) return 57;
    skipped.Read(); skipped.Read();
    if (skipped.Kind != JsonTokenKind::Number || skipped.TokenLength != 1) return 58;
    skipped.Read(); if (skipped.Read()) return 59;
    String indented = new(); JsonWriter pretty = new(&indented, true);
    pretty.WriteStartObject(); pretty.WritePropertyName("values"); pretty.WriteStartArray();
    pretty.WriteNumber(1); pretty.WriteNumber(2); pretty.WriteEndArray(); pretty.WriteEndObject(); pretty.Complete();
    if (!CString::Equals(indented.Data, "{\n  \"values\": [\n    1,\n    2\n  ]\n}")) return 60;
    readonly(char)* spaced = " { \"escaped\" : \"\\u0061\\/\", \"number\" : 1.00e+03 } ";
    String compactJson = new(); JsonText::Minify(spaced, CString::Length(spaced), &compactJson);
    if (!CString::Equals(compactJson.Data, "{\"escaped\":\"\\u0061\\/\",\"number\":1.00e+03}")) return 61;
    BitSet shifted = new(65); shifted[0] = true; shifted[33] = true;
    shifted.ShiftLeft(31); shifted.ShiftRight(32);
    if (shifted.CountSetBits() != 1 || !shifted[32]) return 62;
    shifted.Resize(96); if (shifted.CountSetBits() != 1) return 63;
    Date weekdayDate = new(2024, 2, 29);
    if (weekdayDate.DayOfWeek != DayOfWeek::Thursday || weekdayDate.DayOfYear != 60) return 64;
    {
        byte[4] batchPacket = [(byte)1, (byte)2, (byte)3, (byte)4];
        BinaryBufferWriter batchOutput = new(batchPacket, 4);
        if (!batchOutput.TrySkip(1) || !batchOutput.TryWriteBytes(batchPacket, 3)
            || batchPacket[2] != (byte)2 || batchPacket[3] != (byte)3) return 65;
        BinaryBufferReader batchInput = new(batchPacket, 4);
        if (!batchInput.TryReadBytes(&batchPacket[1], 3) || batchPacket[2] != (byte)1 || batchPacket[3] != (byte)2) return 66;
        char[3] batchUrl; char[2] batchDecoded; nuint batchWritten = 0;
        char[2] batchBytes = [(char)251, (char)255];
        if (!Base64Url::TryEncode(batchBytes, 2, batchUrl, 3, ref batchWritten)
            || batchWritten != 3 || batchUrl[0] != '-' || batchUrl[1] != '_' || batchUrl[2] != '8') return 67;
        if (!Base64Url::TryDecode(batchUrl, 3, batchDecoded, 2, ref batchWritten)
            || batchWritten != 2 || batchDecoded[0] != batchBytes[0] || batchDecoded[1] != batchBytes[1]) return 68;
        int batchKey = 1;
        HashMap<int, Unique<int>> batchOwners = new();
        batchOwners.Add(1, new Unique<int>(new* int(41)));
        readonly(HashMap<int, Unique<int>>)* batchShallow = &batchOwners;
        batchShallow.FindReadOnly(&batchKey)!.Value = 42;
        immutable(HashMap<int, Unique<int>>)* batchDeep = &batchOwners;
        if (batchDeep.FindImmutable(&batchKey)!.ValueImmutable != 42) return 69;
        SortedMap<int, Unique<int>> batchSorted = new(Comparer<int>::Compare);
        batchSorted.Add(1, new Unique<int>(new* int(11)));
        readonly(SortedMap<int, Unique<int>>)* batchSortedShallow = &batchSorted;
        batchSortedShallow.FindReadOnly(&batchKey)!.Value = 12;
        immutable(SortedMap<int, Unique<int>>)* batchSortedDeep = &batchSorted;
        if (batchSortedDeep.FindImmutable(&batchKey)!.ValueImmutable != 12) return 70;
        extralong batchWide = (extralong)1 << 100;
        extralong batchMinimum = Math::Min(batchWide + 1, batchWide + 2);
        extralong batchMaximum = Math::Max(-batchWide - 1, -batchWide - 2);
        extralong batchClamped = Math::Clamp(batchWide + 3, batchWide + 1, batchWide + 2);
        extralong batchAbsolute = Math::Abs(-batchWide - 7);
        if (batchMinimum != batchWide + 1 || batchMaximum != -batchWide - 1
            || batchClamped != batchWide + 2 || batchAbsolute != batchWide + 7) return 71;
        if (Math::Truncate(-1.75f) != -1.0f || Math::Floor(-1.25) != -2.0
            || Math::Ceiling(1.25f) != 2.0f || Math::Round(2.5) != 2.0 || Math::Round(3.5f) != 4.0f) return 72;
        extralong batchParsed = ParseExtraLong("-170141183460469231731687303715884105728");
        if (batchParsed != ((extralong)1 << 127)) return 73;
        char[41] batchDecimal; nuint batchDecimalCount = 0;
        if (!IntegerFormatting::TryFormat(batchParsed, batchDecimal, 40, ref batchDecimalCount)
            || batchDecimalCount != 40) return 74;
        batchDecimal[40] = '\0';
        String batchInterpolated = s$"{batchParsed}";
        if (!CString::Equals(batchDecimal, batchInterpolated.Data)) return 75;
        byte[16] batchWideBytes;
        if (!BinaryPrimitives::TryWriteExtraLongBigEndian(batchWideBytes, 16, batchParsed)) return 76;
        extralong batchRoundtrip = (extralong)0;
        if (!BinaryPrimitives::TryReadExtraLongBigEndian(batchWideBytes, 16, ref batchRoundtrip)
            || batchRoundtrip != batchParsed) return 77;
        String batchSpaced = s"  left , right  ";
        immutable(TextParts) batchParts = batchSpaced.AsImmutable().Trim().Split(',');
        nuint batchPartLength = 0; int batchPartCount = 0;
        foreach (Span<readonly(char)> part in batchParts) {
            batchPartLength += part.Trim().Length; batchPartCount++;
        }
        if (batchPartCount != 2 || batchPartLength != 9) return 78;
        immutable(IntRange) batchRange = new(0, 10);
        var batchQuery = batchRange.Where((int value) => value % 2 == 0).AsEnumerable().Take(2);
        if (batchQuery.Aggregate(0, (int total, int value) => total + value) != 2) return 79;
        if (!CString::IsNullOrEmpty(null) || CString::IsNullOrEmpty(batchSpaced.Data)) return 80;
        if (std::Hashing::Integer(4294967297ul) == std::Hashing::Integer(0ul)) return 81;
    }
    return 0;
}
'@ | Set-Content -LiteralPath $expandedStd
    $allSources = @(Get-ChildItem -LiteralPath $std -Recurse -Filter '*.gf' -File | ForEach-Object FullName)
    & $compiler $expandedStd @allSources --optimization 2 --run -o (Join-Path $extracted 'expanded-std.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged expanded standard library smoke test failed' }
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'ItemTemplates/gflat-source/SourceFile.vstemplate'))) { throw 'VSIX is missing the source item template' }
    if ((Get-Content -Raw -LiteralPath (Join-Path $extracted 'gflat.pkgdef')) -notmatch '"ProjectFactoryPackage"="\{3347BEE8-D7A1-4082-95E4-38A439553CC2\}"') { throw 'Project factory must be registered with CPS' }
    dotnet msbuild (Join-Path $repoRoot 'examples/hello/hello.gfproj') -t:Check "-p:GflatBundledCompiler=$compiler" -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'MSBuild could not invoke the bundled compiler' }
    & $compiler check (Join-Path $repoRoot 'examples/hello/hello.gfproj')
    if ($LASTEXITCODE -ne 0) { throw 'Bundled compiler project check failed' }
    & $compiler build (Join-Path $repoRoot 'examples/hello/hello.gfproj') --emit-ir -o (Join-Path $extracted 'hello.ll')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $extracted 'hello.ll'))) { throw 'Bundled compiler project build failed' }
    & $compiler build (Join-Path $repoRoot 'examples/interpolation/interpolation.gfproj') --emit-ir -o (Join-Path $extracted 'interpolation.ll')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $extracted 'interpolation.ll'))) { throw 'Bundled compiler interpolation/std integration failed' }
    & $compiler build (Join-Path $repoRoot 'examples/values/values.gfproj') --emit-ir -o (Join-Path $extracted 'values.ll')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $extracted 'values.ll'))) { throw 'Bundled compiler owning-vector integration failed' }
}
finally {
    $env:GFLAT_LSP_EXECUTABLE = $oldExecutable
    $env:GFLAT_EDITOR_ASSETS = $oldEditorAssets
    $env:GFLAT_HOVER_CATALOG = $oldHoverCatalog
    $env:GFLAT_HOVER_CAPTURE = $oldHoverCapture
    # Only remove this run's unique extraction directory under the selected test root.
    $resolved = [IO.Path]::GetFullPath($extracted)
    $allowedRoot = [IO.Path]::GetFullPath($testRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

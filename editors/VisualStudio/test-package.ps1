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
        foreach ($source in @('core/TypeInfo.gf', 'core/Attribute.gf', 'core/Exception.gf', 'libc/Exceptions/unhandled.gf', 'core/ITextWriter.gf', 'core/IFormattable.gf', 'libc/Memory/allocation.gf')) {
            if (-not (Test-Path -LiteralPath (Join-Path $extracted "$tool/Std/$source"))) { throw "VSIX is missing $tool/Std/$source" }
        }
    }
    # The complete optional source library lives in the project SDK. Compiler and
    # server Std folders contain only the built-in runtime contract subset.
    foreach ($source in @('core/Text/String.gf', 'core/Text/ByteSearch.gf', 'core/Collections/Deque.gf', 'core/Collections/SortedSet.gf', 'core/Collections/HashMap.gf', 'core/Collections/hash_set_algebra.gf', 'core/Json/JsonString.gf', 'core/Json/json_string_writer.gf', 'core/Binary/VarInt.gf', 'core/Binary/BinaryPrimitives.gf', 'core/Binary/BinaryBufferReader.gf', 'core/Binary/BinaryBufferWriter.gf', 'core/Text/IntegerFormatting.gf')) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted "Sdk/std/$source"))) { throw "VSIX is missing Sdk/std/$source" }
    }
    foreach ($source in @('std/core/Version.gf', 'std/core/Time/DateTimeOffset.gf', 'std/core/Numerics/Rational.gf', 'std/core/Queries/enumerable_extrema_by.gf', 'std/core/Queries/enumerable_count_by.gf', 'std/core/Queries/enumerable_to_hash_map.gf', 'std/core/Queries/enumerable_to_lookup.gf', 'std/core/Queries/enumerable_order.gf', 'std/core/Queries/OrderSource.gf', 'std/core/Collections/default_ordering.gf', 'std/core/Collections/default_equality.gf', 'std/core/Memory/alignment.gf', 'std/core/Time/IMonotonicClock.gf', 'std/core/Time/Stopwatch.gf', 'libraries/platform/windows/Windows.gfproj', 'libraries/platform/windows/PerformanceCounter.gf', 'libraries/platform/linux/Linux.gfproj', 'libraries/platform/linux/MonotonicClock.gf')) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted "Sdk/$source"))) { throw "VSIX is missing Sdk/$source" }
    }
    foreach ($platform in @('windows', 'linux')) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted "Sdk/libraries/platform/$platform/SystemClock.gf"))) {
            throw "Packaged SDK is missing the $platform UTC clock"
        }
    }
    $buildInfo = Get-Content -Raw -LiteralPath (Join-Path $extracted 'BUILD-INFO.json') | ConvertFrom-Json
    foreach ($source in @('SystemRandom.gf', 'WindowsRandom.gfproj', 'README.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted "Sdk/libraries/platform/windows-random/$source"))) {
            throw "Packaged SDK is missing windows-random/$source"
        }
    }
    foreach ($source in @('SystemRandom.gf', 'LinuxRandom.gfproj', 'README.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted "Sdk/libraries/platform/linux-random/$source"))) {
            throw "Packaged SDK is missing linux-random/$source"
        }
    }
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
    $imports = Join-Path $extracted 'private-native-imports.gf'
    @'
private extern void*? malloc(nuint bytes);
private extern void free(void* storage);
int main() {
    void*? own = malloc(8); if (own == null) return 1; free(own!);
    void*? normal = Memory::Allocate(8); if (normal == null) return 2;
    ((byte*)normal!)[0] = (byte)42;
    if (((byte*)normal!)[0] != (byte)42) return 3;
    Memory::Free(normal!); return 0;
}
'@ | Set-Content -LiteralPath $imports
    & $compiler $imports --optimization 2 --run -o (Join-Path $extracted 'private-native-imports.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged private/native allocator import coexistence failed' }

    $invalidNumber = Join-Path $extracted 'invalid-unicode-number.gf'
    [IO.File]::WriteAllText($invalidNumber, 'int main(){float value=1' + [char]0x0662 + 'f;return 0;}')
    $oldNumericErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $numericDiagnostics = & $compiler $invalidNumber --emit-ir -o (Join-Path $extracted 'invalid-unicode-number.ll') 2>&1
        $numericExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $oldNumericErrorAction }
    $numericDiagnosticText = $numericDiagnostics -join "`n"
    if ($numericExitCode -ne 1 -or $numericDiagnosticText -notmatch 'error GF1000:' -or
        $numericDiagnosticText -notmatch 'Unexpected character' -or $numericDiagnosticText -match 'FormatException') {
        throw 'Packaged compiler did not report malformed Unicode numeric text as a source error'
    }

    $invalidLocal = Join-Path $extracted 'reserved-local-name.gf'
    [IO.File]::WriteAllText($invalidLocal, 'int main(){int* alias=null;return 0;}')
    $oldLocalErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $localDiagnostics = & $compiler $invalidLocal --check 2>&1
        $localExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $oldLocalErrorAction }
    $localDiagnosticText = $localDiagnostics -join "`n"
    if ($localExitCode -ne 1 -or $localDiagnosticText -notmatch '\(1,17\): error GF0005:' -or
        $localDiagnosticText -notmatch 'Expected Identifier' -or $localDiagnosticText -match "Unexpected token 'int'") {
        throw 'Packaged compiler did not locate the reserved local name after its type'
    }

    $scopedCast = Join-Path $extracted 'imported-cast.gf'
    @'
using PackageCast;
namespace PackageCast {
    public struct Box<T> {
        public struct Item { public T Value; public Item(T value){Value=move value;} }
    }
}
int main(){Box<int>::Item item=new(42);void* raw=(void*)&item;Box<int>::Item* result=(Box<int>::Item*)raw;return result.Value-42;}
'@ | Set-Content -LiteralPath $scopedCast
    & $compiler $scopedCast --optimization 2 --run -o (Join-Path $extracted 'imported-cast.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged compiler lost the checked imported nested cast target' }

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
    $consoleResult = & $compiler $console (Join-Path $std 'core/IO/IOException.gf') (Join-Path $std 'core/IO/EndOfStreamException.gf') (Join-Path $std 'core/Text/BufferTooSmallException.gf') (Join-Path $std 'libc/Console/Console.gf') --run -o (Join-Path $extracted 'console.exe')
    if ($LASTEXITCODE -ne 0 -or $consoleResult -ne 'console') { throw 'Packaged Console/std bridge smoke test failed' }
    $collections = Join-Path $extracted 'collections.gf'
    @'
using std::Text;
using std; using std::Queries; using std::Collections;
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
    $querySources = @('Collections', 'Queries' | ForEach-Object { Get-ChildItem -LiteralPath (Join-Path $bundledStd $_) -Filter '*.gf' -File | ForEach-Object FullName })
    $contractSources = @('IHashable.gf', 'IEquatable.gf', 'IComparable.gf', 'Hashing/primitive_hashing.gf') | ForEach-Object { Join-Path $bundledStd $_ }
    & $compiler $collections (Join-Path $bundledStd 'Memory/Span.gf') (Join-Path $bundledStd 'Text/String.gf') (Join-Path $bundledStd 'Text/ByteSearch.gf') (Join-Path $bundledStd 'IStringConvertable.gf') @contractSources @querySources --run -o (Join-Path $extracted 'collections.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged collection iteration and owning-element copy smoke test failed' }
    $expandedStd = Join-Path $extracted 'expanded-std.gf'
    @'
using std; using std::Text; using std::Csv; using std::Json; using std::IO; using std::Encoding; using std::Time; using std::Net; using std::Memory; using std::Binary; using std::Hashing; using std::Numerics; using std::Queries; using std::Collections;
int smokeDestroyed = 0;
long smokeTimestamp = 0l;
static class SmokeClock : IMonotonicClock {
    public static long GetTimestamp() => smokeTimestamp;
    public static long GetFrequency() => 3l;
}
Vector<int>*? smokeVector = null;
struct SmokeTemporary {
    public int Value;
    public SmokeTemporary() { Value = 42; }
    ~SmokeTemporary() { smokeDestroyed++; }
}
SmokeTemporary Tick(ref int value) { value++; return new SmokeTemporary(); }
bool SmokeRemove(readonly(int)* value) => *value == 9;
long SmokeMagnitude(int value) { if (value < 0) return -(long)value; return (long)value; }
bool SmokeRangeFailure(int value) { throw new Exception("Range predicate evaluated", 77); }
Unique<int> SmokeOwnedFold(Unique<int> total, int value) { total.Value += value; return move total; }
Unique<int> SmokeFreshOwner(int value) => new Unique<int>(new* int(value));
int SmokeCore() {
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
    if (!std::Text::Utf8::IsValid(writer.Data, writer.Length)) return 4;
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
    return 0;
}
int SmokeBinaryAndQueries() {
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
        JsonReader batchToken = new("\"na\\u006de\"", 11);
        if (!batchToken.Read() || !batchToken.ValueTextEquals("name", 4)) return 82;
        Span<readonly(char)> batchRaw = batchToken.RawToken;
        if (batchRaw.Length != 11 || !batchRaw.StartsWith("\"na", 3)
            || !batchRaw.EndsWith("e\"")) return 83;
        BinaryBufferWriter batchCursorOutput = new(batchWideBytes, 16);
        BinaryBufferReader batchCursorInput = new(batchWideBytes, 16);
        if (!batchCursorOutput.TryWriteExtraLongLittleEndian(batchParsed)
            || !batchCursorInput.TryReadExtraLongLittleEndian(ref batchRoundtrip)
            || batchRoundtrip != batchParsed) return 84;
        immutable(Utf8Scalars) batchScalars = new("ABC", 3);
        if (batchScalars.AsEnumerable().Select((uint scalar) => (int)scalar)
            .Aggregate(0, (int total, int scalar) => total + scalar) != 198) return 85;
        ushort[3] batchUnits = [(ushort)65, (ushort)0, (ushort)90];
        immutable(Utf16Scalars) batchWideScalars = new(batchUnits, 3);
        if (batchWideScalars.AsEnumerable().Select((uint scalar) => (int)scalar)
            .Aggregate(0, (int total, int scalar) => total + scalar) != 155) return 86;
        Uuid batchIdentifier = Uuid::Parse("00112233-4455-6677-8899-AABBCCDDEEFF");
        char[36] batchIdentifierText; nuint batchIdentifierLength = 0;
        if (batchIdentifier.High != 0x0011223344556677ul || batchIdentifier.Low != 0x8899aabbccddeefful
            || !batchIdentifier.TryFormat(batchIdentifierText, 36, ref batchIdentifierLength)
            || batchIdentifierLength != 36) return 87;
        BitSet batchSelected = new(8); batchSelected[1] = true; batchSelected[3] = true;
        immutable(BitSet)* batchSelectedView = &batchSelected;
        if (batchSelectedView.AsEnumerable().Aggregate((nuint)0, (nuint total, nuint bit) => total + bit) != 4) return 88;
        var batchPairs = new IntRange(1, 3).AsEnumerable().Zip(new IntRange(10, 2).AsEnumerable(),
            (int first, int second) => first + second);
        if (batchPairs.Aggregate(0, (int total, int value) => total + value) != 24) return 89;
        if (Hashing::Combine(1, 2) != (nuint)2141140832u) return 90;
        Span<readonly(char)> batchSetting = new("mode=fast=checked", 17);
        Span<readonly(char)> batchSettingKey = new(); Span<readonly(char)> batchSettingValue = new();
        if (!batchSetting.TrySplitOnce('=', ref batchSettingKey, ref batchSettingValue)
            || batchSettingKey.Length != 4 || batchSettingValue.Length != 12
            || !batchSettingValue.StartsWith("fast=checked")) return 91;
        byte[16] batchVariableBytes; BinaryBufferWriter batchVariableOutput = new(batchVariableBytes, 16);
        if (!batchVariableOutput.TryWriteVarUInt32(4294967295u)
            || !batchVariableOutput.TryWriteVarUInt64(18446744073709551615ul)
            || batchVariableOutput.Position != 15) return 92;
        BinaryBufferReader batchVariableInput = new(batchVariableBytes, batchVariableOutput.Position);
        uint batchVariableSmall = 0u; ulong batchVariableLarge = 0ul;
        if (batchVariableInput.ReadVarUInt32(ref batchVariableSmall) != VarInt::Status::Done
            || batchVariableSmall != 4294967295u
            || batchVariableInput.ReadVarUInt64(ref batchVariableLarge) != VarInt::Status::Done
            || batchVariableLarge != 18446744073709551615ul || batchVariableInput.Remaining != 0) return 93;
        batchVariableOutput.Reset();
        if (!batchVariableOutput.TryWriteZigZagInt32(-2147483647 - 1)
            || !batchVariableOutput.TryWriteZigZagInt64(-9223372036854775807l - 1l)
            || batchVariableOutput.Position != 15) return 160;
        batchVariableInput = new(batchVariableBytes, batchVariableOutput.Position);
        int batchVariableSignedSmall = 0; long batchVariableSignedLarge = 0l;
        if (batchVariableInput.ReadZigZagInt32(ref batchVariableSignedSmall) != VarInt::Status::Done
            || batchVariableSignedSmall != (-2147483647 - 1)
            || batchVariableInput.ReadZigZagInt64(ref batchVariableSignedLarge) != VarInt::Status::Done
            || batchVariableSignedLarge != (-9223372036854775807l - 1l)
            || batchVariableInput.Remaining != 0) return 161;
        Deque<int> batchEnds = new(); batchEnds.Reserve(4);
        batchEnds.AddLast(1); batchEnds.AddLast(2); batchEnds.AddLast(3); batchEnds.AddLast(4);
        batchEnds.RemoveFirst(); batchEnds.RemoveFirst(); batchEnds.AddLast(5); batchEnds.AddLast(6);
        if (batchEnds.AsEnumerable().Aggregate(0, (int total, int value) => total + value) != 18) return 94;
        if (new IntRange(1, 3).AsEnumerable().ElementAt(1) != 2) return 95;
        Vector<int> batchInserted = new(); batchInserted.Add(3); batchInserted.Add(1); batchInserted.Add(2);
        batchInserted.InsertRange(1, batchInserted.AsImmutable().Slice(0, 2));
        if (batchInserted.Count != 5 || batchInserted[0] != 3 || batchInserted[1] != 3
            || batchInserted[2] != 1 || batchInserted[3] != 1 || batchInserted[4] != 2) return 96;
        StableSort<int>(batchInserted.AsSpan(), Comparer<int>::Compare);
        if (batchInserted[0] != 1 || batchInserted[1] != 1 || batchInserted[2] != 2
            || batchInserted[3] != 3 || batchInserted[4] != 3) return 97;
        int batchWindowSum = 0; nuint batchWindowCount = 0;
        foreach (Span<int> window in batchInserted.AsSpan().Windows(2, 2))
        { batchWindowSum += window[0] + window[1]; batchWindowCount++; }
        if (batchWindowSum != 7 || batchWindowCount != 2) return 98;
        Span<readonly(char)> batchPath = new("/tmp/file.tar.gz", 16);
        if (!Paths::GetFileName(batchPath, PathStyle::Unix).StartsWith("file.tar.gz")
            || !Paths::GetExtension(batchPath, PathStyle::Unix).StartsWith(".gz")) return 99;
        if (BitOperations::RoundUpToPowerOfTwo(9u) != 16u
            || BitOperations::RoundUpToPowerOfTwo(18446744073709551615ul) != 0ul) return 100;
    }
    return 0;
}
int SmokeTextAndOwners() {
    {
        String batchResized = s"x"; batchResized.Resize(4, 'y');
        if (batchResized.Length != 4 || batchResized[0] != 'x' || batchResized[3] != 'y'
            || batchResized.Data[4] != '\0') return 101;
        String batchOwnedQuoted = JsonStringText::Quote("a\n\0", 3);
        String batchOwnedDecoded = JsonStringText::Unquote(batchOwnedQuoted.AsReadOnly());
        batchOwnedQuoted.Clear();
        if (batchOwnedDecoded.Length != 3 || batchOwnedDecoded[0] != 'a'
            || batchOwnedDecoded[1] != '\n' || batchOwnedDecoded[2] != '\0') return 102;
        String batchWideNumberText = new(); JsonWriter batchWideNumberWriter = new(&batchWideNumberText);
        batchWideNumberWriter.WriteNumber((extralong)1 << 127); batchWideNumberWriter.Complete();
        if (!CString::Equals(batchWideNumberText.Data, "-170141183460469231731687303715884105728")) return 103;
        BigInteger batchBigFirst = BigInteger::Parse("12345678901234567890");
        BigInteger batchBigSecond = new(10l);
        BigInteger batchBigProduct = batchBigFirst.Multiply(&batchBigSecond);
        String batchBigText = batchBigProduct.ToString();
        if (!CString::Equals(batchBigText.Data, "123456789012345678900")) return 104;
        Span<readonly(char)> batchNumberToken = new("+42", 3); long batchNumberValue = 0l;
        if (!batchNumberToken.TryParseLong(&batchNumberValue) || batchNumberValue != 42l
            || batchNumberToken.ParseExtraLong() != (extralong)42) return 105;
        char[4] batchCase = ['A', 'b', '\0', (char)255];
        Ascii::ToLower(batchCase, 4); Ascii::ToUpper(batchCase, 4);
        if (batchCase[0] != 'A' || batchCase[1] != 'B' || batchCase[2] != '\0'
            || batchCase[3] != (char)255) return 106;
        ulong batchPeriod = 0ul;
        if (Math::GreatestCommonDivisor(48u, 18u) != 6u
            || !Math::TryLeastCommonMultiple(6ul, 8ul, ref batchPeriod)
            || batchPeriod != 24ul) return 107;
        char[8] batchCString;
        if (!CString::TryCopy("ok", batchCString, 8)
            || !CString::TryAppend("!", batchCString, 8)
            || !CString::Equals(batchCString, "ok!")) return 108;
        int[5] batchChunkData = [1, 2, 3, 4, 5];
        Span<int> batchChunkView = new(batchChunkData, 5);
        nuint batchChunkCount = 0; nuint batchChunkLength = 0; int batchChunkSum = 0;
        foreach (Span<int> chunk in batchChunkView.Chunks(2))
        {
            batchChunkCount++; batchChunkLength = chunk.Length;
            foreach (int value in chunk) batchChunkSum += value;
        }
        if (batchChunkCount != 3 || batchChunkLength != 1 || batchChunkSum != 15) return 109;
        Queue<int> batchQueue = new(); batchQueue.Enqueue(7); batchQueue.Enqueue(8);
        if (batchQueue.AsEnumerable().First() != 7 || batchQueue.AsEnumerable().Count() != 2) return 110;
        Stack<int> batchStack = new(); batchStack.Push(7); batchStack.Push(8);
        if (batchStack.AsEnumerable().First() != 8 || batchStack.AsEnumerable().Count() != 2) return 111;
        LinkedList<int> batchList = new(); batchList.AddLast(7); batchList.AddLast(8);
        var batchListQuery = batchList.AsEnumerable();
        LinkedList<int> batchMovedList = move batchList;
        if (batchListQuery.ElementAt(1) != 8 || batchMovedList.Count != 2) return 112;
        CsvReader batchCsv = new("\"a,b\",42\n", 9);
        char[8] batchField; nuint batchFieldWritten = 0;
        if (!batchCsv.Read() || !batchCsv.IsQuoted || batchCsv.EndOfRecord
            || !batchCsv.TryCopyValue(batchField, 8, ref batchFieldWritten)
            || batchFieldWritten != 3 || batchField[1] != ',') return 113;
        if (!batchCsv.Read() || !batchCsv.EndOfRecord
            || !batchCsv.TryCopyValue(batchField, 8, ref batchFieldWritten)) return 114;
        int batchQuantity = 0;
        if (!TryParseInt(batchField, batchFieldWritten, &batchQuantity)
            || batchQuantity != 42 || batchCsv.Read()) return 115;
        String batchCsvText = new(); CsvWriter batchCsvWriter = new(&batchCsvText);
        batchCsvWriter.WriteField("a,b", 3); batchCsvWriter.WriteField("true", 4);
        batchCsvWriter.WriteEndRecord();
        String batchCsvExpected = s"\"a,b\",true\r\n";
        if (!batchCsvText.Equals(&batchCsvExpected)) return 116;
        Span<readonly(char)> batchBooleanText = new("TrUe", 4); bool batchBoolean = false;
        if (!batchBooleanText.TryParseBool(&batchBoolean) || !batchBoolean
            || !batchBooleanText.ParseBool()) return 117;
        String batchBinaryText = s"x\0y";
        String batchUrlText = Base64UrlText::Encode(batchBinaryText.Data, batchBinaryText.Length);
        String batchUrlBytes = Base64UrlText::Decode(batchUrlText.Data, batchUrlText.Length);
        if (!batchUrlBytes.Equals(&batchBinaryText)) return 118;
        String batchPathLeft = s"a\0b";
        Span<readonly(char)> batchDrive = new("C:/", 3);
        Span<readonly(char)> batchPathRight = new("c", 1);
        if (!Paths::IsRooted(batchDrive, PathStyle::Windows)
            || !Paths::IsFullyQualified(batchDrive, PathStyle::Windows)) return 119;
        String batchJoinedPath = Paths::Join(batchPathLeft.AsReadOnly(), batchPathRight, PathStyle::Unix);
        if (batchJoinedPath.Length != 5 || batchJoinedPath[1] != '\0'
            || batchJoinedPath[3] != '/' || batchJoinedPath[4] != 'c') return 120;
        Unique<int> batchFoldSeed = new(new* int(10));
        Unique<int> batchFolded = batchQueue.AsEnumerable().Aggregate(move batchFoldSeed, SmokeOwnedFold);
        if (batchFolded.Value != 25) return 121;
        HashSet<int> batchDistinct = batchQueue.AsEnumerable().ToHashSet(); int batchDistinctKey = 7;
        if (batchDistinct.Count != 2 || !batchDistinct.Contains(&batchDistinctKey)) return 122;
        String batchTransformResult = new();
        Base64Decoder batchDecoder = new(&batchTransformResult);
        Base64Encoder batchEncoder = new(&batchDecoder);
        batchEncoder.Write(batchBinaryText.Data, 1);
        batchEncoder.Write(batchBinaryText.Data + 1, batchBinaryText.Length - 1);
        batchEncoder.Complete(); batchDecoder.Complete();
        if (!batchTransformResult.Equals(&batchBinaryText)) return 123;
        CountingWriter batchByteCount = new();
        Base64Decoder batchMeasuredDecoder = new(&batchByteCount);
        batchMeasuredDecoder.Write("eAB5", 4);
        batchMeasuredDecoder.Complete();
        if (batchByteCount.Length != 3) return 124;
        batchByteCount.Clear();
        if (batchByteCount.Length != 0) return 125;
    }
    return 0;
}
int SmokeHashesAndViews() {
    {
        int[5] batchRepeated = [3, 1, 3, 2, 1];
        Span<int> batchRepeatedView = new(batchRepeated, 5);
        Vector<int> batchOrderedUnique = batchRepeatedView.AsEnumerable().Distinct().ToVector();
        if (batchOrderedUnique.Count != 3 || batchOrderedUnique[0] != 3
            || batchOrderedUnique[1] != 1 || batchOrderedUnique[2] != 2) return 126;
        SHA256 batchHash = new();
        if (!batchHash.TryAppend("abc", 3)) return 127;
        byte[32] batchDigest = batchHash.GetHash();
        if (batchDigest[0] != (byte)0xba || batchDigest[31] != (byte)0xad) return 128;
        char[64] batchHashHex; nuint batchHashWritten = 0;
        if (!Hex::TryEncode((readonly(char)*)batchHash.GetHash(), 32,
            batchHashHex, 64, ref batchHashWritten, false)) return 129;
        if (batchHashWritten != 64 || batchHashHex[0] != 'b' || batchHashHex[1] != 'a'
            || batchHashHex[62] != 'a' || batchHashHex[63] != 'd') return 130;
        HMACSHA256 batchAuthentication = new("Jefe", 4);
        if (!batchAuthentication.TryAppend("what do ya want for nothing?", 28)) return 131;
        byte[32] batchTag = batchAuthentication.GetHash();
        if (batchTag[0] != (byte)0x5b || batchTag[31] != (byte)0x43) return 132;
        char[11] batchLabelled; BufferWriter batchLabelledSink = new(batchLabelled, 11);
        LinePrefixWriter batchLabels = new(&batchLabelledSink, "> ", 2);
        batchLabels.Write("a\n", 2); batchLabels.Write("\nb", 2);
        if (batchLabelledSink.Length != 10 || batchLabelled[0] != '>'
            || batchLabelled[4] != '>' || batchLabelled[7] != '>' || batchLabelled[9] != 'b') return 133;
        Span<readonly(char)> batchDelimited = new("left::right", 11);
        Span<readonly(char)> batchBefore = new(); Span<readonly(char)> batchAfter = new();
        if (!batchDelimited.TrySplitOnce("::", 2, ref batchBefore, ref batchAfter)
            || batchBefore.Length != 4 || batchAfter.Length != 5
            || batchBefore[0] != 'l' || batchAfter[4] != 't') return 134;
        String batchEquality = s"a\0b";
        if (!batchEquality.Equals("a\0b", 3) || batchEquality.Equals("a")) return 135;
        BitSet batchOverlapLeft = new(65); BitSet batchOverlapRight = new(65);
        batchOverlapLeft.SetRange(64, 1, true);
        if (batchOverlapLeft.Overlaps(&batchOverlapRight)) return 136;
        batchOverlapRight.SetRange(64, 1, true);
        if (!batchOverlapLeft.Overlaps(&batchOverlapRight)) return 137;
    }
    {
        BigInteger batchDividend = BigInteger::Parse("-18446744073709551617");
        int batchRemainder = 0;
        BigInteger batchQuotient = batchDividend.DivRem(16, ref batchRemainder);
        String batchQuotientText = batchQuotient.ToString();
        if (batchRemainder != -1 || !batchQuotientText.Equals("-1152921504606846976")) return 138;
    }
    {
        BigInteger parsedInteger = new(9l);
        if (!BigInteger::TryParse("+00018446744073709551616", ref parsedInteger)) return 155;
        String parsedIntegerText = parsedInteger.ToString();
        if (!parsedIntegerText.Equals("18446744073709551616")) return 156;
        if (BigInteger::TryParse("12x", ref parsedInteger) || parsedInteger.Sign != 0) return 157;
    }
    {
        BigInteger powerBase = new(-2l);
        BigInteger powerValue = powerBase.Pow(65u);
        String powerText = powerValue.ToString(); String baseText = powerBase.ToString();
        if (!powerText.Equals("-36893488147419103232") || !baseText.Equals("-2")) return 162;
        BigInteger zerothPower = powerBase.Pow(0u);
        String zerothText = zerothPower.ToString();
        if (!zerothText.Equals("1")) return 163;
    }
    {
        BigInteger nativeLimit = BigInteger::Parse("18446744073709551615");
        ulong nativeUnsigned = 0ul;
        if (!nativeLimit.TryToULong(ref nativeUnsigned) || nativeUnsigned != 18446744073709551615ul
            || nativeLimit.ToULong() != nativeUnsigned) return 164;
        long nativeSigned = 9l;
        if (nativeLimit.TryToLong(ref nativeSigned) || nativeSigned != 0l) return 164;
    }
    {
        int[4] queryValues = [1, 2, 3, 4];
        Span<int> queryView = new(queryValues, 4);
        var searchedQuery = queryView.AsEnumerable();
        int searchKey = 3; int absentKey = 9;
        if (!searchedQuery.Any((int value) => value == 3) || !searchedQuery.Contains(&searchKey)
            || searchedQuery.Contains(&absentKey)) return 165;
        if (!searchedQuery.Contains(&absentKey, (immutable(int)* left, immutable(int)* right) => *left % 2 == *right % 2)) return 165;
        if (!queryView.AsImmutable().AsEnumerable().Contains(&searchKey,
            (immutable(int)* left, immutable(int)* right) => *left == *right)) return 166;
        if (searchedQuery.Count((int value) => value % 2 == 0) != 2) return 169;
        if (!searchedQuery.SequenceEqual(searchedQuery) || searchedQuery.SequenceEqual(searchedQuery.Take(3))) return 170;
        bool rangeFailure = false;
        try { new IntRange(0, 5).Where(SmokeRangeFailure).Take(1).Skip(1).Any(); }
        catch (ref Exception error) { rangeFailure = error.GetCode() == 77; }
        if (!rangeFailure) return 171;
        int indexedValue = 0;
        if (!searchedQuery.TryElementAt(2, ref indexedValue) || indexedValue != 3
            || searchedQuery.TryElementAt(4, ref indexedValue) || indexedValue != 3) return 172;
        var reversedQuery = searchedQuery.Reverse();
        if (reversedQuery.ElementAt(0) != 4
            || !reversedQuery.SequenceEqual(searchedQuery.Select((int value) => 5 - value))) return 173;
        Rune face = new(0x1f600u); String glyph = s$"{face}";
        if (face.IsAscii || face.Utf8Length != 4 || face.Utf16Length != 2 || glyph.Length != 4
            || (byte)glyph[0] != (byte)240 || (byte)glyph[3] != (byte)128) return 174;
        Rune zero = new(); String nulGlyph = zero.ToString();
        if (!zero.IsAscii || nulGlyph.Length != 1 || nulGlyph[0] != '\0') return 175;
        String streamedHex = s"prefix:"; ITextWriter* hexSink = &streamedHex;
        char[2] rawHex = ['\0', (char)255];
        hexSink.WriteHex(rawHex, 2); hexSink.WriteHex(rawHex, 2, false);
        if (!streamedHex.Equals("prefix:00FF00ff")) return 176;
    }
    {
        Date isoDate = Date::FromIsoWeek(2020, 53, DayOfWeek::Friday);
        if (isoDate.Year != 2021 || isoDate.IsoWeekYear != 2020 || isoDate.IsoWeekOfYear != 53
            || Date::GetIsoWeeksInYear(2021) != 52) return 167;
        Deque<String> endpoints = new(); endpoints.AddLast(s"first"); endpoints.AddLast(s"last");
        String endpoint = s"old";
        if (!endpoints.TryRemoveLast(ref endpoint) || !endpoint.Equals("last")
            || !endpoints.TryRemoveFirst(ref endpoint) || !endpoint.Equals("first")
            || endpoints.TryRemoveLast(ref endpoint) || !endpoint.Equals("first")) return 168;
    }
    {
        BigInteger generalDividend = BigInteger::Parse("499999999500000000000000000");
        BigInteger generalDivisor = BigInteger::Parse("500000000999999999");
        BigInteger generalRemainder = new();
        BigInteger generalQuotient = generalDividend.DivRem(&generalDivisor, ref generalRemainder);
        if (generalQuotient.ToLong() != 999999997l || generalRemainder.ToLong() != 3999999997l) return 177;
        BigInteger aliasedQuotient = generalDividend.DivRem(&generalDivisor, ref generalDivisor);
        if (!aliasedQuotient.Equals(&generalQuotient) || generalDivisor.ToLong() != 3999999997l) return 178;
        BigInteger negative = new(-48l);
        BigInteger positive = negative.Abs();
        BigInteger negated = positive.Negate();
        if (positive.ToLong() != 48l || negated.ToLong() != -48l || negative.ToLong() != -48l) return 179;
        BigInteger other = new(18l);
        BigInteger gcd = BigInteger::GreatestCommonDivisor(&negative, &other);
        if (gcd.ToLong() != 6l || other.ToLong() != 18l) return 180;
    }
    {
        int[3] queryData = [12, 3, 2];
        Span<int> queryValues = new(queryData, 3);
        if (queryValues.AsEnumerable().Aggregate((int left, int right) => left - right) != 7) return 181;
        if (queryValues.AsEnumerable().Min() != 2 || queryValues.AsEnumerable().Max() != 12) return 182;
        if (queryValues.AsEnumerable().Min((immutable(int)* left, immutable(int)* right) => *left - *right) != 2) return 183;
        if (queryValues.AsEnumerable().Prepend(1).Append(4).Aggregate((int left, int right) => left + right) != 22) return 184;
        String prettyJson = new();
        JsonText::PrettyPrint("[1,{}]", 6, &prettyJson);
        if (!prettyJson.Equals("[\n  1,\n  {}\n]")) return 185;
        BitSet bitKey = new(65); bitKey[64] = true;
        BitSet bitCopy = bitKey;
        HashMap<BitSet, int> bitMap = new(); bitMap.Add(bitKey, 7);
        bitKey.Clear();
        if (!bitMap.ContainsKey(&bitCopy) || bitMap[&bitCopy] != 7 || bitKey.Equals(&bitCopy)) return 186;
        IpAddress subnetHost = IpAddress::Parse("192.0.2.37");
        IpNetwork subnet = IpNetwork::FromAddress(subnetHost, 24);
        IpNetwork parsedSubnet = IpNetwork::Parse("192.0.2.0/024");
        if (!subnet.Contains(&subnetHost) || !subnet.Equals(&parsedSubnet)) return 187;
        IpAddress mappedHost = IpAddress::Parse("::ffff:192.0.2.37");
        if (subnet.Contains(&mappedHost)) return 188;
        nuint literalValue = 7;
        nuint[3] nativeLiteral = [0, 1, literalValue];
        byte[3] byteLiteral = [0, 127, 255];
        if (nativeLiteral[2] != 7 || sizeof(nativeLiteral) != 3 * sizeof(nuint) || byteLiteral[2] != 255) return 189;
        String compactOwner = s"a\0b";
        compactOwner.Reserve(64); compactOwner.ShrinkToFit();
        if (compactOwner.Capacity != 3 || compactOwner.Length != 3 || compactOwner.Data[2] != 'b' || compactOwner.Data[3] != '\0') return 190;
        Vector<int> poppedValues = new(); poppedValues.Add(42); int poppedResult = -1;
        if (!poppedValues.TryPop(ref poppedResult) || poppedResult != 42 || poppedValues.TryPop(ref poppedResult) || poppedResult != 42) return 191;
        char[8] base32Encoded; char[3] base32Decoded; nuint base32Written = 0;
        if (!Base32::TryEncode("foo", 3, base32Encoded, 8, ref base32Written) || base32Written != 8 || base32Encoded[0] != 'M' || base32Encoded[7] != '=') return 192;
        if (!Base32::TryDecode(base32Encoded, 8, base32Decoded, 3, ref base32Written) || base32Written != 3 || base32Decoded[0] != 'f' || base32Decoded[2] != 'o') return 193;
        PseudoRandom fullSignedRandom = new(0ul);
        if (fullSignedRandom.NextInt() != (int)0xe220a839u) return 194;
        byte[4] delimiterData = [255, 0, 255, 1]; byte[1] delimiterMembers = [255];
        if (ByteSearch::IndexOfAny(delimiterData, 4, delimiterMembers, 1) != 0 || ByteSearch::LastIndexOfAny(delimiterData, 4, delimiterMembers, 1) != 2) return 195;
        if (ByteSearch::IndexOfAny(delimiterData, 4, delimiterMembers, 0) != 4) return 196;
        String repeatedText = new(); ITextWriter* repeatedWriter = &repeatedText;
        repeatedWriter.WriteRepeated('x', 65); repeatedWriter.WriteRepeated('\0', 2);
        if (repeatedText.Length != 67 || repeatedText.Data[64] != 'x' || repeatedText.Data[65] != '\0') return 197;
        readonly(char)* repeatedAddress = repeatedText.Data;
        repeatedWriter.WriteRepeated('x', 0);
        if (repeatedText.Length != 67 || repeatedText.Data != repeatedAddress) return 198;
        LinkedList<int> filteredNodes = new(); filteredNodes.AddLast(9);
        LinkedList<int>::Node* survivorNode = filteredNodes.AddLast(7); filteredNodes.AddLast(9);
        if (filteredNodes.RemoveAll(SmokeRemove) != 2 || filteredNodes.Count != 1 || filteredNodes.First != survivorNode || survivorNode.Value != 7) return 199;
        filteredNodes.Clear();
        if (filteredNodes.RemoveAll(SmokeRemove) != 0 || !filteredNodes.IsEmpty) return 200;
        BinaryBufferReader peekInput = new(delimiterData, 4); byte peekValue = 0;
        if (!peekInput.TryPeekByte(ref peekValue) || peekValue != 255 || peekInput.Position != 0) return 201;
        if (!peekInput.TrySkip(4) || peekInput.TryPeekByte(ref peekValue) || peekValue != 0 || peekInput.Position != 4) return 202;
        LinkedList<int> reversedNodes = new();
        LinkedList<int>::Node* originalFirst = reversedNodes.AddLast(1);
        LinkedList<int>::Node* originalLast = reversedNodes.AddLast(2);
        reversedNodes.Reverse();
        if (reversedNodes.First != originalLast || reversedNodes.Last != originalFirst || originalLast.Next != originalFirst || originalFirst.Previous != originalLast) return 203;
        if (originalLast.Previous != null || originalFirst.Next != null || originalFirst.Value != 1 || originalLast.Value != 2 || reversedNodes.Count != 2) return 204;
        float adjacentFloat = Math::BitIncrement(1.0f);
        if (adjacentFloat <= 1.0f || Math::BitDecrement(adjacentFloat) != 1.0f) return 205;
        double adjacentDouble = Math::BitDecrement(1.0);
        if (adjacentDouble >= 1.0 || Math::BitIncrement(adjacentDouble) != 1.0) return 206;
        if (BitConverter::FloatToUInt32Bits(Math::BitIncrement(-0.0f)) != 1u || BitConverter::FloatToUInt32Bits(Math::BitDecrement(0.0f)) != 0x80000001u) return 207;
        if (BitConverter::DoubleToUInt64Bits(Math::BitDecrement(BitConverter::UInt64BitsToDouble(0x7ff0000000000000ul))) != 0x7feffffffffffffful) return 208;
        String owningBase32 = Base32Text::Encode("a\0b", 3);
        if (!owningBase32.Equals("MEAGE===", 8)) return 209;
        String decodedBase32 = Base32Text::Decode(owningBase32.Data, owningBase32.Length);
        if (!decodedBase32.Equals("a\0b", 3)) return 210;
        BigInteger lcmLeft = new(-84l); BigInteger lcmRight = new(126l);
        BigInteger commonMultiple = BigInteger::LeastCommonMultiple(&lcmLeft, &lcmRight);
        if (commonMultiple.ToLong() != 252l || lcmLeft.ToLong() != -84l || lcmRight.ToLong() != 126l) return 211;
        IpNetwork parentRange = IpNetwork::Parse("192.0.2.0/24");
        IpNetwork childRange = IpNetwork::Parse("192.0.2.128/25");
        IpNetwork separateRange = IpNetwork::Parse("192.0.3.0/24");
        if (!parentRange.Contains(&childRange) || childRange.Contains(&parentRange) || !childRange.Overlaps(&parentRange)) return 212;
        if (parentRange.Overlaps(&separateRange) || separateRange.Overlaps(&childRange)) return 213;
        Repeated<int> repeatedValues = new(7, 4);
        if (repeatedValues.Count != 4 || repeatedValues.IsEmpty || repeatedValues.AsEnumerable().Take(2).Count() != 2) return 214;
        int repeatedSum = 0;
        foreach (int entry in repeatedValues) repeatedSum += entry;
        if (repeatedSum != 28) return 215;
        HashMap<int, String> transferMap = new(); transferMap.Add(1, s"kept");
        int transferKey = 1; String transferValue = s"old";
        if (!transferMap.Remove(&transferKey, ref transferValue) || !transferValue.Equals("kept") || !transferMap.IsEmpty) return 216;
        if (transferMap.Remove(&transferKey, ref transferValue) || !transferValue.Equals("kept")) return 217;
        SortedMap<int, int> transferSorted = new(Comparer<int>::Compare); transferSorted.Add(1, 42);
        int transferredValue = 9;
        if (!transferSorted.Remove(&transferKey, ref transferredValue) || transferredValue != 42 || !transferSorted.IsEmpty) return 218;
        Span<int> fallbackEmpty = new();
        if (fallbackEmpty.AsEnumerable().DefaultIfEmpty(42).Single() != 42) return 219;
        int[2] fallbackData = [7, 9]; Span<int> fallbackInput = new(fallbackData, 2);
        var fallbackQuery = fallbackInput.AsEnumerable().DefaultIfEmpty(42);
        if (fallbackQuery.Count() != 2 || fallbackQuery.First() != 7 || fallbackQuery.Last() != 9) return 220;
        readonly(char)* owningCsvInput = "\"a\"\"b\",";
        CsvReader owningCsvReader = new(owningCsvInput, CString::Length(owningCsvInput));
        String owningCsvField = s"old";
        if (owningCsvReader.HasValue || !owningCsvReader.Read() || !owningCsvReader.HasValue) return 221;
        CsvReaderText::CopyValue(&owningCsvReader, ref owningCsvField);
        if (!owningCsvField.Equals("a\"b", 3) || !owningCsvReader.Read()) return 222;
        CsvReaderText::CopyValue(&owningCsvReader, ref owningCsvField);
        if (!owningCsvField.IsEmpty || owningCsvReader.Read() || owningCsvReader.HasValue) return 223;
        String anyByteText = s"a\0b,."; char[2] anyByteSet = [',', '\0'];
        if (anyByteText.IndexOfAny(anyByteSet, 2) != 1 || anyByteText.LastIndexOfAny(anyByteSet, 2) != 3) return 224;
        if (anyByteText.IndexOfAny(",.") != 3 || anyByteText.LastIndexOfAny(",.") != 4) return 225;
        TimeOfDay batchClockValue = new(12, 34, 56, 789);
        char[17] clockText; nuint clockCount = 0;
        if (!batchClockValue.TryFormat(clockText, 16, ref clockCount) || clockCount != 16) return 226;
        clockText[clockCount] = '\0';
        if (!CString::Equals(clockText, "12:34:56.7890000")) return 227;
        TimeOfDay batchParsedClock = new();
        if (!TimeOfDay::TryParse("23:59:59.9999999", &batchParsedClock) || batchParsedClock.Ticks != 863999999999l) return 228;
        if (TimeOfDay::TryParse("24:00:00", &batchParsedClock) || batchParsedClock.Ticks != 0l) return 229;
        Uuid binaryUuid = Uuid::Parse("00112233-4455-6677-8899-aabbccddeeff");
        byte[16] uuidNetworkBytes; nuint uuidByteCount = 0;
        if (!binaryUuid.TryWriteBytes(uuidNetworkBytes, 16, ref uuidByteCount) || uuidByteCount != 16
            || uuidNetworkBytes[0] != 0 || uuidNetworkBytes[8] != 136 || uuidNetworkBytes[15] != 255) return 230;
        Uuid recoveredUuid = Uuid::FromBytes(uuidNetworkBytes, 16);
        if (!recoveredUuid.Equals(&binaryUuid)) return 231;
        if (Uuid::TryFromBytes(uuidNetworkBytes, 15, &recoveredUuid) || !recoveredUuid.IsNil) return 232;
        readonly(char)* pointerDocument = "{\"a/b\":[0,{\"name\":\"ok\"}]}";
        Span<readonly(char)> pointerInput = new(pointerDocument, CString::Length(pointerDocument));
        Span<readonly(char)> pointerValue = new();
        if (!JsonPointer::TryResolve(pointerInput, new("/a~1b/1/name", 12), ref pointerValue)
            || pointerValue.Length != 4 || pointerValue[1] != 'o' || pointerValue[2] != 'k') return 233;
        readonly(char)* pointerPrevious = pointerValue.Data!;
        if (JsonPointer::TryResolve(pointerInput, new("/missing", 8), ref pointerValue)
            || pointerValue.Data != pointerPrevious || pointerValue.Length != 4) return 234;
        readonly(char)* pointerDuplicate = "{\"x\":1,\"x\":2}";
        if (JsonPointer::TryResolve(new(pointerDuplicate, CString::Length(pointerDuplicate)), new("/x", 2), ref pointerValue)
            || pointerValue.Data != pointerPrevious) return 235;
        int[2] wideSumData = [2147483647, 1]; Span<int> wideSumInput = new(wideSumData, 2);
        if (wideSumInput.AsEnumerable().Sum() != 2147483648l) return 236;
        if (wideSumInput.AsReadOnly().AsEnumerable().Sum() != 2147483648l
            || wideSumInput.AsImmutable().AsEnumerable().Take(0).Sum() != 0l) return 237;
        long[3] overflowingSumData = [9223372036854775807l, 1l, -1l];
        Span<long> overflowingSum = new(overflowingSumData, 3); bool sumRejected = false;
        try { overflowingSum.AsEnumerable().Sum(); }
        catch (ref Exception error) { sumRejected = CString::Equals(error.GetMessage(), "Sequence sum overflow"); }
        if (!sumRejected) return 238;
        int clockDays = 99;
        TimeOfDay clockMidnight = new();
        TimeOfDay clockPrevious = clockMidnight.Add(TimeSpan::FromTicks(-1l), ref clockDays);
        if (clockDays != -1 || clockPrevious.Ticks != 863999999999l
            || clockMidnight.Ticks != 0l) return 239;
        TimeOfDay clockNext = clockPrevious.Add(TimeSpan::FromTicks(1l));
        if (clockNext.Ticks != 0l
            || !clockNext.IsBetween(new TimeOfDay(23, 0), new TimeOfDay(1, 0))
            || clockNext.IsBetween(clockNext, clockNext)) return 240;
        String trimmedPathEnding = Paths::TrimEndingDirectorySeparator("dir//", PathStyle::Unix);
        if (!trimmedPathEnding.Equals("dir/")) return 241;
        char[4] pathEndingBytes = ['a', '\0', 'b', '/'];
        Span<readonly(char)> pathEndingView = new(pathEndingBytes, 4);
        Span<readonly(char)> pathEndingTrimmed = Paths::TrimEndingDirectorySeparator(pathEndingView, PathStyle::Unix);
        if (pathEndingTrimmed.Length != 3 || pathEndingTrimmed.Data != pathEndingView.Data
            || pathEndingTrimmed[1] != '\0') return 242;
        Span<readonly(char)> noPathEnding = new();
        if (!Paths::EndsInDirectorySeparator(pathEndingView, PathStyle::Unix)
            || Paths::EndsInDirectorySeparator(noPathEnding, PathStyle::Windows)) return 243;
        BigInteger bufferFormattedInteger = BigInteger::Parse("-123456789012345678901234567890");
        char[32] bigIntegerOutput; for (nuint i = 0; i < 32; i++) bigIntegerOutput[i] = '?';
        nuint bigIntegerWritten = 99;
        if (bufferFormattedInteger.TryFormat(bigIntegerOutput, 30, ref bigIntegerWritten)
            || bigIntegerWritten != 0 || bigIntegerOutput[0] != '?') return 244;
        if (!bufferFormattedInteger.TryFormat(bigIntegerOutput, 31, ref bigIntegerWritten)
            || bigIntegerWritten != 31 || bigIntegerOutput[31] != '?') return 245;
        bigIntegerOutput[31] = '\0';
        if (!CString::Equals(bigIntegerOutput, "-123456789012345678901234567890")) return 246;
        char[5] splitAnyBytes = ['a', ',', 'b', ';', 'c'];
        char[2] splitAnySeparators = [',', ';'];
        Span<char> splitAnyText = new(splitAnyBytes, 5);
        TextPartsAny splitAnyParts = splitAnyText.SplitAny(new Span<readonly(char)>(splitAnySeparators, 2));
        splitAnySeparators[0] = 'x'; splitAnySeparators[1] = 'y';
        if (splitAnyParts.AsEnumerable().Count() != 3) return 247;
        String splitAnyOwner = s"a,,b;";
        if (splitAnyOwner.SplitAny(",;", true).AsEnumerable().Count() != 2) return 248;
        char[3] splitAnyBinary = ['a', '\0', 'b']; char[1] splitAnyZero = ['\0'];
        Span<readonly(char)> splitAnyBinaryView = new(splitAnyBinary, 3);
        if (splitAnyBinaryView.SplitAny(new Span<readonly(char)>(splitAnyZero, 1)).AsEnumerable().Count() != 2
            || splitAnyBinaryView.SplitAny("").AsEnumerable().Count() != 1) return 249;
        DateTime calendar = DateTime::Parse("2024-02-29T23:30:00");
        DateTime calendarNext = calendar.Add(TimeSpan::FromHours(2));
        if (calendarNext.Date.Month != 3 || calendarNext.Date.Day != 1
            || calendarNext.TimeOfDay.Hour != 1
            || calendarNext.Difference(calendar).Ticks != TimeSpan::FromHours(2).Ticks) return 250;
        DateTime calendarClamped = calendar.AddYears(1);
        if (calendarClamped.Date.Day != 28 || calendarClamped.TimeOfDay.Ticks != calendar.TimeOfDay.Ticks) return 251;
        char[28] calendarOutput; calendarOutput[19] = '?'; nuint calendarWritten = 0;
        if (!calendarNext.TryFormat(calendarOutput, 19, ref calendarWritten)
            || calendarWritten != 19 || calendarOutput[19] != '?') return 252;
        calendarOutput[19] = '\0';
        if (!CString::Equals(calendarOutput, "2024-03-01T01:30:00")) return 252;
        DisjointSet groups = new(6);
        if (!groups.Union(0, 1) || !groups.Union(4, 5) || groups.Union(1, 0)) return 253;
        immutable(DisjointSet)* groupView = &groups;
        if (groupView.SetCount != 4 || groupView.ElementCount != 6
            || !groupView.Connected(0, 1) || groupView.Connected(0, 4)
            || groupView.SizeOf(5) != 2) return 254;
        DisjointSet transferredGroups = move groups;
        if (transferredGroups.SetCount != 4 || transferredGroups.Find(4) != transferredGroups.Find(5)) return 255;
        Stopwatch<SmokeClock> timer = new(); timer.Start(); smokeTimestamp = 6l; timer.Stop();
        TimeSpan timerElapsed = timer.Elapsed;
        if (timer.IsRunning || timer.ElapsedTicks != 6l || timer.ElapsedSeconds != 2d
            || timerElapsed.Ticks != 20000000l) return 257;
        timer.Reset();
        if (timer.ElapsedTicks != 0l || timer.IsRunning) return 258;
        int[4] projectedValues = [-7, 3, -3, 5];
        Span<int> projectedView = new(projectedValues, 4);
        if (projectedView.AsEnumerable().MinBy(SmokeMagnitude) != 3
            || projectedView.AsEnumerable().MaxBy(SmokeMagnitude) != -7) return 259;
        Version numericVersion = Version::Parse("001.02");
        Version extendedVersion = new(1, 2, 0);
        if (numericVersion.Major != 1 || numericVersion.Minor != 2 || numericVersion.Build != -1
            || numericVersion.CompareTo(&extendedVersion) >= 0) return 260;
        char[32] formattedLineBytes;
        BufferWriter formattedLineOutput = new(formattedLineBytes, 32);
        DateTime formattedLinePrefix = DateTime::Parse("2000-01-01T00:00:00");
        LinePrefixWriter formattedLines = new(&formattedLineOutput, &formattedLinePrefix);
        formattedLines.Write("x", 1);
        if (formattedLineOutput.Length != 20 || formattedLineBytes[0] != '2'
            || formattedLineBytes[18] != '0' || formattedLineBytes[19] != 'x') return 261;
        Version ownedTextValue = new(2, 3);
        String ownedText = ownedTextValue.ToString();
        int ownedTextNumber = 42;
        String ownedNumberText = ownedTextNumber.ToString();
        if (!ownedText.Equals("2.3") || !ownedNumberText.Equals("42")) return 262;
        int[4] keyedInput = [1, 2, 3, 4];
        Span<int> keyedView = new(keyedInput, 4);
        HashMap<int, nuint> parityCounts = keyedView.AsEnumerable().CountBy((int value) => value % 2);
        int evenKey = 0; int oddKey = 1; int thirdKey = 3;
        if (parityCounts.Count != 2 || parityCounts[&evenKey] != 2 || parityCounts[&oddKey] != 2) return 263;
        HashMap<int, int> originalMap = keyedView.AsEnumerable().ToHashMap((int value) => value);
        HashMap<int, long> selectedMap = keyedView.AsEnumerable().ToHashMap((int value) => value, (int value) => (long)value * 10l);
        if (originalMap.Count != 4 || selectedMap.Count != 4 || originalMap[&thirdKey] != 3 || selectedMap[&thirdKey] != 30l) return 264;
        HashMap<int, Vector<int>> originalGroups = keyedView.AsEnumerable().ToLookup((int value) => value % 2);
        HashMap<int, Vector<long>> selectedGroups = keyedView.AsEnumerable().ToLookup((int value) => value % 2, (int value) => (long)value * (long)value);
        Span<int> oddValues = originalGroups[&oddKey].AsSpan();
        Span<long> evenSquares = selectedGroups[&evenKey].AsSpan();
        if (originalGroups.Count != 2 || selectedGroups.Count != 2 || oddValues.Length != 2 || oddValues[0] != 1 || oddValues[1] != 3
            || evenSquares.Length != 2 || evenSquares[0] != 4l || evenSquares[1] != 16l) return 265;
        Rational sixth = new(1l, 6l); Rational third = new(1l, 3l);
        Rational half = sixth.Add(&third); Rational expectedHalf = new(1l, 2l);
        String halfText = half.ToString();
        if (!half.Equals(&expectedHalf) || !halfText.Equals("1/2")) return 266;
        Deque<int> indexedDeque = new(); indexedDeque.Reserve(4);
        for (int i = 0; i < 4; i++) indexedDeque.AddLast(i);
        indexedDeque.RemoveFirst(); indexedDeque.RemoveFirst();
        indexedDeque.AddLast(4); indexedDeque.AddLast(5);
        indexedDeque.Insert(2, 9); int removedMiddle = indexedDeque.RemoveAt(3);
        if (removedMiddle != 4 || indexedDeque.Count != 4 || indexedDeque[0] != 2 || indexedDeque[1] != 3
            || indexedDeque[2] != 9 || indexedDeque[3] != 5) return 267;
        Rational parsedFraction = Rational::Parse("-12/-18");
        Rational expectedFraction = new(2l, 3l);
        if (!parsedFraction.Equals(&expectedFraction) || !Rational::TryParse("2/4tail", 3, ref parsedFraction)
            || !parsedFraction.Equals(&expectedHalf)) return 268;
        DateTimeOffset offsetStamp = DateTimeOffset::Parse("2000-01-01T02:00:00+02:00");
        DateTimeOffset utcStamp = offsetStamp.ToOffset(TimeSpan::FromTicks(0l));
        if (!offsetStamp.Equals(&utcStamp) || offsetStamp.EqualsExact(&utcStamp)
            || utcStamp.DateTime.TimeOfDay.Hour != 0) return 269;
        DateTimeOffset preEpoch = DateTimeOffset::FromUnixTimeMilliseconds(-1l);
        DateTimeOffset epochSecond = DateTimeOffset::FromUnixTimeSeconds(-1l);
        if (preEpoch.ToUnixTimeMilliseconds() != -1l || preEpoch.ToUnixTimeSeconds() != -1l
            || epochSecond.ToUnixTimeMilliseconds() != -1000l || epochSecond.Offset.Ticks != 0l) return 270;
        Vector<int> orderedInput = keyedView.AsEnumerable().Order().ToVector();
        Vector<int> descendingInput = keyedView.AsEnumerable()
            .Order((immutable(int)* first, immutable(int)* second) => *second - *first).ToVector();
        if (orderedInput.Count != 4 || orderedInput[0] != 1 || orderedInput[3] != 4
            || descendingInput.Count != 4 || descendingInput[0] != 4 || descendingInput[3] != 1) return 271;
        DateTimeOffset laterStamp = offsetStamp.Add(TimeSpan::FromMinutes(15l));
        TimeSpan elapsedStamp = laterStamp.Difference(offsetStamp);
        DateTimeOffset restoredStamp = laterStamp.Subtract(elapsedStamp);
        if (elapsedStamp.Ticks != 9000000000l || laterStamp.Offset.Ticks != offsetStamp.Offset.Ticks
            || !restoredStamp.EqualsExact(&offsetStamp)) return 272;
        int[5] defaultSorted = [3, 4, 1, 2, 0];
        Span<int> defaultSpan = new(defaultSorted, 5);
        Sort(defaultSpan);
        if (defaultSorted[0] != 0 || defaultSorted[4] != 4
            || FindLastIndex<int>(defaultSpan.AsImmutable(), (immutable(int)* item) => *item % 2 == 0) != 4) return 273;
        defaultSorted[0] = 9;
        StableSort(defaultSpan);
        if (defaultSorted[0] != 1 || defaultSorted[4] != 9) return 274;
        int contractKey = 3;
        if (!IsSorted(defaultSpan.AsImmutable()) || LowerBound(defaultSpan.AsImmutable(), &contractKey) != 2
            || UpperBound(defaultSpan.AsImmutable(), &contractKey) != 3
            || BinarySearch(defaultSpan.AsImmutable(), &contractKey) != 2) return 275;
        if (MinIndex(defaultSpan.AsImmutable()) != 0 || MaxIndex(defaultSpan.AsImmutable()) != 4
            || SequenceCompare(defaultSpan.Slice(0, 2).AsImmutable(), defaultSpan.AsImmutable()) != -1
            || !SequenceEqual(defaultSpan.AsImmutable(), defaultSpan.AsImmutable())
            || IndexOf(defaultSpan.AsImmutable(), &contractKey) != 2
            || LastIndexOf(defaultSpan.AsImmutable(), &contractKey) != 2
            || !Contains(defaultSpan.AsImmutable(), &contractKey)) return 276;
        if (Memory::AlignUp(13, 8) != 16 || Memory::AlignDown(123, 64) != 64
            || !Memory::IsAligned(16, 8) || Memory::IsAligned(16, 3)) return 277;
        const byte complementConstant = ~(byte)0;
        byte complementInput = (byte)defaultSorted[0];
        byte complementValue = ~complementInput;
        nuint nativeMask = ~(nuint)0;
        if (complementConstant != (byte)255 || complementValue != (byte)254
            || nativeMask != (nuint)0 - (nuint)1) return 278;
        IpNetwork endpointNetwork = IpNetwork::Parse("192.0.2.128/25");
        IpAddress endpoint = endpointNetwork.LastAddress;
        IpAddress expectedEndpoint = IpAddress::Parse("192.0.2.255");
        if (!endpoint.Equals(&expectedEndpoint) || !endpointNetwork.Contains(&endpoint)) return 279;
        if (Ascii::HashIgnoreCase("Content-Type", 12) != Ascii::HashIgnoreCase("content-type", 12)) return 280;
        TimeSpan fractionalDuration = TimeSpan::FromTicks(15000l);
        if (fractionalDuration.TotalMilliseconds != 1.5d
            || TimeSpan::FromDays(1l).TotalHours != 24d) return 281;
        PseudoRandom signedRandom = new(42ul);
        PseudoRandom wordRandom = new(42ul);
        if (signedRandom.NextLong() != (long)wordRandom.NextULong()) return 282;
        long signedRange = signedRandom.NextLong(-5000000000l, 5000000001l);
        if (signedRange < -5000000000l || signedRange >= 5000000001l
            || signedRandom.NextLong(1l) != 0l) return 283;
        BitSet occupiedSlots = new(65);
        occupiedSlots.SetRange(0, 64, true);
        if (occupiedSlots.FindIndex(false) != 64 || occupiedSlots.FindIndex(true, 64) != 65) return 284;
        String borrowedComparison = s"a\0b";
        if (borrowedComparison.CompareTo("a") <= 0
            || borrowedComparison.CompareTo("a\0b", 3) != 0) return 285;
        int[2] concatPrefix = [1, 2];
        Span<int> concatView = new(concatPrefix, 2);
        int concatSum = 0;
        foreach (int concatValue in concatView.AsEnumerable().Concat(new IntRange(3, 2).AsEnumerable()))
            concatSum += concatValue;
        if (concatSum != 10) return 286;
        int freshOwnerSum = 0;
        foreach (Unique<int> freshOwner in new IntRange(1, 2).AsEnumerable().Select(SmokeFreshOwner))
            freshOwnerSum += freshOwner.Value;
        if (freshOwnerSum != 3) return 287;
    }
    {
        Unique<int> batchOwner = new(new* int(7));
        int*? batchReleased = batchOwner.Release();
        if (batchReleased == null || !batchOwner.IsEmpty) return 139;
        Unique<int> batchReceiver = new(batchReleased!);
        Unique<int> batchOther = new(new* int(9));
        Swap(ref batchReceiver, ref batchOther);
        if (batchReceiver.Value != 9 || batchOther.Value != 7) return 140;
    }
    {
        TimeSpan batchDuration = TimeSpan::FromSeconds(2l).Subtract(TimeSpan::FromMilliseconds(1250l));
        if (batchDuration.Ticks != 7500000l || batchDuration.Milliseconds != 750) return 141;
        char[27] batchDurationText; BufferWriter batchDurationSink = new(batchDurationText, 27);
        batchDuration.Format(&batchDurationSink);
        if (!CString::Equals(batchDurationText, "00:00:00.7500000")) return 142;
    }
    {
        IpAddress batchAddress = IpAddress::Parse("2001:0DB8:0000:0000:0:0:0:1");
        IpAddress batchCanonical = IpAddress::Parse("2001:db8::1");
        if (!batchAddress.Equals(&batchCanonical)) return 146;
        char[40] batchAddressText; BufferWriter batchAddressSink = new(batchAddressText, 40);
        batchAddress.Format(&batchAddressSink);
        if (!CString::Equals(batchAddressText, "2001:db8::1")) return 147;
        IpAddress batchZero = new();
        if (IpAddress::TryParse("127.1", &batchAddress) || !batchAddress.Equals(&batchZero)) return 148;
    }
    {
        TimeSpan parsedDuration = TimeSpan::Parse("-1.02:03:04.5");
        if (parsedDuration.Ticks != -937845000000l) return 149;
        if (!TimeSpan::TryParse("00:00:01.25", &parsedDuration) || parsedDuration.Ticks != 12500000l) return 150;
        if (TimeSpan::TryParse("24:00:00", &parsedDuration) || parsedDuration.Ticks != 0l) return 151;
    }
    {
        byte[4] networkBytes = [(byte)192, (byte)0, (byte)2, (byte)1];
        IpAddress networkAddress = IpAddress::FromBytes(networkBytes, 4);
        if (!networkAddress.IsIpv4 || networkAddress.ByteCount != (nuint)4) return 152;
        byte[16] roundTrip; nuint byteCount = 0;
        if (!networkAddress.TryWriteBytes(roundTrip, 16, ref byteCount) || byteCount != (nuint)4
            || roundTrip[0] != (byte)192 || roundTrip[2] != (byte)2 || roundTrip[3] != (byte)1) return 153;
        IpAddress restoredAddress = new();
        if (!IpAddress::TryFromBytes(roundTrip, byteCount, &restoredAddress) || !restoredAddress.Equals(&networkAddress)) return 154;
    }
    return 0;
}
int SmokeBoundedFiles(char* path) {
    String text = FileText::ReadAll(path, 3);
    Vector<byte> bytes = FileBytes::ReadAll(path, 3);
    if (!text.Equals("a\0b", 3) || bytes.Count != 3 || bytes[1] != (byte)0) return 143;
    int rejected = 0;
    try { String excess = FileText::ReadAll(path, 2); }
    catch (ref IOException error) { if (CString::Equals(error.GetMessage(), "File exceeds maximum byte count")) rejected++; }
    try { Vector<byte> excess = FileBytes::ReadAll(path, 2); }
    catch (ref IOException error) { if (CString::Equals(error.GetMessage(), "File exceeds maximum byte count")) rejected++; }
    if (rejected != 2) return 144;
    FileBytes::WriteAll(path, ref bytes);
    FileBytes::AppendAll(path, ref bytes);
    Vector<byte> appended = FileBytes::ReadAll(path, 6);
    if (appended.Count != 6 || appended[3] != (byte)'a' || appended[4] != (byte)0 || appended[5] != (byte)'b') return 158;
    Vector<byte> empty = new();
    FileBytes::WriteAll(path, ref empty);
    Vector<byte> cleared = FileBytes::ReadAll(path, 0);
    if (cleared.Count != 0 || bytes.Count != 3) return 159;
    return 0;
}
int main(int argc, char** argv) {
    if (argc != 2) return 145;
    int result = SmokeCore(); if (result != 0) return result;
    result = SmokeBinaryAndQueries(); if (result != 0) return result;
    result = SmokeTextAndOwners(); if (result != 0) return result;
    result = SmokeHashesAndViews(); if (result != 0) return result;
    return SmokeBoundedFiles(argv[1]);
}
'@ | Set-Content -LiteralPath $expandedStd
    $allSources = @(Get-ChildItem -LiteralPath $std -Recurse -Filter '*.gf' -File | ForEach-Object FullName)
    $allSourcesList = Join-Path $extracted 'standard-library.sources'
    [IO.File]::WriteAllLines($allSourcesList, $allSources, [Text.UTF8Encoding]::new($false))
    $boundedInput = Join-Path $extracted 'bounded input.bin'
    [IO.File]::WriteAllBytes($boundedInput, [byte[]](97, 0, 98))
    & $compiler $expandedStd --source-list $allSourcesList --optimization 2 --run -o (Join-Path $extracted 'expanded-std.exe') -- $boundedInput
    if ($LASTEXITCODE -ne 0) { throw "Packaged expanded standard library smoke test failed (exit code $LASTEXITCODE)" }
    foreach ($project in @('windows/Windows.gfproj', 'linux/Linux.gfproj', 'windows-random/WindowsRandom.gfproj', 'linux-random/LinuxRandom.gfproj')) {
        & $compiler check (Join-Path $extracted "Sdk/libraries/platform/$project")
        if ($LASTEXITCODE -ne 0) { throw "Packaged optional platform project check failed: $project" }
    }
    $clockProbe = Join-Path $extracted 'clock-provider.gf'
    @'
alias Timer = std::Time::Stopwatch<Windows::PerformanceCounter>;
int main() {
    Timer timer = Timer::StartNew(); timer.Stop();
    std::Time::TimeSpan elapsed = timer.Elapsed;
    if (timer.IsRunning || timer.ElapsedTicks < 0l || elapsed.Ticks < 0l) return 1;
    std::Time::DateTime utc = Windows::SystemClock::GetUtcNow();
    if (utc.Ticks < 504911232000000000l || utc.Ticks > 3155378975999999999l) return 2;
    return 0;
}
'@ | Set-Content -LiteralPath $clockProbe
    & $compiler $clockProbe --source-list $allSourcesList (Join-Path $extracted 'Sdk/libraries/platform/windows/PerformanceCounter.gf') (Join-Path $extracted 'Sdk/libraries/platform/windows/SystemClock.gf') --optimization 2 --run -o (Join-Path $extracted 'clock-provider.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged optional Windows clock provider failed to link or run' }
    $clockConsumer = Join-Path $extracted 'clock consumer'
    New-Item -ItemType Directory -Force -Path $clockConsumer | Out-Null
    Copy-Item -LiteralPath $clockProbe -Destination (Join-Path $clockConsumer 'main.gf')
    @'
<Project DefaultTargets="Build">
  <PropertyGroup><GflatSdkRoot>../Sdk</GflatSdkRoot></PropertyGroup>
  <Import Project="$(GflatSdkRoot)/build/Gflat.props" />
  <ItemGroup><ProjectReference Include="$(GflatSdkRoot)/libraries/platform/windows/Windows.gfproj" /></ItemGroup>
  <Import Project="$(GflatSdkRoot)/build/Gflat.targets" />
</Project>
'@ | Set-Content -LiteralPath (Join-Path $clockConsumer 'clock.gfproj')
    & $compiler build (Join-Path $clockConsumer 'clock.gfproj') --configuration Release --run
    if ($LASTEXITCODE -ne 0) { throw 'Packaged SDK consumer could not build and run through the optional clock project reference' }
    $randomConsumer = Join-Path $extracted 'random consumer'
    New-Item -ItemType Directory -Force -Path $randomConsumer | Out-Null
    @'
int main()
{
    byte[32] bytes;
    if (!Windows::SystemRandom::TryFill(bytes, 32)) return 1;
    return 0;
}
'@ | Set-Content -LiteralPath (Join-Path $randomConsumer 'main.gf')
    @'
<Project DefaultTargets="Build">
  <PropertyGroup><GflatSdkRoot>../Sdk</GflatSdkRoot></PropertyGroup>
  <Import Project="$(GflatSdkRoot)/build/Gflat.props" />
  <PropertyGroup><NoStdLib>true</NoStdLib></PropertyGroup>
  <ItemGroup><ProjectReference Include="$(GflatSdkRoot)/libraries/platform/windows-random/WindowsRandom.gfproj" /></ItemGroup>
  <Import Project="$(GflatSdkRoot)/build/Gflat.targets" />
</Project>
'@ | Set-Content -LiteralPath (Join-Path $randomConsumer 'random.gfproj')
    & $compiler build (Join-Path $randomConsumer 'random.gfproj') --configuration Release --run
    if ($LASTEXITCODE -ne 0) { throw 'Packaged SDK consumer could not build and run through the optional Windows random project reference' }
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

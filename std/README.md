# Standard library

Projects include this source library by default. `NoStdLib` disables the default
reference; applications can then select individual source files or provide their
own runtime contracts. See [project setup](../docs/PROJECTS.md).

Start with [a complete text-processing program](../docs/STDLIB_GETTING_STARTED.md)
for parsing, formatting, and the ownership rules used by these APIs.

## Core

Core algorithms and data structures contain no direct libc calls. Owning types
allocate through the replaceable `Memory::Allocate`/`Free` API; its default
implementation lives in `libc`.

| Area | APIs | Details |
| --- | --- | --- |
| Text | `String`, `ITextWriter`, `IFormattable`, `IStringConvertable` | [Strings](../docs/STRINGS.md), [formatting](core/README.md#strings-and-formatting) |
| C strings | `CString` | [Borrowed null-terminated byte utilities](../docs/CSTRING.md) |
| Caller-buffer text | `BufferWriter` | [Allocation-free formatting](../docs/BUFFER_WRITER.md) |
| Writer line helpers | `ITextWriter*` `WriteLine` overloads | [Borrowed writer conveniences](../docs/TEXT_WRITERS.md) |
| Borrowed text parts | `TextParts`, String/Span `Split`, Span `TrySplitOnce`/`Trim` | [Allocation-free splitting and trimming](../docs/TEXT_PARTS.md), [key/value views](../docs/TEXT_SPLIT_ONCE.md) |
| Borrowed text matching | Span `StartsWith`, `EndsWith` | [Counted and C-string patterns without copying](../docs/TEXT_MATCH_SPAN.md) |
| Borrowed text lines | `TextLines` | [Counted LF and CRLF line views](../docs/TEXT_LINES.md) |
| Joining text | `Span<immutable(String)>.Join` | [Borrowed owners, counted separators, and one output allocation](../docs/STRING_JOIN.md) |
| Owning Unicode conversion | `UnicodeText` | [Strict counted UTF-16 and UTF-8 conversion](../docs/UNICODE_TEXT.md) |
| Contiguous storage | `Vector<T>`, `Span<T>`, typed span `CopyTo`/`Fill` | [Ownership and views](core/README.md#vectors), [copying and filling initialized elements](core/Collections/OWNING_COLLECTIONS.md#copying-borrowed-spans) |
| Collections | `Queue<T>`, `Stack<T>`, `PriorityQueue<T>`, `BitSet` | [Owning collections](core/Collections/OWNING_COLLECTIONS.md) |
| Double-ended storage | `Deque<T>` | [Owning ring buffer](../docs/DEQUE.md) |
| Borrowed windows | `Span<T>.Windows` | [Overlapping full-width views and lazy queries](../docs/SPAN_WINDOWS.md) |
| Borrowed chunks | `Span<T>.Chunks` | [Nonoverlapping views, including a shorter final chunk](../docs/SPAN_CHUNKS.md) |
| Stable node storage | `LinkedList<T>` | [Owning linked lists and node handles](../docs/LINKED_LIST.md) |
| Single-object ownership | `Unique<T>` | [Move-only ownership of one manual allocation](../docs/UNIQUE.md) |
| Hash collections | `HashMap<K,V>`, `HashSet<T>` | [Borrowed lookup and ownership](../docs/HASH_COLLECTIONS.md) |
| Ordered maps | `SortedMap<K,V>` | [Sorted contiguous entries and explicit comparison](../docs/SORTED_MAP.md) |
| Ordered sets | `SortedSet<T>` | [Sorted unique owning values with explicit comparison](../docs/SORTED_SET.md) |
| Contracts | `IHashable`, `IEquatable<T>`, `IComparable<T>` | [Primitive and String implementations](../docs/HASHING.md) |
| Algorithms | `Sort`, `StableSort`, `Reverse`, `Rotate`, `Shuffle`, `Partition`, bounds/search helpers, `Comparer<T>` | [Span algorithms](core/Collections/ALGORITHMS.md) |
| Queries | `IEnumerable<T,E>`, `Where`, `Select`, `Skip`, `Take`, `SkipWhile`, `TakeWhile`, aggregates and `TryFirst`/`TryLast`/`TrySingle` | [Lazy queries](core/Collections/README.md) |
| Integer sequences | `IntRange`, `FilteredIntRange` | [Allocation-free ranges and specialized aggregates](core/README.md#integer-sequences) |
| Numeric text | `TryParseInt`, `ParseInt`, matching methods for other integer widths | [Strict decimal parsing](../docs/INTEGER_PARSING.md) |
| Arbitrary-precision integers | `BigInteger` | [Owned values, exact arithmetic, and decimal text](../docs/BIG_INTEGER.md) |
| Hexadecimal numbers | `TryParseHexUInt`, `ParseHexUInt`, matching methods for other unsigned widths | [Explicit hexadecimal parsing](../docs/HEX_INTEGER_PARSING.md) |
| Boolean text | `TryParseBool`, `ParseBool` | [Strict ASCII Boolean parsing](../docs/BOOLEAN_PARSING.md) |
| Identifiers | `Uuid` | [Canonical UUID parsing, formatting, comparison, and hashing](../docs/UUID.md) |
| Calendar dates | `Date` | [Gregorian values, calendar arithmetic, strict ISO parsing, and formatting](../docs/DATE.md) |
| Caller-buffer numbers | `IntegerFormatting::TryFormat`, `TryFormatHex` | [Decimal and hexadecimal formatting without a writer or allocation](../docs/INTEGER_FORMATTING.md) |
| Unicode | `Utf8`, incremental `Utf8Decoder`, borrowed `Utf8Scalars` | [UTF-8 buffers](../docs/UTF8.md), [chunked decoding](../docs/UTF8_DECODER.md), [scalar iteration](../docs/UTF8_SCALARS.md) |
| UTF-16 and conversion | `Utf16`, incremental `Utf16Decoder`, borrowed `Utf16Scalars`, `UnicodeTranscoding` | [Code units and chunked decoding](../docs/UTF16.md), [scalar iteration](../docs/UTF16_SCALARS.md), [strict caller-buffer transcoding](../docs/UNICODE_TRANSCODING.md) |
| ASCII bytes | `Ascii` classification, comparison, and case conversion | [Byte utilities](../docs/ASCII.md) |
| Binary text | `Base64`, unpadded `Base64Url`, `Hex`; owning `Base64Text`, `Base64UrlText`, `HexText` helpers | [Caller-buffer and owning codecs](../docs/BINARY_CODECS.md) |
| JSON strings | `JsonString`, `JsonStringText`, writer `WriteJsonString` | [Strict UTF-8 buffer codecs and owning String conveniences](../docs/JSON_STRING.md) |
| JSON documents | `JsonReader`, `JsonTokenKind`, `JsonParseException` | [Forward-only tokens, borrowed raw lexemes, and decoded text comparison](../docs/JSON_READER.md) |
| JSON text | `JsonText::Minify` | [Validate first and remove whitespace while preserving raw tokens](../docs/JSON_TEXT.md) |
| JSON output | `JsonWriter`, `JsonWriteException` | [Validated output through a borrowed text writer](../docs/JSON_WRITER.md) |
| CSV input | `CsvReader`, `CsvParseException` | [Borrowed counted fields and caller-buffer decoding](../docs/CSV_READER.md) |
| CSV output | `CsvWriter` | [Streaming counted fields and CRLF records](../docs/CSV_WRITER.md) |
| URI components | `PercentEncoding`, optional String conveniences | [Percent encoding](../docs/BINARY_CODECS.md#component-percent-encoding) |
| Paths | `Paths`, `PathStyle` | [Lexical Unix/Windows paths](core/Paths/README.md) |
| Numbers | `Math`, `PseudoRandom` | [Scalar math](../docs/MATH.md), [seeded randomness](../docs/PSEUDORANDOM.md) |
| Bits | `BitOperations` | [Counts, rotations, power-of-two rounding, and byte reversal](../docs/BIT_OPERATIONS.md) |
| Floating representations | `BitConverter` | [Exact floating-point bit conversions](../docs/BIT_CONVERTER.md) |
| Binary scalars | `BinaryPrimitives` | [Checked byte-order integer and IEEE floating-point reads and writes](../docs/BINARY_PRIMITIVES.md) |
| Variable-length integers | `VarInt` | [Canonical unsigned base-128 encoding and signed ZigZag transforms](../docs/VARINT.md) |
| Byte searches | `ByteSearch` | [Counted binary substring search](../docs/BYTE_SEARCH.md) |
| Binary buffers | `BinaryBufferReader`, `BinaryBufferWriter` | [Borrowed cursors and bounded payloads](../docs/BINARY_CURSORS.md) |
| Streaming checksums | `CRC32`, `Adler32` | [Incremental byte checksums](../docs/CHECKSUMS.md) |
| Temporary storage | `Arena` | [Independent raw-storage arenas](../docs/ARENA.md) |
| Raw memory | `Memory::Copy`, `Move`, `Fill`, `Compare`, `Equal` | [Byte operations and linking](../docs/MEMORY.md) |

Containers own stored values. Raw pointers stored as elements borrow their
pointees; they do not cause automatic deletion. Views and enumerators borrow
their sources. Do not retain them across operations that invalidate storage.

## Libc and host runtime

| Area | APIs |
| --- | --- |
| Console | `Console::Write`, `WriteLine`, `Read`, `ReadByte`, `PeekByte`, `ReadExactly`, `ReadLine`, `TryReadLine`, `Out`, `Error`, flush operations |
| Files | `File`, `FileText`, `FileBytes`, `FileLines`, `FileLineSequence`, `FileBinary` |
| Allocation | Default replaceable `Memory::Allocate` and `Memory::Free` |
| Managed storage | Replaceable collector selection and `TracingCollector` |
| Exceptions | Default unhandled exception termination |
| Floating text | Floating-point formatting overloads |
| Environment | Borrowed `Environment::GetVariable` and owning `EnvironmentText` conveniences |

See [host APIs](libc/README.md). File and console operations accept caller-owned
character buffers; `String` methods are conveniences rather than a requirement.
Explicit file `Close` can report errors; destruction closes without throwing.

## Runnable examples

- [Word count](../examples/word-count/README.md): streamed input and owning hash keys.
- [Task list](../examples/tasks/README.md): owning titles, reference-indexer updates, and reusable console input.
- [File checksums](../examples/checksum/README.md): streaming CRC32 and Adler32 over binary files.
- [Hex dump](../examples/hexdump/README.md): byte offsets, hex columns, and printable ASCII with stack-backed formatting.
- [Unicode conversion](../examples/transcode/README.md): strict UTF-8/UTF-16LE conversion with validation before output-file creation.
- [Quote text lines](../examples/quote-lines/README.md): lazy file lines written directly as newline-delimited JSON strings.
- [Decode JSON string lines](../examples/unquote-lines/README.md): strict validation and in-place decoding with a reusable buffer.
- [Check JSON documents](../examples/json-check/README.md): forward-only validation and nesting statistics without building a DOM.
- [Minify JSON documents](../examples/json-minify/README.md): validate first, then write compact JSON while preserving duplicate names, property order, and number spelling.
- [Number statistics](../examples/number-stats/README.md): bounded stdin lines, borrowed ASCII trimming, strict integer parsing, and an exact 128-bit running sum.
- [Sort lines](../examples/sort-lines/README.md): move owning strings into a vector, sort by unsigned byte order, and preserve counted output.
- [Settings to JSON](../examples/settings-json/README.md): parse borrowed key/value slices, validate UTF-8, and retain accepted strings in an owning map.
- [CSV column sums](../examples/csv-sum/README.md): validate quoted fields and sum one integer column without allocating field strings.
- [Pathfinding](../examples/pathfinding/README.md): weighted shortest paths using a priority queue.
- [Search](../examples/search/README.md): chunked file processing.
- [Queries](../examples/queries/main.gf): lazy, allocation-free collection pipelines.

This remains an experimental library. There are no thread-safety guarantees for
mutable containers, no locale-sensitive text operations, and no cryptographic
guarantees for ordinary hashes or seeded random values.

# Standard library API index

Browse APIs by subject below. For a first program, see [getting started](STDLIB_GETTING_STARTED.md).

## Core

Core algorithms and data structures contain no direct libc calls. Owning types
allocate through the replaceable `Memory::Allocate`/`Free` API. Its default
implementation lives in `libc`.

### Text and byte streams

| Area | APIs | Details |
| --- | --- | --- |
| Text | `String`, `ITextWriter`, `IFormattable`, `IStringConvertable` | [Strings](STRINGS.md), [formatting](../Standard/core/README.md#strings-and-formatting) |
| Owning text conversion | Primitive and borrowed `IFormattable` `ToString()` extensions | [Owned results and receiver rules](STRING_CONVERSION.md) |
| C strings | `CString` | [Borrowed null-terminated byte utilities](CSTRING.md) |
| Caller-buffer text | `BufferWriter` | [Allocation-free formatting](BUFFER_WRITER.md) |
| Byte measurement | `CountingWriter` | [Count writer output without storing it](COUNTING_WRITER.md) |
| Shared output | `TeeWriter` | [Forward one stream to two borrowed writers](TEXT_WRITERS.md#send-one-stream-to-two-writers) |
| Buffered output | `BufferedWriter` | [Coalesce writes with caller-owned scratch](TEXT_WRITERS.md#buffer-small-writes-before-forwarding) |
| Bounded output | `LimitedWriter` | [Cap bytes forwarded to a borrowed writer](LIMITED_WRITER.md) |
| Prefixed lines | `LinePrefixWriter` | [Prefix streamed lines without buffering them](LINE_PREFIX_WRITER.md) |
| Writer helpers | `ITextWriter*` `WriteLine` overloads and `WriteRepeated` | [Borrowed writer conveniences](TEXT_WRITERS.md) |
| Byte readers | `IByteReader`, borrowed `ReadByte`, `ReadExactly`, `SkipExactly` and `CopyTo` extensions | [Borrowed byte readers](BYTE_READERS.md) |
| Binary scalar IO | Endian reads on `IByteReader*` and writes on `ITextWriter*` | [Borrowed binary IO](BINARY_IO.md) |
| Buffered input | `BufferedReader` with owned or caller-provided scratch, `PeekByte` | [Read-ahead, cursor lifetime and ownership](BUFFERED_READER.md) |
| Bounded input | `LimitedReader` | [Read one payload without consuming its tail](../Standard/core/IO/LIMITED_READER.md) |
| Byte lines | Borrowed writer, reusable `String`, and owning `ReadLine` helpers | [Counted lines from any byte source](BYTE_READERS.md#read-counted-lines-from-any-byte-source) |
| Byte iteration | Borrowed `ByteSequence`, `EnumerateBytes`, and optional queries | [Deferred single-pass byte iteration](BYTE_READERS.md#enumerate-bytes-without-owning-the-source) |
| Line iteration | Borrowed `LineSequence` with reusable current line and optional queries | [Deferred counted line iteration](BYTE_READERS.md#enumerate-lines-without-owning-the-source) |
| Stream varints | `ReadVarUInt32/64`, `ReadZigZagInt32/64`, and matching writer extensions | [Borrowed stream varints](STREAM_VARINT.md) |
| Borrowed text parts | `TextParts`, String/Span `Split`, Span `TrySplitOnce`/`TrySplitLastOnce`/`Trim` | [Allocation-free splitting and trimming](TEXT_PARTS.md), [key/value views](TEXT_SPLIT_ONCE.md), [multi-byte delimiters](TEXT_SPLIT_PATTERN.md) |
| Borrowed byte-set splitting | `TextPartsAny`, String/Span `SplitAny` | [Copied delimiter sets and borrowed parts](TEXT_PARTS_ANY.md) |
| Borrowed text matching | Span `StartsWith`, `EndsWith` | [Counted and C-string patterns without copying](TEXT_MATCH_SPAN.md) |
| Borrowed text lines | `TextLines` | [Counted LF and CRLF line views](TEXT_LINES.md) |
| Joining text | `Span<immutable(String)>.Join` | [Borrowed owners, counted separators, and one output allocation](STRING_JOIN.md) |
| Owning Unicode conversion | `UnicodeText` | [Strict counted UTF-16 and UTF-8 conversion](UNICODE_TEXT.md) |

### Collections and queries

| Area | APIs | Details |
| --- | --- | --- |
| Contiguous storage | `Vector<T>`, `Span<T>`, typed span `CopyTo`/`Fill` | [Ownership and views](../Standard/core/README.md#vectors), [copying and filling initialized elements](../Standard/core/Collections/OWNING_COLLECTIONS.md#copying-borrowed-spans) |
| Collections | `Queue<T>`, `Stack<T>`, `PriorityQueue<T>`, `BitSet` | [Owning collections](../Standard/core/Collections/OWNING_COLLECTIONS.md) |
| Double-ended storage | `Deque<T>` | [Owning ring buffer](DEQUE.md) |
| Borrowed windows | `Span<T>.Windows` | [Overlapping full-width views and lazy queries](SPAN_WINDOWS.md) |
| Borrowed chunks | `Span<T>.Chunks` | [Nonoverlapping views, including a shorter final chunk](SPAN_CHUNKS.md) |
| Stable node storage | `LinkedList<T>` | [Owning linked lists and node handles](LINKED_LIST.md) |
| Single-object ownership | `Unique<T>` | [Move-only ownership of one manual allocation](UNIQUE.md) |
| Hash collections | `HashMap<K,V>`, `HashSet<T>`, query `ToHashSet` | [Borrowed lookup and ownership](HASH_COLLECTIONS.md), [query materialization](QUERY_HASH_SET.md) |
| Ordered maps | `SortedMap<K,V>` | [Sorted contiguous entries and explicit comparison](SORTED_MAP.md) |
| Ordered sets | `SortedSet<T>` | [Sorted unique owning values with explicit comparison](SORTED_SET.md) |
| Connected components | `DisjointSet` | [Fixed-index groups and allocation-free unions and queries](DISJOINT_SET.md) |
| Contracts | `IHashable`, `IEquatable<T>`, `IComparable<T>` | [Primitive and String implementations](HASHING.md) |
| Algorithms | `Sort`, `StableSort`, `Reverse`, `Rotate`, `Shuffle`, `Partition`, max-heap operations, bounds/search helpers, `Comparer<T>` | [Span algorithms](../Standard/core/Collections/ALGORITHMS.md) |
| Rank selection | `NthElement<T>(span, nth, compare)` | [In-place selection and partition guarantees](NTH_ELEMENT.md) |
| Queries | `IEnumerable<T,E>`, `Where`, `Select`, `Skip`, `Take`, `SkipWhile`, `TakeWhile`, aggregates and `TryFirst`/`TryLast`/`TrySingle` | [Lazy queries](../Standard/core/Queries/README.md) |
| Lazy uniqueness | `Distinct` | [First-occurrence order with scoped hash storage](QUERY_DISTINCT.md) |
| Lazy uniqueness by key | `DistinctBy(selector)` | [First whole values for selected keys](QUERY_DISTINCT_BY.md) |
| Query ordering | `Order()` / `Order(compare)` | [Stable buffered ordering and borrowed cursor values](QUERY_ORDER.md) |
| Query concatenation | `Concat(other)` | [Concatenated iteration and ownership](QUERY_CONCAT.md) |
| Buffered query tail | `TakeLast(count)` | [Finite tails and bounded storage](QUERY_TAKE_LAST.md) |
| Streaming suffix omission | `SkipLast(count)` | [Bounded lookahead and source consumption](QUERY_SKIP_LAST.md) |
| Query endpoints | `Append`, `Prepend` | [Captured values and repeatable enumeration](QUERY_ENDPOINT.md) |
| Query key counts | `CountBy` | [Owning hash maps of selected-key counts](QUERY_COUNT_BY.md) |
| Query map results | `ToHashMap` | [Selected keys and values with duplicate detection](QUERY_TO_HASH_MAP.md) |
| Query groups | `ToLookup` | [Owning maps of vectors in encounter order](QUERY_LOOKUP.md) |
| Empty-query fallback | `DefaultIfEmpty(value)` | [Explicit fallback and capture ownership](QUERY_DEFAULT_IF_EMPTY.md) |
| Integer query totals | `Sum()` | [Checked long totals through mutable and protected views](QUERY_SUM.md) |
| Integer sequences | `IntRange`, `FilteredIntRange` | [Allocation-free ranges and specialized aggregates](../Standard/core/README.md#integer-sequences) |
| Repeated values | `Repeated<T>` | [Captured values and independent query cursors](REPEATED_VALUES.md) |

### Numbers and parsing

| Area | APIs | Details |
| --- | --- | --- |
| Numeric text | `TryParseInt`, `ParseInt`, matching methods for other integer widths | [Strict decimal parsing](INTEGER_PARSING.md) |
| Arbitrary-precision integers | `BigInteger` | [Owned values, exact arithmetic, strict decimal parsing, and formatting](BIG_INTEGER.md) |
| Exact fractions | `Rational` | [Normalized BigInteger fractions with owning value semantics](RATIONAL.md) |
| Atomic integers | `Interlocked` | [Sequentially consistent int and long operations](INTERLOCKED.md) |
| Hexadecimal numbers | `TryParseHexUInt`, `ParseHexUInt`, matching methods for other unsigned widths | [Explicit hexadecimal parsing](HEX_INTEGER_PARSING.md) |
| Boolean text | `TryParseBool`, `ParseBool` | [Strict ASCII Boolean parsing](BOOLEAN_PARSING.md) |

### Identifiers, addresses, and time

| Area | APIs | Details |
| --- | --- | --- |
| Identifiers | `Uuid`, `UuidGenerator` | [Canonical UUID values and explicit-source version 4 generation](UUID.md) |
| Numeric versions | `Version` | [Dotted version parsing, ordering, and caller-buffer formatting](VERSION.md) |
| IP addresses | `IpAddress` | [IPv4 and IPv6 parsing, canonical formatting, network-byte conversion, comparison, and hashing](IP_ADDRESS.md) |
| IP endpoints | `IpEndpoint` | [Numeric address/port parsing, canonical formatting, and collection keys](IP_ENDPOINT.md) |
| IP networks | `IpNetwork`, `NetworkAddress`, `LastAddress` | [CIDR prefixes, numeric endpoints, and family-specific containment](IP_NETWORK.md) |
| Calendar dates | `Date` | [Gregorian values, calendar arithmetic, strict ISO parsing, and formatting](DATE.md) |
| Durations | `TimeSpan` | [Signed tick values, checked arithmetic, invariant parsing and formatting](TIME_SPAN.md) |
| Times of day | `TimeOfDay` | [Clock-face values, midnight-wrapping arithmetic, and strict invariant text](TIME_OF_DAY.md) |
| Calendar date and time | `DateTime` | [Combined civil values without a time zone](DATE_TIME.md) |
| Explicit-offset timestamps | `DateTimeOffset` | [UTC identity, fixed offsets, parsing and formatting](DATE_TIME_OFFSET.md) |
| Interval timing | `Stopwatch<C>`, `IMonotonicClock` | [Value timers and explicit clock providers](STOPWATCH.md) |

### Text and Unicode

| Area | APIs | Details |
| --- | --- | --- |
| Caller-buffer numbers | `IntegerFormatting::TryFormat`, `TryFormatHex` | [Decimal and hexadecimal formatting without a writer or allocation](INTEGER_FORMATTING.md) |
| Unicode | `Utf8`, incremental `Utf8Decoder`, borrowed `Utf8Scalars` | [UTF-8 buffers](UTF8.md), [chunked decoding](UTF8_DECODER.md), [scalar iteration](UTF8_SCALARS.md) |
| Unicode scalar values | `Rune` | [Validated scalars and UTF-8 formatting](RUNE.md) |
| UTF-16 and conversion | `Utf16`, incremental `Utf16Decoder`, borrowed `Utf16Scalars`, `UnicodeTranscoding` | [Code units and chunked decoding](UTF16.md), [scalar iteration](UTF16_SCALARS.md), [strict caller-buffer transcoding](UNICODE_TRANSCODING.md) |
| ASCII bytes | `Ascii` classification, comparison, case conversion, and case-insensitive hashing | [Byte utilities](ASCII.md) |

### Encodings, compression, and formats

| Area | APIs | Details |
| --- | --- | --- |
| Binary text | `Base64`, unpadded `Base64Url`, `Hex`. Owning `Base64Text`, `Base64UrlText`, `HexText` helpers and writer `WriteHex` | [Caller-buffer, owning, and writer codecs](BINARY_CODECS.md) |
| Raw compression | `Deflate::Decompress` | [Stored, fixed-Huffman, and dynamic-Huffman decoding into caller-owned buffers](../Standard/core/Compression/DEFLATE.md) |
| Gzip members | `Gzip::DecompressMember` | [Optional headers, integrity checks, and exact first-member consumption](../Standard/core/Compression/GZIP.md) |
| Zlib streams | `Zlib::Decompress` | [Header checks, declared history windows, and Adler32 validation](../Standard/core/Compression/ZLIB.md) |
| Padded Base32 | `Base32`, `Base32Text` | [Strict canonical caller-buffer codecs and owning results](BASE32.md) |
| Streaming Base32 | `Base32Encoder`, `Base32Decoder` | [Encoding](BASE32_ENCODER.md), [strict decoding](BASE32_DECODER.md) through borrowed writers |
| Streaming Base64 | `Base64Encoder`, `Base64Decoder` | [Encoding](BASE64_ENCODER.md), [strict decoding](BASE64_DECODER.md) through borrowed writers |
| Streaming hexadecimal | `HexDecoder` | [Strict decoding](HEX_DECODER.md) through a borrowed writer |
| JSON strings | `JsonString`, `JsonStringText`, writer `WriteJsonString` | [Strict UTF-8 buffer codecs and owning String conveniences](JSON_STRING.md) |
| JSON documents | `JsonReader`, `JsonTokenKind`, `JsonParseException` | [Forward-only tokens, borrowed raw lexemes, and decoded text comparison](JSON_READER.md) |
| JSON text | `JsonText::Minify`, `JsonText::PrettyPrint` | [Validate first and format whitespace while preserving raw tokens](JSON_TEXT.md) |
| JSON lookup | `JsonPointer::TryResolve` | [Borrowed raw values selected by plain JSON Pointer](JSON_POINTER.md) |
| JSON output | `JsonWriter`, `JsonWriteException` | [Validated output through a borrowed text writer](JSON_WRITER.md) |
| CSV input | `CsvReader`, `CsvReaderText`, `CsvParseException` | [Borrowed fields and reusable String decoding](CSV_READER.md) |
| CSV output | `CsvWriter` | [Streaming counted fields and CRLF records](CSV_WRITER.md) |
| URI components | `PercentEncoding`, optional String conveniences | [Percent encoding](BINARY_CODECS.md#component-percent-encoding) |

### Paths, bits, and binary data

| Area | APIs | Details |
| --- | --- | --- |
| Paths | `Paths`, `PathStyle` | [Lexical Unix/Windows paths](../Standard/core/IO/PATHS.md) |
| Numbers | `Math`, `PseudoRandom` | [Scalar math](MATH.md), [seeded randomness](PSEUDORANDOM.md) |
| Bits | `BitOperations` | [Counts, rotations, power-of-two rounding, and byte reversal](BIT_OPERATIONS.md) |
| Floating representations | `BitConverter` | [Exact floating-point bit conversions](BIT_CONVERTER.md) |
| Binary scalars | `BinaryPrimitives` | [Checked byte-order integer and IEEE floating-point reads and writes](BINARY_PRIMITIVES.md) |
| Delimited binary fields | `BinaryBufferReader::TryReadTo` | [Borrow fields through a required delimiter without unbounded reads](../Standard/core/Binary/BINARY_READ_TO.md) |
| Bit fields | `BitPrimitives`, `BitOrder` | [Borrowed-buffer fields with explicit bit order](BIT_FIELDS.md) |
| Variable-length integers | `VarInt` | [Canonical unsigned base-128 encoding and signed ZigZag transforms](VARINT.md) |
| Byte searches | `ByteSearch` | [Counted substring and byte-set searches](BYTE_SEARCH.md) |
| Byte wildcards | `MatchesWildcard` | [Whole-input counted wildcard matching with bounded and C-string adapters](WILDCARD_MATCHING.md) |
| Byte edit distance | `EditDistance::TryCompute` | [Levenshtein distance with caller workspace](EDIT_DISTANCE.md) |
| Binary buffers | `BinaryBufferReader`, `BinaryBufferWriter` | [Borrowed cursors and bounded payloads](BINARY_CURSORS.md) |

### Hashing and raw storage

| Area | APIs | Details |
| --- | --- | --- |
| Streaming checksums | `CRC32`, `CRC32C`, `Adler32` | [Incremental byte checksums](CHECKSUMS.md) |
| SHA-256 digests | `SHA256` | [Incremental hashing and fixed-buffer results](SHA256.md) |
| SHA-512 digests | `SHA512` | [Incremental hashing and independent snapshots](SHA512.md) |
| Keyed digests | `HMACSHA256`, `HMACSHA512` | [HMAC-SHA256](HMAC_SHA256.md), [HMAC-SHA512](HMAC_SHA512.md) with counted keys |
| Key extraction/expansion | `HKDFSHA256` | [HKDF-SHA256 with caller-buffer expansion](HKDF_SHA256.md) |
| Temporary storage | `Arena` | [Independent raw-storage arenas](ARENA.md) |
| Raw memory | `Memory::Copy`, `Move`, `Fill`, `Compare`, `Equal` | [Byte operations and linking](MEMORY.md) |
| Integer alignment | `Memory::AlignUp`, `AlignDown`, `IsAligned` | [Checked offsets and alignment tests](MEMORY.md#integer-alignment) |

Containers own stored values. Raw pointers stored as elements borrow their
pointees. They do not cause automatic deletion. Container views and enumerators
borrow container storage. Do not retain them across operations that invalidate
that storage. Query sequences and iterators can also own captured values or
materialized buffers. See the [query ownership rules](../Standard/core/Queries/README.md).

## Libc and host runtime

| Area | APIs |
| --- | --- |
| Console | `Console::Write`, `WriteLine`, `Read`, `ReadByte`, `PeekByte`, `ReadExactly`, `ReadLine`, `TryReadLine` into buffers, String, or a borrowed writer. `Out`, `Error`, flush operations |
| Files | `File`, `FileText`, `FileBytes`, `FileLines`, `FileLineSequence`, `FileBinary`, [`FileHash`](FILE_HASH.md), [`FileVarInt`](FILE_VARINT.md) |
| Allocation | Default replaceable `Memory::Allocate` and `Memory::Free` |
| Managed storage | Replaceable collector selection and `TracingCollector` |
| Exceptions | Default unhandled exception termination |
| Floating text | Floating-point formatting and [hosted C-string parsing](FLOATING_PARSING.md) |
| Environment | Borrowed `Environment::GetVariable` and owning `EnvironmentText` conveniences |

See [host APIs](../Standard/libc/README.md). File and console operations accept caller-owned
character buffers. `String` methods are conveniences rather than a requirement.
Explicit file `Close` can report errors. Destruction closes without throwing.

[Optional platform libraries](../libraries/README.md) provide monotonic clocks
for `Stopwatch<C>` and `SystemClock::GetUtcNow()` for UTC calendar timestamps.
Applications reference the Windows or Linux source project explicitly.
The optional [Windows TCP library](../libraries/platform/windows-network/README.md)
provides an owning blocking client for numeric IPv4 and IPv6 endpoints.

## Runnable examples

- [Word count](../examples/word-count/README.md): streamed input and owning hash keys
- [IP address counts](../examples/ip-count/README.md): normalize equivalent IPv4/IPv6 spellings, optionally filter by CIDR, and count numeric keys from stdin
- [Task list](../examples/tasks/README.md): owning titles, reference-indexer updates, and reusable console input
- [File checksums](../examples/checksum/README.md): streaming CRC32 and Adler32, or SHA-256 with `--sha256`, over binary files
- [Closest byte matches](../examples/closest/README.md): select minimum edit-distance candidates with a reused scratch row and stable ties
- [Hex dump](../examples/hexdump/README.md): byte offsets, hex columns, and printable ASCII with stack-backed formatting
- [Unicode conversion](../examples/transcode/README.md): strict UTF-8/UTF-16LE conversion with validation before output-file creation
- [Quote text lines](../examples/quote-lines/README.md): lazy file lines written directly as newline-delimited JSON strings
- [Decode JSON string lines](../examples/unquote-lines/README.md): strict validation and in-place decoding with a reusable buffer
- [Check JSON documents](../examples/json-check/README.md): forward-only validation and nesting statistics without building a DOM
- [Minify JSON documents](../examples/json-minify/README.md): validate first, then write compact JSON while preserving duplicate names, property order, and number spelling
- [Pretty-print JSON documents](../examples/json-pretty/README.md): validate first, then indent original token bytes with a selectable width
- [Number statistics](../examples/number-stats/README.md): bounded stdin lines, borrowed ASCII trimming, strict integer parsing, and an exact 128-bit running sum
- [Integer arithmetic](../examples/big-calc/README.md): exact BigInteger calculations, greatest common divisors, and least common multiples
- [Fraction arithmetic](../examples/ratio-calc/README.md): normalize and calculate with exact Rational values from integer or fraction operands
- [Time conversion](../examples/time-convert/README.md): normalize explicit-offset timestamps to UTC and convert Unix seconds or milliseconds
- [Sort lines](../examples/sort-lines/README.md): move owning strings into a vector, sort by unsigned byte order, and preserve counted output
- [Sample lines](../examples/sample-lines/README.md): bounded reservoir sampling with deterministic seeds and randomized output order
- [Settings to JSON](../examples/settings-json/README.md): parse borrowed key/value slices, validate UTF-8, and retain accepted strings in an owning map
- [Extract a CSV column](../examples/csv-column/README.md): validate first, then preserve counted field bytes in a binary one-column CSV file
- [CSV column sums](../examples/csv-sum/README.md): validate quoted fields and sum one integer column without allocating field strings
- [CSV to JSON](../examples/csv-to-json/README.md): validate CSV and UTF-8 first, then write row arrays with one reusable field buffer
- [JSON get](../examples/json-get/README.md): select a raw value with a plain pointer or a counted UTF-8 pointer file
- [Pathfinding](../examples/pathfinding/README.md): weighted shortest paths using a priority queue
- [Connected components](../examples/components/README.md): stream undirected edges and report graph partitions
- [Timed sorting](../examples/sort-bench/README.md): select a platform clock, time two sorting algorithms, and verify their results
- [Timestamped lines](../examples/stamp-lines/README.md): stream input through a formatted line prefix and an explicit UTC clock
- [Search](../examples/search/README.md): chunked file processing
- [Queries](../examples/queries/main.gf): lazy, allocation-free collection pipelines

Coordinate concurrent access to mutable containers. Text helpers use their
documented byte and Unicode rules. Use system random providers and suitable
cryptographic APIs for security-sensitive data.

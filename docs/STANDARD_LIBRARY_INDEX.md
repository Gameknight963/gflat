# Standard library API index

Browse APIs by subject below. For a first program, see [getting started](STDLIB_GETTING_STARTED.md).

## Core

Core types and algorithms are grouped below. For ownership examples, see
[the core guide](CORE_LIBRARY.md).

### Text and byte streams

| APIs | Guide |
| --- | --- |
| `String`, `ITextWriter`, `IFormattable`, `IStringConvertable` | [Strings](STRINGS.md), [Strings and formatting](../Standard/core/README.md#strings-and-formatting) |
| Primitive and borrowed `IFormattable` `ToString()` extensions | [Convert a value to an owning String](STRING_CONVERSION.md) |
| `CString` | [Borrowed C strings](CSTRING.md) |
| `BufferWriter` | [Caller-owned text output](BUFFER_WRITER.md) |
| `CountingWriter` | [Measure writer output](COUNTING_WRITER.md) |
| `TeeWriter` | [Send one stream to two writers](TEXT_WRITERS.md#send-one-stream-to-two-writers) |
| `BufferedWriter` | [Buffer small writes before forwarding](TEXT_WRITERS.md#buffer-small-writes-before-forwarding) |
| `LimitedWriter` | [Bound output through a borrowed writer](LIMITED_WRITER.md) |
| `LinePrefixWriter` | [Prefix streamed lines](LINE_PREFIX_WRITER.md) |
| `ITextWriter*` `WriteLine`, `WriteRepeated`, and `WriteJoin` overloads | [Text writer helpers](TEXT_WRITERS.md) |
| `String.ReplaceLineEndings()` | [Normalize line endings](LINE_ENDINGS.md) |
| String arguments for search and replacement | [Using owning strings as search arguments](STRING_ARGUMENTS.md) |
| Ranged String `AsSpan`, `AsReadOnly`, `AsImmutable` | [Borrowing a range of string bytes](STRING_RANGE_VIEWS.md) |
| `IByteReader`, borrowed `ReadByte`, `ReadExactly`, `ReadAtLeast`, `SkipExactly` and `CopyTo` extensions | [Borrowed byte readers](BYTE_READERS.md) |
| `ReadToEnd()` and `ReadToEnd(ref text)` | [Read the remaining input](READ_TO_END.md) |
| Endian reads on `IByteReader*` and writes on `ITextWriter*` | [Binary scalar IO](BINARY_IO.md) |
| `BufferedReader` with owned or caller-provided scratch, `PeekByte` | [Explicit buffered input](BUFFERED_READER.md) |
| `LimitedReader` | [Limit a consumer to one payload](../Standard/core/IO/LIMITED_READER.md) |
| Borrowed writer, reusable `String`, and owning `ReadLine` helpers | [Read counted lines from any byte source](BYTE_READERS.md#read-counted-lines-from-any-byte-source) |
| Borrowed `ByteSequence`, `EnumerateBytes`, and optional queries | [Enumerate bytes without owning the source](BYTE_READERS.md#enumerate-bytes-without-owning-the-source) |
| Borrowed `LineSequence` with reusable current line and optional queries | [Enumerate lines without owning the source](BYTE_READERS.md#enumerate-lines-without-owning-the-source) |
| `ReadVarUInt32/64`, `ReadZigZagInt32/64`, and matching writer extensions | [Borrowed stream varints](STREAM_VARINT.md) |
| `TextParts`, String/Span `Split`, Span `TrySplitOnce`/`TrySplitLastOnce`/`Trim` | [Borrowed text splitting](TEXT_PARTS.md), [Split a borrowed key and value](TEXT_SPLIT_ONCE.md), [Split borrowed text once on a byte-sequence delimiter](TEXT_SPLIT_PATTERN.md) |
| `TextPartsAny`, String/Span `SplitAny` | [Splitting on a byte set](TEXT_PARTS_ANY.md) |
| `TextPartsPattern`, String/Span pattern `Split` | [Splitting on an exact byte pattern](TEXT_PATTERN_SPLIT.md) |
| Span `StartsWith`, `EndsWith` | [Borrowed text prefix and suffix checks](TEXT_MATCH_SPAN.md) |
| `TextLines` | [Borrowed text lines](TEXT_LINES.md) |
| `Span<immutable(String)>.Join` | [Joining borrowed Strings](STRING_JOIN.md) |
| `UnicodeText` | [Owning Unicode conversions](UNICODE_TEXT.md) |

### Collections and queries

| APIs | Guide |
| --- | --- |
| `Vector<T>`, `Span<T>`, typed span `CopyTo`/`Fill` | [Vectors](../Standard/core/README.md#vectors), [Copying borrowed spans](../Standard/core/Collections/OWNING_COLLECTIONS.md#copying-borrowed-spans) |
| Span value and pattern `Count` | [Counting values and patterns in spans](SPAN_COUNT.md) |
| Span `IndexOfAny`, `IndexOfAnyExcept`, and related searches | [Searching a span for a set of values](SPAN_SET_SEARCH.md) |
| Span inclusive-range searches | [Searching spans by an inclusive range](SPAN_RANGE_SEARCH.md) |
| `Queue<T>`, `Stack<T>`, `PriorityQueue<T>`, `BitSet` | [Owning collections](../Standard/core/Collections/OWNING_COLLECTIONS.md) |
| `BitSet.Not` | [Inverting every bit](BIT_SET_COMPLEMENT.md) |
| `Deque<T>` | [Deque](DEQUE.md) |
| Collection `EnsureCapacity`, `TrimExcess`, `ShrinkToFit` | [Controlling collection capacity](COLLECTION_CAPACITY.md) |
| Vector/deque `Contains`, `IndexOf`, `LastIndexOf`, `RemoveValue` | [Searching vectors and deques](SEQUENCE_COLLECTION_SEARCH.md) |
| `Vector<T>.GetRange` | [Copying a vector range](VECTOR_GET_RANGE.md) |
| `Vector<T>.ConvertAll` | [Converting every vector element](VECTOR_CONVERT_ALL.md) |
| Vector predicate searches | [Searching vectors with a predicate](VECTOR_PREDICATES.md) |
| Queue/stack `Contains` | [Searching a queue or stack](QUEUE_STACK_CONTAINS.md) |
| Queue, stack, and priority-queue `TryPeek` | [Copy the next collection value](COLLECTION_PEEK.md) |
| `PriorityQueue<T>.EnqueueDequeue`, `DequeueEnqueue` | [Replacing a priority queue root](PRIORITY_QUEUE_REPLACEMENT.md) |
| `Span<T>.Windows` | [Sliding windows over borrowed spans](SPAN_WINDOWS.md) |
| `Span<T>.Chunks` | [Borrowed span chunks](SPAN_CHUNKS.md) |
| `LinkedList<T>` | [Linked lists](LINKED_LIST.md) |
| Linked-list `Find`, `FindLast`, `Contains`, `RemoveValue` | [Search linked-list values](LINKED_LIST_SEARCH.md) |
| `Unique<T>` | [Unique ownership](UNIQUE.md) |
| `HashMap<K,V>`, `HashSet<T>`, query `ToHashSet` | [Hash collections](HASH_COLLECTIONS.md), [Materializing query results into a set](QUERY_HASH_SET.md) |
| Hash-table `EnsureCapacity`, `TrimExcess` | [Reserving and trimming hash tables](HASH_CAPACITY.md) |
| `SortedMap<K,V>` | [Sorted map](SORTED_MAP.md) |
| Map `TryGetValue`, `GetValueOrDefault` | [Copying values from maps](MAP_VALUE_LOOKUP.md) |
| Map `ContainsValue` | [Searching map values](MAP_VALUE_SEARCH.md) |
| `SortedSet<T>` | [SortedSet](SORTED_SET.md) |
| Set `TryGetValue`, sorted-set `Min`/`Max` | [Retrieve stored set values](SET_VALUE_LOOKUP.md) |
| Sorted-set equality, subset/superset checks, overlap | [Comparing sorted sets](SORTED_SET_RELATIONS.md) |
| Sorted-set algebra and `RemoveWhere` | [Mutating sorted sets](SORTED_SET_ALGEBRA.md) |
| `DisjointSet` | [Disjoint sets](DISJOINT_SET.md) |
| `IHashable`, `IEquatable<T>`, `IComparable<T>` | [Equality, hashing, and ordering](HASHING.md) |
| `Sort`, `StableSort`, `Reverse`, `Rotate`, `Shuffle`, `Partition`, max-heap operations, bounds/search helpers, `Comparer<T>` | [Value and span algorithms](../Standard/core/Collections/ALGORITHMS.md) |
| `NthElement<T>(span, nth, compare)` | [Selecting an element by rank](NTH_ELEMENT.md) |
| `IEnumerable<T,E>`, `Where`, `Select`, `Skip`, `Take`, `SkipWhile`, `TakeWhile`, aggregates and `TryFirst`/`TryLast`/`TrySingle` | [Enumeration and queries](../Standard/core/Queries/README.md) |
| `Distinct` | [Lazy distinct values](QUERY_DISTINCT.md) |
| Query `Index()`, `Indexed<T>` | [Pair sequence items with their positions](QUERY_INDEX.md) |
| `Union`, `Except`, `Intersect` | [Query set operations](QUERY_SET_OPERATIONS.md) |
| `UnionBy`, `ExceptBy`, `IntersectBy` | [Query set operations by key](QUERY_SET_OPERATIONS_BY.md) |
| `DistinctBy(selector)` | [Lazy distinct values by key](QUERY_DISTINCT_BY.md) |
| `Order()` / `Order(compare)` | [Order a buffered query](QUERY_ORDER.md) |
| `Concat(other)` | [Concatenating queries](QUERY_CONCAT.md) |
| `TakeLast(count)` | [Taking a finite sequence's tail](QUERY_TAKE_LAST.md) |
| `SkipLast(count)` | [Streaming without a trailing suffix](QUERY_SKIP_LAST.md) |
| `Append`, `Prepend` | [Adding a query endpoint](QUERY_ENDPOINT.md) |
| `CountBy` | [Count sequence elements by key](QUERY_COUNT_BY.md) |
| `AggregateBy` | [Aggregate values by key](QUERY_AGGREGATE_BY.md) |
| `ToHashMap` | [Materialize a query as a hash map](QUERY_TO_HASH_MAP.md) |
| `ToLookup` | [Materialize ordered groups](QUERY_LOOKUP.md) |
| `DefaultIfEmpty(value)` | [Default value for an empty query](QUERY_DEFAULT_IF_EMPTY.md) |
| `Sum()` / `Sum(selector)` | [Integer, floating, and projected sums](QUERY_SUM.md) |
| `Average()` / `Average(selector)` | [Sequence averages](QUERY_AVERAGE.md) |
| `IntRange`, `FilteredIntRange` | [Integer sequences](../Standard/core/README.md#integer-sequences) |
| `Repeated<T>` | [Repeated values](REPEATED_VALUES.md) |

### Numbers and parsing

| APIs | Guide |
| --- | --- |
| `TryParseInt`, `ParseInt`, matching methods for other integer widths | [Integer parsing](INTEGER_PARSING.md) |
| `BigInteger` | [BigInteger](BIG_INTEGER.md) |
| `Math` | [Scalar math](MATH.md) |
| `Math::DivRem`, `TryDivRem` | [Integer quotient and remainder](MATH_DIVREM.md) |
| `PseudoRandom` | [Deterministic pseudorandom values](PSEUDORANDOM.md) |
| `PseudoRandom.NextBytes` | [Filling a byte buffer](RANDOM_BYTES.md) |
| `PseudoRandom.Shuffle` | [Shuffling through a random generator](RANDOM_SHUFFLE.md) |
| `PseudoRandom.GetItems` | [Choosing random items](RANDOM_ITEMS.md) |
| `PseudoRandom.GetString`, `GetHexString` | [Generating random text](RANDOM_TEXT.md) |
| `Math` floating-point classification | [Classifying floating-point values](FLOATING_CLASSIFICATION.md) |
| `Math::Clamp`, `MinMagnitude`, `MaxMagnitude` for floating values | [Floating-point bounds and magnitudes](FLOATING_EXTREMA.md) |
| `Math::Round(value, MidpointRounding)` | [Choosing a rounding mode](MIDPOINT_ROUNDING.md) |
| `Math::ScaleB` | [Scaling by powers of two](MATH_SCALE_B.md) |
| `Math::BigMul` | [Full-width integer multiplication](MATH_BIG_MUL.md) |
| `Rational` | [Exact rational values](RATIONAL.md) |
| `Interlocked` | [Atomic integer operations](INTERLOCKED.md) |
| `TryParseHexUInt`, `ParseHexUInt`, matching methods for other unsigned widths | [Hexadecimal integer parsing](HEX_INTEGER_PARSING.md) |
| `TryParseBool`, `ParseBool` | [Boolean parsing](BOOLEAN_PARSING.md) |

### Identifiers, addresses, and time

| APIs | Guide |
| --- | --- |
| `Uuid`, `UuidGenerator` | [UUID values](UUID.md) |
| UUID `ParseExact`, `ToString(format)` | [Explicit UUID text formats](UUID_FORMATS.md) |
| `Version` | [Dotted numeric versions](VERSION.md) |
| Version field-count formatting and revision halves | [Formatting selected version fields](VERSION_FORMATTING.md) |
| `IpAddress` | [Numeric IP addresses](IP_ADDRESS.md) |
| IP family conversion, loopback, and IPv6 prefixes | [Mapping and classifying IP addresses](IP_ADDRESS_MAPPING.md) |
| `IpEndpoint` | [Numeric IP endpoints](IP_ENDPOINT.md) |
| `IpNetwork`, `NetworkAddress`, `LastAddress` | [IP network prefixes](IP_NETWORK.md) |
| `Date` | [Calendar dates](DATE.md) |
| `TimeSpan` | [Time intervals](TIME_SPAN.md) |
| Signed component `TimeSpan` constructors | [Constructing durations from components](TIME_SPAN_CONSTRUCTION.md) |
| Fractional `TimeSpan` factories, microsecond and nanosecond components/totals | [Fractional time intervals](TIME_SPAN_FRACTIONAL.md) |
| `TimeSpan.Duration`, `Multiply`, `Divide` | [Scaling time intervals](TIME_SPAN_SCALING.md) |
| `TimeOfDay` | [Time of day](TIME_OF_DAY.md) |
| Clock `AddHours`, `AddMinutes`, `AddTicks` | [Adding units to a clock value](CLOCK_UNIT_ADDITION.md) |
| Direct date/time components and clock-duration conversion | [Date and clock components](TEMPORAL_COMPONENTS.md) |
| `DateTime` | [Date and time](DATE_TIME.md) |
| `DateTimeOffset` | [Civil timestamps with an explicit UTC offset](DATE_TIME_OFFSET.md) |
| Date/time component constructors | [Constructing dates and timestamps from components](TEMPORAL_CONSTRUCTION.md) |
| Date/time `AddTicks`, `AddDays` through `AddMicroseconds` | [Adding date and time units](TEMPORAL_UNIT_ADDITION.md) |
| `Stopwatch<C>`, `IMonotonicClock` | [Measuring intervals](STOPWATCH.md) |

### Text and Unicode

| APIs | Guide |
| --- | --- |
| `IntegerFormatting::TryFormat`, `TryFormatHex` | [Integer formatting into caller buffers](INTEGER_FORMATTING.md) |
| `Utf8`, incremental `Utf8Decoder`, borrowed `Utf8Scalars` | [UTF-8 codec](UTF8.md), [Incremental UTF-8 decoding](UTF8_DECODER.md), [Iterating UTF-8 scalar values](UTF8_SCALARS.md) |
| `Rune` | [Unicode scalar values](RUNE.md) |
| `Utf16`, incremental `Utf16Decoder`, borrowed `Utf16Scalars`, `UnicodeTranscoding` | [Strict UTF-16 code-unit helpers](UTF16.md), [Borrowed UTF-16 scalar iteration](UTF16_SCALARS.md), [Counted Unicode transcoding](UNICODE_TRANSCODING.md) |
| `Ascii` classification, comparison, case conversion, and case-insensitive hashing | [ASCII byte utilities](ASCII.md) |

### Encodings, compression, and formats

| APIs | Guide |
| --- | --- |
| `Base64`, unpadded `Base64Url`, `Hex`. Owning `Base64Text`, `Base64UrlText`, `HexText` helpers and writer `WriteHex` | [Base64](BINARY_CODECS.md) |
| `Deflate::Decompress` | [Raw DEFLATE](../Standard/core/Compression/DEFLATE.md) |
| `Gzip::DecompressMember` | [Gzip members](../Standard/core/Compression/GZIP.md) |
| `Zlib::Decompress` | [Zlib streams](../Standard/core/Compression/ZLIB.md) |
| `ZipArchive`, `ZipArchiveEntry` | [Reading ZIP archives](../Standard/core/Compression/ZIP.md) |
| `ZipArchiveWriter` | [Writing stored ZIP archives](../Standard/core/Compression/ZIP_WRITER.md) |
| `Base32`, `Base32Text` | [Padded Base32](BASE32.md) |
| `Base32Encoder`, `Base32Decoder` | [Streaming Base32 output](BASE32_ENCODER.md), [Streaming Base32 decoding](BASE32_DECODER.md) through borrowed writers |
| `Base64Encoder`, `Base64Decoder` | [Streaming Base64 output](BASE64_ENCODER.md), [Streaming Base64 decoding](BASE64_DECODER.md) through borrowed writers |
| `HexDecoder` | [Streaming hexadecimal decoding](HEX_DECODER.md) through a borrowed writer |
| `JsonString`, `JsonStringText`, writer `WriteJsonString` | [Quoting and unquoting a UTF-8 JSON string](JSON_STRING.md) |
| `JsonReader`, `JsonTokenKind`, `JsonParseException` | [JSON reader](JSON_READER.md) |
| `JsonText::Minify`, `JsonText::PrettyPrint` | [JSON text](JSON_TEXT.md) |
| `JsonPointer::TryResolve` | [Borrowed JSON Pointer lookup](JSON_POINTER.md) |
| `JsonWriter`, `JsonWriteException` | [JSON writer](JSON_WRITER.md) |
| `CsvReader`, `CsvReaderText`, `CsvParseException` | [CSV reader](CSV_READER.md) |
| `CsvWriter`, borrowed String-row `WriteRecord` | [CSV writer](CSV_WRITER.md) |
| `PercentEncoding`, optional String conveniences | [Component percent encoding](BINARY_CODECS.md#component-percent-encoding) |

### Paths, bits, and binary data

| APIs | Guide |
| --- | --- |
| `Paths`, `PathStyle` | [Lexical paths](../Standard/core/IO/PATHS.md) |
| `Paths.HasExtension` | [Checking for a path extension](PATH_EXTENSION_TEST.md) |
| `MemoryStream` | [Owning byte streams](../Standard/core/IO/MEMORY_STREAM.md) |
| `MemoryStream.WriteTo` | [Writing an entire memory stream](MEMORY_STREAM_WRITE_TO.md) |
| `MemoryStream.ToBytes` | [Taking an owning snapshot](MEMORY_STREAM_BYTES.md) |
| `BitOperations` | [Integer bit operations](BIT_OPERATIONS.md) |
| Native-width masks and signed power checks | [Native-width bit operations](NATIVE_BIT_OPERATIONS.md) |
| `BitOperations.Log2`, `Math.ILogB` | [Integer base-two logarithms](INTEGER_LOGARITHMS.md) |
| `BitConverter` | [Floating-point representation](BIT_CONVERTER.md) |
| `BinaryPrimitives` | [Binary scalar buffers](BINARY_PRIMITIVES.md) |
| Integer `ReverseEndianness` | [Reversing integer byte order](REVERSE_ENDIANNESS.md) |
| Buffer `TryReverseEndianness`, span `ReverseEndianness` | [Reversing byte order in buffers](REVERSE_ENDIANNESS_BUFFERS.md) |
| `BinaryBufferReader::TryReadTo` | [Bounded delimiter fields](../Standard/core/Binary/BINARY_READ_TO.md) |
| `BitPrimitives`, `BitOrder` | [Bit fields in byte buffers](BIT_FIELDS.md) |
| `VarInt` | [Variable-length integers](VARINT.md) |
| `ByteSearch` | [Counted byte searches](BYTE_SEARCH.md) |
| `MatchesWildcard` | [Counted byte wildcard matching](WILDCARD_MATCHING.md) |
| `EditDistance::TryCompute` | [Counted-byte edit distance](EDIT_DISTANCE.md) |
| `BinaryBufferReader`, `BinaryBufferWriter` | [Borrowed binary buffer cursors](BINARY_CURSORS.md) |

### Hashing and raw storage

| APIs | Guide |
| --- | --- |
| `CRC32`, `CRC32C`, `Adler32` | [Byte checksums](CHECKSUMS.md) |
| `SHA256` | [SHA-256 byte digests](SHA256.md) |
| `SHA512` | [SHA-512 byte digests](SHA512.md) |
| `HMACSHA256`, `HMACSHA512` | [HMAC-SHA256 over counted bytes](HMAC_SHA256.md), [HMAC-SHA512 over counted bytes](HMAC_SHA512.md) with counted keys |
| `HKDFSHA256` | [HKDF-SHA256 key extraction and expansion](HKDF_SHA256.md) |
| `Arena` | [Arena storage](ARENA.md) |
| `Memory::Copy`, `Move`, `Fill`, `Compare`, `Equal` | [Raw byte operations](MEMORY.md) |
| `Memory::AlignUp`, `AlignDown`, `IsAligned` | [Integer alignment](MEMORY.md#integer-alignment) |

## Libc and host runtime

| Area | APIs |
| --- | --- |
| Console | `Console::Write`, `WriteLine`, `Read`, `ReadByte`, `PeekByte`, `ReadExactly`, `ReadLine`, `TryReadLine` into buffers, String, or a borrowed writer. `Out`, `Error`, flush operations |
| Console redirection | [`IsInputRedirected`, `IsOutputRedirected`, `IsErrorRedirected`](HOST_RUNTIME.md#stream-redirection) |
| Formatted console output | [Primitive values and borrowed `IFormattable` values](CONSOLE_FORMATTING.md) |
| Managed console output | [Managed objects and temporary references](CONSOLE_MANAGED_FORMATTING.md) |
| Files | `File`, `Directory`, `FileStream`, `SeekOrigin`, `FileLines`, `FileLineSequence`, `FileBinary`, [`FileHash`](FILE_HASH.md), [`FileVarInt`](FILE_VARINT.md) |
| Allocation | Default replaceable `Memory::Allocate` and `Memory::Free` |
| Managed storage | Replaceable collector selection and `TracingCollector` |
| Exceptions | Default unhandled exception termination |
| Floating text | Floating-point formatting and [hosted C-string parsing](FLOATING_PARSING.md) |
| Environment | Borrowed `Environment::GetVariable` and owning `EnvironmentText` conveniences |

See [host runtime APIs](HOST_RUNTIME.md) and
[optional platform libraries](../libraries/README.md).

## Runnable examples

The [example index](../examples/README.md) lists command-line programs using
these APIs, with build commands and sample input.

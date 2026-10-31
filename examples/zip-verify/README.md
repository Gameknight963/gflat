# ZIP payload verification

Build and run from the repository root:

```console
gflat build examples/zip-verify/zip-verify.gfproj --run -- package.zip
```

The command reads one archive and verifies the entries referenced by its central directory. It checks stored bytes or decodes raw DEFLATE, then compares the complete uncompressed byte count and IEEE CRC-32 with the directory metadata. It prints only `Verified N entries` after every entry passes. Entry names and payload bytes are never printed, extracted, executed, or used as filesystem paths.

Supported input is the classic single-disk ZIP layout with methods 0 (stored) and 8 (DEFLATE). Local headers must agree with central versions, flags, methods, timestamps and counted filename bytes. With general-purpose bit 3 clear, local CRC and sizes must also match. With bit 3 set, local CRC/sizes must be zero and the following classic data descriptor must match the central values. Both descriptors with and without the optional signature are supported. The central sizes bound the payload; the verifier does not scan compressed bytes looking for a descriptor signature.

For supported methods 0 and 8, zero compressed bytes, zero uncompressed bytes and CRC 0 are accepted as the ZIP container's no-data empty-file case described by APPNOTE section 4.3.8. This does not change the raw DEFLATE API, where empty input means `NeedInput`. As a common reader tolerance, a complete explicitly compressed empty stream such as `03 00` is accepted too. Any nonempty method-8 payload is decoded and must consume exactly its compressed slice, produce exactly the declared count and match the CRC. Zero compressed bytes with a nonzero output count or CRC fails.

The following byte-count limits apply before payload allocation or decoding:

| Limit | Maximum |
|---|---:|
| Input archive | 128 MiB |
| One entry's uncompressed data | 16 MiB |
| Sum of declared uncompressed data | 128 MiB |
| Sum of declared compressed data | 128 MiB |
| Shared candidate-directory/local-metadata scan budget | 128 MiB |

All entry references are charged, including duplicates or overlapping references. The sum checks use remaining-budget subtraction. Empty-output compressed data still consumes the compressed-work budget. The scan budget charges each plausible EOCD candidate's directory size before walking it, plus each checked local header/name/extra extent. Exhaustion is an error, including when a later archive-comment candidate consumes the budget before an earlier candidate could be tried. Fixed header probes and entry counts are also bounded by the classic format. These are byte/work limits, not a deadline or a total process-memory guarantee.

The archive is held in one owning byte vector, whose storage remains unchanged while readers borrow it. At most one entry's decoded output is held at a time. Vector growth and normal runtime state have their own overhead; the limits describe archive and decoded byte counts.

ZIP64 sentinels or extra-field ID 1, nonzero disk numbers, encryption, unsupported flag bits, methods other than 0/8, extraction versions above 2.0, compressed/encrypted directories, digital signatures and trailing bytes after the archive comment are unsupported. Extra fields must be structurally bounded ID/length/value sequences; non-ZIP64 contents are otherwise skipped. UTF-8 flags and attributes do not cause filename decoding or path interpretation. There is no SFX offset adjustment, dictionary support, interval/alias canonicalization or inspection of unreferenced bytes. Checksum verification describes the referenced contents and does not authenticate an archive.

The EOCD search accepts a candidate only when its comment extent reaches EOF and its bounded central walk contains exactly the declared entries. An invalid candidate in a comment is skipped while the budget permits. A comment containing another fully valid directory/EOCD interpretation can select that later interpretation; this command does not claim a unique canonical interpretation.

Exit codes are 0 for completed verification and output, 1 for input/format/limit/allocation/checksum/output failure, and 2 for an incorrect argument count. Validation happens before stdout output, although a later output error can leave a partial summary. Error reporting on stderr is best effort. The input file is opened for reading only.

Format reference: [PKWARE APPNOTE](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT), sections 4.3.7, 4.3.9, 4.3.12, 4.3.16 and 4.4.3-4.4.9. This example adds no public standard-library API or native dependency.

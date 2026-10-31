# ZIP payload verification

Build and run from the repository root:

```console
gflat build examples/zip-verify/zip-verify.gfproj --run -- package.zip
```

The command verifies entries referenced by an archive's central directory. It checks
stored bytes or decodes raw DEFLATE, then compares the uncompressed byte count and IEEE
CRC-32 with the directory metadata. After every entry passes, it prints `Verified N
entries`.

## Supported input

The tool accepts classic single-disk ZIP archives with methods 0 (stored) and 8
(DEFLATE). Local headers must match central versions, flags, methods, timestamps, and
counted filename bytes.

- With general-purpose bit 3 clear, local CRC and sizes must match
- With bit 3 set, local CRC and sizes must be zero, followed by a matching
  classic data descriptor. The descriptor's signature is optional
- Central sizes bound each payload and locate its descriptor
- An empty file may have zero compressed bytes, zero uncompressed bytes, and CRC 0
- A compressed empty stream such as `03 00` is also accepted
- Every nonempty DEFLATE payload must consume its exact compressed slice,
  produce the declared byte count, and match the CRC

Unsupported input:

- ZIP64 sentinels or extra-field ID 1
- Multiple disks, encryption, or unsupported flag bits
- Methods other than 0/8 or extraction versions above 2.0
- Compressed or encrypted directories, digital signatures, or trailing bytes
  after the archive comment
- Self-extracting offset adjustments

Extra fields must contain bounded ID/length/value sequences. Other extra-field contents
remain opaque. Filename bytes are used for header comparison only.

## Limits

The following byte-count limits apply before payload allocation or decoding:

| Limit | Maximum |
|---|---:|
| Input archive | 128 MiB |
| One entry's uncompressed data | 16 MiB |
| Sum of declared uncompressed data | 128 MiB |
| Sum of declared compressed data | 128 MiB |
| Shared candidate-directory/local-metadata scan budget | 128 MiB |

All entry references count toward these limits, including duplicates and overlapping
references. Empty-output compressed data counts toward the compressed-work budget. The
scan budget includes each candidate's directory size and checked local header, filename,
and extra fields. Exhaustion is an error, even when a later comment candidate consumes
the budget before an earlier candidate can be tried.

The archive occupies one owning byte vector, with at most one entry's decoded output
held at a time. Memory use also includes spare capacity and runtime state.

## Directory selection

The EOCD search accepts a candidate whose comment reaches EOF and whose bounded
directory contains exactly the declared entries. Invalid candidates in comments are
skipped while the budget permits. A comment containing another valid directory/EOCD
interpretation can select that later interpretation.

## Errors

- 0 for completed verification and output
- 1 for input, format, limit, allocation, checksum, or output failure
- 2 for an incorrect argument count

Validation finishes before output. A later output error can leave a partial summary.
Diagnostics go to stderr when writable. The archive is opened for reading.

Format reference: [PKWARE
APPNOTE](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT), sections 4.3.7,
4.3.9, 4.3.12, 4.3.16, and 4.4.3–4.4.9.

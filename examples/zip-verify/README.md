# Verify ZIP payloads

Check entry sizes and CRCs in a ZIP archive:

```console
gflat build examples/zip-verify/zip-verify.gfproj --run -- package.zip
```

The tool checks every entry referenced by the central directory and prints
`Verified N entries` after all pass.

## Supported archives

Input must be single-disk ZIP32 with stored or DEFLATE-compressed entries
(methods 0 and 8). Local headers must match the central metadata. Data
descriptors with or without their optional signature are accepted. Each decoded
entry must consume its compressed slice and match its declared size and CRC.
Empty entries may use zero byte counts and CRC 0, or an explicit compressed
empty stream.

ZIP64, encryption, split archives, self-extracting offset adjustments,
extraction versions above 2.0, and unsupported flags or compression methods are
rejected. Encrypted or compressed directories and directory signatures are also
unsupported. Archive comments must end at EOF. A comment containing another valid
directory and end record can select that later interpretation.

## Limits

| Limit | Maximum |
| --- | ---: |
| Input archive | 128 MiB |
| One entry's uncompressed data | 16 MiB |
| Sum of declared uncompressed data | 128 MiB |
| Sum of declared compressed data | 128 MiB |
| Directory and local-metadata scan budget | 128 MiB |

Duplicate and overlapping entry references count toward the limits. The scan
budget includes each candidate directory and checked local metadata. Exhausting
it is an error. Memory holds the complete archive and one decoded entry, plus
spare capacity and runtime overhead.

## Errors

Exit codes are 0 for success, 1 for malformed input, limit, checksum, or IO
failure, and 2 for invalid arguments. Errors go to stderr.

Format reference: [PKWARE APPNOTE](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT).

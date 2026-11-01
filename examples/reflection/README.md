# Formatting fields with reflection

This program prints each public instance field's name and formatted value:

```text
Count = 42
Name = Ada
```

Run from the repository root:

```console
gflat build examples/reflection/reflection.gfproj --run
```

`field_formatting.gf` expands `WriteFields<T>` at compile time. Each public field
must provide `Format(ITextWriter*)`, either as a member or an imported extension.
Private fields are skipped.

The formatter borrows the record and writes into a caller-provided writer. The
example uses a fixed buffer. A formatting or writer error leaves the completed
prefix in the destination.

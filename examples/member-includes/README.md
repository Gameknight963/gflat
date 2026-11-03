# Reusable members with includes

`Point` and `Pair<T>` include the same three headers to add field equality,
clearing, and metadata printing. The headers use `typeof(*this)` to discover
the type they belong to, including its generic arguments.

Run from the repository root:

```console
gflat build examples/member-includes/member-includes.gfproj --run
```

Output:

```text
Equal points: true
Equal after clearing: false
Cleared pair: true
Point fields:
  x: int
  y: int
Pair<int> fields:
  first: int
  second: int
```

## The headers

- `equality.gfh` compares declared instance fields, including private fields. Each field must support `!=`. `Equals` takes its argument by value, following the type's copy rules
- `clear_fields.gfh` assigns each field its default value. Fields must be writable and support default initialization
- `field_metadata.gfh` adds static methods that count and print declared fields. Printing uses the `printf` declaration in `main.gf`

These headers are included separately inside each receiving type, so they allow
repeated inclusion. A nested type becomes the owner of headers included inside it.

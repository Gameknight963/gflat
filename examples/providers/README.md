# Compile-time providers

Run from the repository root:

```sh
dotnet run --project gflat -- build examples/providers/providers.gfproj --run
```

The program checks two checksums of `abc` and exits with zero on success.

- `Checksums.gf` declares a static interface and a generic library scope
- `Algorithms.gf` provides two implementations
- `main.gf` selects each implementation with an alias

`Sum::Result` and `Xor::Result` are different types because they belong to different
specializations. The compiler resolves provider calls directly.

See the [language specification](../../docs/LANGUAGE_SPEC.md) for static interfaces and
constrained aliases.

# Compile-time providers

Run from the repository root:

```sh
dotnet run --project gflat -- build examples/providers/providers.gfproj --run
```

The program checks two checksums of `abc` and exits with zero on success.

- `Checksums.gf` declares a static interface and a generic library scope.
- `span_algorithms.gf` provides two implementations.
- `main.gf` selects each implementation once with an alias, then uses qualified names normally.

`Sum::Result` and `Xor::Result` are different types because they belong to different specializations. The compiler resolves provider calls directly; there is no provider instance or interface dispatch table.

This exercises the general provider mechanism. It does not select or change the garbage collector. The sum and XOR algorithms are deliberately simple and are not cryptographic hashes.

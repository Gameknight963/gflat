# Managed references in native storage

Run from the repository or nightly bundle root:

```sh
gflat build examples/managed/managed.gfproj --run
```

The example puts `Node^` references in a native `std::Vector<Node^>`. Each node
also references itself. Collection keeps the nodes alive while the vector holds
them, then reclaims all 20 cycles after `Clear()` removes those references.

`new^` constructs an object through `Memory::Allocate` and transfers the
allocation to `GC::Adopt`. The compiler registers root storage; the default
collector also scans native allocations made through `Memory`.

The current collector is single-threaded and non-moving. Managed pointees cannot
have destructors. Conservative scanning can retain objects longer than expected;
compiler temporary roots remain registered until their function exits.

See the [GC API and replacement contracts](../../std/libc/README.md) for automatic
collection, explicit adoption, and custom runtimes.

# Managed references in native storage

Run from the repository or nightly bundle root:

```sh
gflat build examples/managed/managed.gfproj --run
```

The example puts `Node^` references in a native `std::Vector<Node^>`. Each node
also references itself. Collection keeps the nodes alive while the vector holds
them, then reclaims all 20 cycles after `Clear()` removes those references.

`new^` constructs an object using the provider selected by the global `GC`
alias, which defaults to `std::TracingCollector`. The compiler reports initialized
managed-reference slots in locals, temporaries, fields, and native vector elements.
The collector scans those slots and never guesses from unrelated native bytes.

The bundled collector is written in gflat, is single-threaded, and does not move
objects. Managed pointees cannot have destructors. The Vector ends element
lifetimes before releasing its native buffer; no hidden allocator interception
is needed to retire its roots.

See the [GC API and replacement contracts](../../std/libc/README.md) for automatic
collection, explicit adoption, and custom runtimes.

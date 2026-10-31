# Managed references in native storage

Run from the repository or nightly bundle root:

```sh
gflat build examples/managed/managed.gfproj --run
```

The example puts `Node^` references in a native `Standard::Collections::Vector<Node^>`.
Each node also references itself. Collection keeps the nodes alive while the vector
holds them, then reclaims all 20 cycles after `Clear()` removes those references.

## Collector selection

`new^` constructs an object using the provider selected by the global `GC` alias, which
defaults to `Standard::TracingCollector`. The compiler reports initialized
managed-reference slots in locals, temporaries, fields, and native vector elements. The
collector scans those slots to find reachable objects.

The bundled collector is written in gflat and uses single-threaded, non-moving
collection. Managed pointees cannot have destructors. The vector ends element lifetimes
before releasing its native buffer.

See the [GC API and replacement contracts](../../Standard/libc/README.md) for automatic
collection, explicit adoption, and custom runtimes.

# Managed references in native storage

Run from the repository or nightly bundle root:

```sh
gflat build examples/managed/managed.gfproj --run
```

The example stores twenty `Node^` references in a native `Vector<Node^>`.
Each node also references itself. Collection keeps the nodes alive while the
vector holds them, then reclaims the cycles after `Clear()` removes those
references.

`new^` uses the collector selected by the global `GC` alias. The default,
`Standard::TracingCollector`, performs single-threaded, non-moving collection.
The compiler reports managed-reference slots in locals, fields, temporaries,
and vector elements. Managed pointees cannot have destructors.

See [managed memory](../../Standard/libc/README.md#managed-memory) for collector selection
and replacement contracts.

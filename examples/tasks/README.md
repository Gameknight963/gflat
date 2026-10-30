# In-memory task list

Build from the repository or nightly bundle root:

```sh
gflat build examples/tasks/tasks.gfproj
```

Run `examples/tasks/bin/Debug/tasks.exe` directly (prefix the path with `./` on
Linux), then enter commands such as:

```text
add Finish String tests
add Write Standard
done 2
list
quit
```

Tasks own their titles in a `Vector<Task>`. The reusable input `String` can be
cleared or grown without changing existing titles. Completing a task modifies
the stored element through the vector's reference indexer. Nothing is saved to
disk.

The example uses `Console::TryReadLine`, counted String output, strict decimal
parsing, and String prefix matching. LF and CRLF end commands; a final command
without a newline is accepted. An embedded NUL or bare CR in a title remains
part of its counted contents. Console output uses the host's text-stream newline
handling. Input is byte-oriented, with no terminal encoding conversion.

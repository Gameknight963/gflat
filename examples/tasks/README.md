# In-memory task list

Build the interactive task list:

```sh
gflat build examples/tasks/tasks.gfproj
```

Run `examples/tasks/bin/Debug/tasks.exe`, then enter commands such as:

```text
add Finish String tests
add Write Standard
done 2
list
quit
```

`add` stores a title, `done` completes a numbered task, and `list` shows the
current tasks. Tasks last for the current run.

The example stores tasks in a `Vector<Task>`. It copies accepted titles from a
reusable input String and updates stored tasks through the vector's reference
indexer.

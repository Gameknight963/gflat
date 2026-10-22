# JSON text

`std::JsonText::Minify(readonly(char)* input, nuint count, ITextWriter* output)`
validates one complete counted UTF-8 JSON document, then writes it without JSON
whitespace outside strings. It preserves every other byte: escape spelling,
number spelling, property order, and duplicate property names. It adds no newline
and does not flush the writer.

Validation uses `JsonReader`, including its strict UTF-8 and surrogate rules.
Empty input is invalid. Invalid or incomplete input throws `JsonParseException`
with the reader's message and byte offset. Validation failures, including nesting
stack allocation failure, produce no output. The reader's stack is released
before the first output call. A later writer exception propagates unchanged and
may leave partial output; calls retain no state.

Input is borrowed: keep all `count` bytes live and unchanged throughout the call.
They must not overlap storage that the destination may modify or reallocate.
The writer consumes each input run synchronously and must not retain its pointer.
The helper uses no owning string, DOM, or output scratch buffer; only the
reader's nesting stack may allocate.

For selective source use, include `JsonText.gf` alongside the dependencies of
`JsonReader` and `ITextWriter`; `String` and `JsonWriter` are not required.

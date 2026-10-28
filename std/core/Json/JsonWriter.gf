using std::Text;
using std::Collections;
namespace std::Json;

/// Owns nesting state and borrows the output writer, without buffering the document.
/// Keep the destination stable and alive; input must not alias storage that writing may modify.
/// Destruction writes nothing and does not complete the document, flush or close the destination.
/// Validation, ordering and pre-output nesting allocation failures remain retryable.
/// After an output exception, writes and Complete fail; discard this instance, which has no Reset.
public struct JsonWriter
{
    private static const byte ArrayFirst = (byte)0;
    private static const byte ArrayNext = (byte)1;
    private static const byte ObjectFirst = (byte)2;
    private static const byte ObjectNext = (byte)3;
    private static const byte ObjectValue = (byte)4;

    private ITextWriter* output;
    private Vector<byte> stack;
    private bool rootWritten;
    private bool failed;
    private readonly(bool) indented;

    /// Borrows the destination without clearing it; output is compact by default.
    /// Indented mode sends two-space indentation and LF to the sink; no final newline is added.
    public JsonWriter(ITextWriter* destination, bool indented = false)
    {
        output = destination; stack = new(); rootWritten = false; failed = false;
        this.indented = indented;
    }

    private immutable void CheckUsable()
    {
        if (failed) throw new JsonWriteException("JSON writer cannot continue after output failure");
    }

    private void CheckValue()
    {
        this.CheckUsable();
        if (stack.Count == 0)
        {
            if (rootWritten) throw new JsonWriteException("JSON document already has a root value");
        }
        else
        {
            byte state = stack[stack.Count - 1];
            if (state == ObjectFirst || state == ObjectNext)
                throw new JsonWriteException("Write a JSON property name before its value");
        }
    }

    private static void CheckString(readonly(char)* data, nuint length)
    {
        nuint required = 0;
        if (!JsonString::TryGetQuotedLength(data, length, ref required))
            throw new JsonWriteException("Invalid UTF-8 or length overflow in JSON string");
    }

    private static nuint CStringLength(readonly(char)* text) nothrow
    {
        nuint length = 0;
        while (text[length] != '\0') length++;
        return length;
    }

    // Reserve before emitting a delimiter: allocation failure must not consume
    // the parent's value position or leave half an opening operation written.
    private void ReserveFrame()
    {
        if (stack.Count < stack.Capacity) return;
        nuint maximum = (nuint)0 - (nuint)1;
        if (stack.Count == maximum) throw new JsonWriteException("JSON nesting depth overflow");
        nuint grown = 4;
        if (stack.Count != 0)
        {
            grown = maximum;
            if (stack.Count <= maximum / 2) grown = stack.Count * 2;
        }
        stack.Reserve(grown);
    }

    private void WriteIndent(nuint depth)
    {
        output.Write("\n", 1);
        for (nuint i = 0; i < depth; i++) output.Write("  ", 2);
    }

    private void WriteValuePrefix()
    {
        if (stack.Count == 0) return;
        byte state = stack[stack.Count - 1];
        if (state == ArrayNext) output.Write(",", 1);
        if (indented && (state == ArrayFirst || state == ArrayNext)) this.WriteIndent(stack.Count);
    }

    private void CommitValue()
    {
        if (stack.Count == 0) rootWritten = true;
        else
        {
            nuint last = stack.Count - 1;
            if (stack[last] == ObjectValue) stack[last] = ObjectNext;
            else stack[last] = ArrayNext;
        }
    }

    private void WriteToken(readonly(char)* data, nuint length)
    {
        this.CheckValue();
        failed = true;
        this.WriteValuePrefix();
        output.Write(data, length);
        this.CommitValue();
        failed = false;
    }

    private void Start(byte state, readonly(char)* delimiter)
    {
        this.CheckValue();
        this.ReserveFrame();
        failed = true;
        this.WriteValuePrefix();
        output.Write(delimiter, 1);
        this.CommitValue();
        stack.Add(state); // Capacity was reserved before any output.
        failed = false;
    }

    /// Writes an object opener where a value is expected, automatically separating values.
    /// Reserves nesting capacity before output; allocation failure preserves state and output.
    public void WriteStartObject() { this.Start(ObjectFirst, "{"); }
    /// Writes an array opener where a value is expected, automatically separating values.
    /// Reserves nesting capacity before output; allocation failure preserves state and output.
    public void WriteStartArray() { this.Start(ArrayFirst, "["); }

    /// Writes a closing object delimiter when the current object awaits a name or its end.
    /// A missing property value or wrong container is a retryable structural error with no output.
    public void WriteEndObject()
    {
        this.CheckUsable();
        if (stack.Count == 0) throw new JsonWriteException("No JSON object is open");
        byte state = stack[stack.Count - 1];
        if (state != ObjectFirst && state != ObjectNext)
            throw new JsonWriteException("Cannot close JSON object before its property value or inside an array");
        failed = true;
        if (indented && state == ObjectNext) this.WriteIndent(stack.Count - 1);
        output.Write("}", 1);
        stack.Pop(); failed = false;
    }

    /// Writes a closing array delimiter only when an array is the current container.
    /// A wrong or missing container is a retryable structural error with no output.
    public void WriteEndArray()
    {
        this.CheckUsable();
        if (stack.Count == 0) throw new JsonWriteException("No JSON array is open");
        byte state = stack[stack.Count - 1];
        if (state != ArrayFirst && state != ArrayNext)
            throw new JsonWriteException("Cannot close JSON array inside an object");
        failed = true;
        if (indented && state == ArrayNext) this.WriteIndent(stack.Count - 1);
        output.Write("]", 1);
        stack.Pop(); failed = false;
    }

    /// Writes a counted strict-UTF-8 property name with JSON quoting, including embedded zeros.
    /// Requires an object awaiting a name; a value must follow the name.
    /// Invalid text or ordering writes nothing and is retryable; sink errors can leave partial output.
    public void WritePropertyName(readonly(char)* data, nuint length)
    {
        this.CheckUsable();
        if (stack.Count == 0) throw new JsonWriteException("JSON property name requires an object");
        nuint last = stack.Count - 1;
        byte state = stack[last];
        if (state != ObjectFirst && state != ObjectNext)
            throw new JsonWriteException("JSON property name requires an object awaiting a name");
        CheckString(data, length);
        failed = true;
        if (state == ObjectNext) output.Write(",", 1);
        if (indented) this.WriteIndent(stack.Count);
        std::Json::WriteJsonString(output, data, length);
        if (indented) output.Write(": ", 2); else output.Write(":", 1);
        stack[last] = ObjectValue; failed = false;
    }

    /// Uses bytes before the first zero as the property name, then validates and quotes them.
    public void WritePropertyName(readonly(char)* text)
        { this.WritePropertyName(text, CStringLength(text)); }

    /// Writes counted strict UTF-8 as a JSON string, retaining embedded zeros through escapes.
    /// Input must stay unchanged and separate from destination storage that writing may modify.
    /// Validation failures emit nothing and are retryable; output exceptions fault this writer.
    public void WriteString(readonly(char)* data, nuint length)
    {
        this.CheckValue();
        CheckString(data, length);
        failed = true;
        this.WriteValuePrefix();
        std::Json::WriteJsonString(output, data, length);
        this.CommitValue(); failed = false;
    }

    /// Uses bytes before the first zero as string contents, then validates and quotes them.
    public void WriteString(readonly(char)* text) { this.WriteString(text, CStringLength(text)); }
    /// Writes the true or false JSON literal in the current value position.
    public void WriteBoolean(bool value)
    {
        if (value) this.WriteToken("true", 4); else this.WriteToken("false", 5);
    }
    /// Writes the null JSON literal in the current value position.
    public void WriteNull() { this.WriteToken("null", 4); }

    /// Writes an invariant decimal integer using a fixed stack buffer, without allocating number text.
    public void WriteNumber(long value)
    {
        char[21] buffer; nuint written = 0;
        if (!IntegerFormatting::TryFormat(value, buffer, 21, ref written))
            throw new JsonWriteException("JSON integer formatting failed");
        this.WriteToken(buffer, written);
    }
    /// Writes an invariant unsigned decimal integer without allocating number text.
    public void WriteNumber(ulong value)
    {
        char[21] buffer; nuint written = 0;
        if (!IntegerFormatting::TryFormat(value, buffer, 21, ref written))
            throw new JsonWriteException("JSON integer formatting failed");
        this.WriteToken(buffer, written);
    }
    /// Writes the full signed 128-bit decimal value using fixed stack storage.
    public void WriteNumber(extralong value)
    {
        char[40] buffer; nuint written = 0;
        if (!IntegerFormatting::TryFormat(value, buffer, 40, ref written))
            throw new JsonWriteException("JSON integer formatting failed");
        this.WriteToken(buffer, written);
    }
    public void WriteNumber(sbyte value) { this.WriteNumber((long)value); }
    public void WriteNumber(byte value) { this.WriteNumber((ulong)value); }
    public void WriteNumber(short value) { this.WriteNumber((long)value); }
    public void WriteNumber(ushort value) { this.WriteNumber((ulong)value); }
    public void WriteNumber(int value) { this.WriteNumber((long)value); }
    public void WriteNumber(uint value) { this.WriteNumber((ulong)value); }
    public void WriteNumber(nint value) { this.WriteNumber((long)value); }
    public void WriteNumber(nuint value) { this.WriteNumber((ulong)value); }

    /// Validates exactly one counted JSON number, with no surrounding whitespace or extra data.
    /// Preserves its literal spelling without conversion through a machine numeric type.
    /// Validation failure writes nothing and is retryable; output failure can partially write and fault.
    public void WriteRawNumber(readonly(char)* data, nuint length)
    {
        this.CheckValue();
        if (length == 0 || data[0] != '-' && (data[0] < '0' || data[0] > '9'))
            throw new JsonWriteException("Expected one complete JSON number without whitespace");
        JsonReader reader = new(data, length);
        try
        {
            if (!reader.Read() || reader.Kind != JsonTokenKind::Number || reader.TokenOffset != 0 || reader.TokenLength != length)
                throw new JsonWriteException("Expected one complete JSON number without whitespace");
            if (reader.Read()) throw new JsonWriteException("Expected one complete JSON number without whitespace");
        }
        catch (ref JsonParseException error)
        {
            throw new JsonWriteException("Expected one complete JSON number without whitespace");
        }
        this.WriteToken(data, length);
    }

    /// Validates that this writer has written one root value and closed every container.
    /// Writes nothing, does not flush or close, and may be called repeatedly after success.
    /// An incomplete-document error remains retryable after supplying missing values or closing delimiters.
    public immutable void Complete()
    {
        this.CheckUsable();
        if (!rootWritten || stack.Count != 0) throw new JsonWriteException("Incomplete JSON document");
    }
}

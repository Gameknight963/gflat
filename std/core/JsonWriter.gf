using std::Collections;
namespace std;

// Owns nesting state; borrows the destination without flushing or closing it.
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

    public void WriteStartObject() { this.Start(ObjectFirst, "{"); }
    public void WriteStartArray() { this.Start(ArrayFirst, "["); }

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
        std::WriteJsonString(output, data, length);
        if (indented) output.Write(": ", 2); else output.Write(":", 1);
        stack[last] = ObjectValue; failed = false;
    }

    public void WritePropertyName(readonly(char)* text)
        { this.WritePropertyName(text, CStringLength(text)); }

    public void WriteString(readonly(char)* data, nuint length)
    {
        this.CheckValue();
        CheckString(data, length);
        failed = true;
        this.WriteValuePrefix();
        std::WriteJsonString(output, data, length);
        this.CommitValue(); failed = false;
    }

    public void WriteString(readonly(char)* text) { this.WriteString(text, CStringLength(text)); }
    public void WriteBoolean(bool value)
    {
        if (value) this.WriteToken("true", 4); else this.WriteToken("false", 5);
    }
    public void WriteNull() { this.WriteToken("null", 4); }

    public void WriteNumber(long value)
    {
        char[21] buffer; nuint written = 0;
        if (!IntegerFormatting::TryFormat(value, buffer, 21, ref written))
            throw new JsonWriteException("JSON integer formatting failed");
        this.WriteToken(buffer, written);
    }
    public void WriteNumber(ulong value)
    {
        char[21] buffer; nuint written = 0;
        if (!IntegerFormatting::TryFormat(value, buffer, 21, ref written))
            throw new JsonWriteException("JSON integer formatting failed");
        this.WriteToken(buffer, written);
    }
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

    // Validation only: does not close containers, flush, or modify the output.
    public immutable void Complete()
    {
        this.CheckUsable();
        if (!rootWritten || stack.Count != 0) throw new JsonWriteException("Incomplete JSON document");
    }
}

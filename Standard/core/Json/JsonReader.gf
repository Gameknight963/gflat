using Standard::Memory;
using Standard::Collections;
namespace Standard::Json;

/// Reads tokens from borrowed counted UTF-8 input and owns its nesting stack.
/// Keep input alive and unchanged for reader use and every retained raw token view.
/// Nesting state grows as needed and can throw BadAllocException.
public struct JsonReader
{
    private static const byte ArrayFirst = (byte)0;
    private static const byte ArrayValue = (byte)1;
    private static const byte ArrayComma = (byte)2;
    private static const byte ObjectFirst = (byte)3;
    private static const byte ObjectKey = (byte)4;
    private static const byte ObjectColon = (byte)5;
    private static const byte ObjectValue = (byte)6;
    private static const byte ObjectComma = (byte)7;

    private readonly(char)* input;
    private nuint length;
    private nuint position;
    private Vector<byte> stack;
    private bool rootComplete;
    private bool failed;
    private JsonTokenKind kind;
    private nuint tokenOffset;
    private nuint tokenLength;
    private nuint depth;

    /// Borrows count bytes without copying them or requiring a zero terminator.
    /// Starts before the first token. Construction does not validate the document.
    public JsonReader(readonly(char)* data, nuint count)
    {
        input = data; length = count; position = 0; stack = new();
        rootComplete = false; failed = false;
        kind = JsonTokenKind::None; tokenOffset = 0; tokenLength = 0; depth = 0;
    }

    /// Current token kind. None before reading, after completion or failure, and after Reset.
    public JsonTokenKind Kind { immutable get nothrow => kind; }
    /// Current lexeme byte offset in the original input, meaningful when Kind is not None.
    public nuint TokenOffset { immutable get nothrow => tokenOffset; }
    /// Current raw byte count, including string or property-name quotes. Zero when Kind is None.
    public nuint TokenLength { immutable get nothrow => tokenLength; }
    /// Enclosing container count. Matching opening and closing delimiters have the same depth.
    public nuint Depth { immutable get nothrow => depth; }
    /// Borrows the exact current lexeme, keeping quotes, escapes and original number spelling.
    /// Container lexemes are single delimiters. None returns a canonical empty span.
    /// Saved spans borrow input, not reader state. Reading, resetting, moving or destroying the reader preserves them.
    /// Keep input alive and unchanged for every use. The getter copies no bytes and allocates nothing.
    public Span<readonly(char)> RawToken
    {
        immutable get
        {
            if (kind == JsonTokenKind::None) return new Span<readonly(char)>();
            return new Span<readonly(char)>(input + tokenOffset, tokenLength);
        }
    }

    /// Compares decoded text of a current String or PropertyName with counted UTF-8, without allocation.
    /// Other token kinds return false without reading text. The input regions may overlap.
    /// Stops at the first mismatch.
    public immutable bool ValueTextEquals(readonly(char)* text, nuint count) nothrow
    {
        if (kind != JsonTokenKind::String && kind != JsonTokenKind::PropertyName) return false;
        return JsonString::ValueTextEquals(input + tokenOffset, tokenLength, text, count);
    }

    /// Rewinds the same borrowed input and clears the failure latch and current token metadata.
    /// Retains nesting-stack capacity and does not replace, validate or repair the input.
    public void Reset()
    {
        stack.Clear(); position = 0; rootComplete = false; failed = false;
        kind = JsonTokenKind::None; tokenOffset = 0; tokenLength = 0; depth = 0;
    }

    private void Fail(readonly(char)* message, nuint offset)
    {
        throw new JsonParseException(message, offset);
    }

    private void SkipWhitespace() nothrow
    {
        while (position < length)
        {
            char value = input[position];
            if (value != ' ' && value != '\t' && value != '\r' && value != '\n') return;
            position++;
        }
    }

    private static bool Digit(char value) nothrow => value >= '0' && value <= '9';

    private void ReadString()
    {
        position++;
        bool escaped = false;
        while (position < length)
        {
            char value = input[position++];
            if (!escaped && value == '"')
            {
                nuint decoded = 0;
                if (!JsonString::TryGetUnquotedLength(input + tokenOffset, position - tokenOffset, ref decoded))
                    this.Fail("Invalid JSON string", tokenOffset);
                return;
            }
            if (!escaped && value == '\\') escaped = true;
            else escaped = false;
        }
        this.Fail("Unterminated JSON string", length);
    }

    private void ReadNumber()
    {
        if (input[position] == '-') position++;
        if (position == length) this.Fail("Incomplete JSON number", position);
        if (input[position] == '0') position++;
        else
        {
            if (input[position] < '1' || input[position] > '9') this.Fail("Invalid JSON number", position);
            while (position < length && Digit(input[position])) position++;
        }
        if (position < length && input[position] == '.')
        {
            position++;
            if (position == length || !Digit(input[position])) this.Fail("Missing JSON fraction digits", position);
            while (position < length && Digit(input[position])) position++;
        }
        if (position < length && (input[position] == 'e' || input[position] == 'E'))
        {
            position++;
            if (position < length && (input[position] == '+' || input[position] == '-')) position++;
            if (position == length || !Digit(input[position])) this.Fail("Missing JSON exponent digits", position);
            while (position < length && Digit(input[position])) position++;
        }
    }

    private void ReadLiteral(readonly(char)* text, nuint count)
    {
        for (nuint i = 0; i < count; i++)
        {
            if (position == length || input[position] != text[i]) this.Fail("Invalid JSON literal", position);
            position++;
        }
    }

    private void CompleteValue()
    {
        if (stack.Count == 0) rootComplete = true;
        else
        {
            nuint last = stack.Count - 1;
            byte state = stack[last];
            if (state == ArrayFirst || state == ArrayValue) stack[last] = ArrayComma;
            else stack[last] = ObjectComma;
        }
    }

    /// On a property name, first reads its value. An opener advances through its matching closer.
    /// Leaves that closing token current. Other scalar, end and None tokens do not advance.
    /// Validates skipped content, not later or trailing input. Keep reading until Read returns false.
    /// Any exception requires Reset before another Read or Skip, including nesting allocation failure.
    public void Skip()
    {
        if (failed) this.Fail("Reset JSON reader after a failed read", position);
        if (kind == JsonTokenKind::PropertyName) this.Read();
        if (kind != JsonTokenKind::StartObject && kind != JsonTokenKind::StartArray) return;
        nuint startDepth = depth;
        while (this.Read())
        {
            if (depth == startDepth && (kind == JsonTokenKind::EndObject || kind == JsonTokenKind::EndArray)) return;
        }
    }

    /// Returns true for the next token. False only after one root and trailing JSON whitespace are validated.
    /// Stopping early does not validate the rest. Reads after successful completion keep returning false.
    /// Malformed or incomplete data throws JsonParseException. Nesting growth can throw BadAllocException.
    /// Any exception latches failure until Reset. Completion or error leaves Kind None and TokenLength zero.
    public bool Read()
    {
        if (failed) this.Fail("Reset JSON reader after a failed read", position);
        failed = true;
        kind = JsonTokenKind::None; tokenLength = 0; depth = 0;
        while (true)
        {
            this.SkipWhitespace();
            tokenOffset = position;
            if (stack.Count == 0 && rootComplete)
            {
                if (position != length) this.Fail("Trailing JSON data", position);
                failed = false; return false;
            }
            if (position == length) this.Fail("Incomplete JSON document", position);
            char value = input[position];
            if (stack.Count != 0)
            {
                nuint last = stack.Count - 1;
                byte state = stack[last];
                if ((state == ArrayFirst || state == ArrayComma) && value == ']')
                {
                    stack.Pop(); position++; kind = JsonTokenKind::EndArray; depth = stack.Count; break;
                }
                if ((state == ObjectFirst || state == ObjectComma) && value == '}')
                {
                    stack.Pop(); position++; kind = JsonTokenKind::EndObject; depth = stack.Count; break;
                }
                if (state == ArrayComma || state == ObjectComma)
                {
                    if (value != ',') this.Fail("Expected JSON comma or closing delimiter", position);
                    if (state == ArrayComma) stack[last] = ArrayValue; else stack[last] = ObjectKey;
                    position++; continue;
                }
                if (state == ObjectColon)
                {
                    if (value != ':') this.Fail("Expected JSON colon", position);
                    stack[last] = ObjectValue; position++; continue;
                }
                if (state == ObjectFirst || state == ObjectKey)
                {
                    if (value != '"') this.Fail("Expected JSON property name", position);
                    this.ReadString(); stack[last] = ObjectColon;
                    kind = JsonTokenKind::PropertyName; depth = stack.Count; break;
                }
            }
            depth = stack.Count;
            if (value == '{' || value == '[')
            {
                this.CompleteValue();
                if (value == '{') stack.Add(ObjectFirst); else stack.Add(ArrayFirst);
                position++;
                if (value == '{') kind = JsonTokenKind::StartObject; else kind = JsonTokenKind::StartArray;
            }
            else
            {
                if (value == '"') { this.ReadString(); kind = JsonTokenKind::String; }
                else if (value == '-' || Digit(value)) { this.ReadNumber(); kind = JsonTokenKind::Number; }
                else if (value == 't') { this.ReadLiteral("true", 4); kind = JsonTokenKind::True; }
                else if (value == 'f') { this.ReadLiteral("false", 5); kind = JsonTokenKind::False; }
                else if (value == 'n') { this.ReadLiteral("null", 4); kind = JsonTokenKind::Null; }
                else this.Fail("Expected JSON value", position);
                this.CompleteValue();
            }
            break;
        }
        tokenLength = position - tokenOffset; failed = false; return true;
    }
}

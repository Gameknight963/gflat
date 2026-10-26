using std::Memory;
using std::Collections;
namespace std;

// Owns its nesting stack; borrows immutable counted UTF-8 input.
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

    public JsonReader(readonly(char)* data, nuint count)
    {
        input = data; length = count; position = 0; stack = new();
        rootComplete = false; failed = false;
        kind = JsonTokenKind::None; tokenOffset = 0; tokenLength = 0; depth = 0;
    }

    public JsonTokenKind Kind { immutable get nothrow => kind; }
    public nuint TokenOffset { immutable get nothrow => tokenOffset; }
    public nuint TokenLength { immutable get nothrow => tokenLength; }
    public nuint Depth { immutable get nothrow => depth; }
    public Span<readonly(char)> RawToken
    {
        immutable get
        {
            if (kind == JsonTokenKind::None) return new Span<readonly(char)>();
            return new Span<readonly(char)>(input + tokenOffset, tokenLength);
        }
    }

    public immutable bool ValueTextEquals(readonly(char)* text, nuint count) nothrow
    {
        if (kind != JsonTokenKind::String && kind != JsonTokenKind::PropertyName) return false;
        return JsonString::ValueTextEquals(input + tokenOffset, tokenLength, text, count);
    }

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

    // Leave containers on their closing token, so the next Read reaches a sibling.
    // A property name includes its value; other current tokens do not advance.
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

    // false means the entire document, including trailing whitespace, was validated.
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

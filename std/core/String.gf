namespace std;

// Owns bytes, conventionally UTF-8, without validating their encoding.
// Length and Capacity exclude the trailing null byte.
struct String : ITextWriter, IFormattable, IStringConvertable, IHashable, IEquatable<String>, IComparable<String>
{
    private char* data;
    private nuint length;
    private nuint capacity;

    public String() nothrow
    {
        // Empty storage is never written or freed; the first growth allocates.
        data = (char*)"";
        length = 0;
        capacity = 0;
    }

    // A value copy owns an independent buffer. Moving needs no user-written code.
    public copy String(immutable(String)* source)
    {
        data = (char*)"";
        length = 0;
        capacity = 0;
        this.AppendLiteral(source.data, source.length);
    }

    public nuint Length { immutable get nothrow => length; }
    public nuint Capacity { immutable get nothrow => capacity; }
    public bool IsEmpty { immutable get nothrow => length == 0; }
    public static const nuint NotFound = (nuint)0 - (nuint)1;
    public readonly(char)* Data { immutable get nothrow => data; }

    // Null-terminated input. Use the count overload for buffers or embedded nulls.
    public static String From(readonly(char)* text)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return String::From(text, count);
    }

    public static String From(readonly(char)* text, nuint count)
    {
        String result = new String();
        result.AppendLiteral(text, count);
        return result;
    }

    public static String operator s""(readonly(char)* text, nuint count)
    {
        return String::From(text, count);
    }

    public void Reserve(nuint requested)
    {
        if (requested <= capacity) return;
        nuint maximum = (nuint)0 - (nuint)1;
        if (requested == maximum) throw new Exception("String capacity overflow", 1);
        void*? allocation = Memory::Allocate(requested + 1);
        if (allocation == null) throw new BadAllocException("String allocation failed");
        char* replacement = (char*)allocation;
        for (nuint i = 0; i < length; i++) replacement[i] = data[i];
        replacement[length] = '\0';
        if (capacity != 0) Memory::Free((void*)data);
        data = replacement;
        capacity = requested;
    }

    // Length is counted bytes: a null fill does not shorten the string.
    public void Resize(nuint requested, char fill = '\0')
    {
        if (requested == length) return;
        if (requested > length)
        {
            this.Reserve(requested);
            for (nuint i = length; i < requested; i++) data[i] = fill;
        }
        length = requested;
        data[length] = '\0';
    }

    public void AppendLiteral(readonly(char)* text, nuint count)
    {
        if (count == 0) return;
        nuint maximum = (nuint)0 - (nuint)1;
        if (count > maximum - 1 - length)
            throw new Exception("String length overflow", 1);
        nuint required = length + count;
        if (required > capacity)
        {
            nuint grown = required;
            if (capacity <= (maximum - 1) / 2 && capacity * 2 > grown)
                grown = capacity * 2;
            void*? allocation = Memory::Allocate(grown + 1);
            if (allocation == null) throw new BadAllocException("String allocation failed");
            char* replacement = (char*)allocation;
            for (nuint i = 0; i < length; i++) replacement[i] = data[i];
            // Copy before freeing: text may refer to this string's own buffer.
            for (nuint i = 0; i < count; i++) replacement[length + i] = text[i];
            replacement[required] = '\0';
            if (capacity != 0) Memory::Free((void*)data);
            data = replacement;
            capacity = grown;
        }
        else
        {
            // memmove order also handles overlapping input without libc.
            if ((readonly(char)*)(data + length) > text && (readonly(char)*)(data + length) < text + count)
            {
                nuint i = count;
                while (i != 0) { i--; data[length + i] = text[i]; }
            }
            else
                for (nuint i = 0; i < count; i++) data[length + i] = text[i];
            data[required] = '\0';
        }
        length = required;
    }

    public void Append(readonly(char)* text, nuint count) { this.AppendLiteral(text, count); }

    public void Append(readonly(char)* text)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        this.AppendLiteral(text, count);
    }

    public void Append(immutable(String)* text) { this.AppendLiteral(text.Data, text.Length); }
    public void Append(char value) { this.AppendLiteral(&value, 1); }

    // Self-insertion snapshots the input before either shifting or reallocating.
    public void Insert(nuint index, readonly(char)* text, nuint count)
    {
        if (index > length) throw new Exception("String insertion out of range");
        if (count == 0) return;
        nuint maximum = (nuint)0 - (nuint)1;
        if (count > maximum - 1 - length) throw new Exception("String length overflow");
        // Trimming may leave readable input beyond Length within our allocation.
        if (text >= (readonly(char)*)data && text <= (readonly(char)*)(data + capacity))
        {
            String snapshot = String::From(text, count);
            this.Insert(index, snapshot.Data, snapshot.Length);
            return;
        }
        nuint required = length + count;
        if (required > capacity)
        {
            nuint grown = required;
            if (capacity <= (maximum - 1) / 2 && capacity * 2 > grown) grown = capacity * 2;
            this.Reserve(grown);
        }
        // Backwards shifting includes the existing terminator.
        nuint remaining = length - index + 1;
        while (remaining != 0) { remaining--; data[index + count + remaining] = data[index + remaining]; }
        for (nuint i = 0; i < count; i++) data[index + i] = text[i];
        length = required;
    }

    public void Insert(nuint index, readonly(char)* text)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        this.Insert(index, text, count);
    }

    public void Insert(nuint index, char value) { this.Insert(index, &value, 1); }
    public void Insert(nuint index, immutable(String)* text) { this.Insert(index, text.Data, text.Length); }

    // Byte offsets, as with the indexer. The result owns an independent buffer.
    public immutable String Substring(nuint start, nuint count)
    {
        if (start > length || count > length - start) throw new Exception("String substring out of range");
        return String::From(data + start, count);
    }

    public immutable String Substring(nuint start)
    {
        if (start > length) throw new Exception("String substring out of range");
        return this.Substring(start, length - start);
    }

    public immutable nuint IndexOf(char value, nuint start = 0)
    {
        if (start > length) throw new Exception("String search start out of range");
        for (nuint i = start; i < length; i++) if (data[i] == value) return i;
        return NotFound;
    }

    public immutable nuint IndexOf(readonly(char)* text, nuint count, nuint start = 0)
    {
        if (start > length) throw new Exception("String search start out of range");
        if (count == 0) return start;
        if (count > length - start) return NotFound;
        nuint remaining = length - start;
        nuint found = ByteSearch::IndexOf((readonly(byte)*)(data + start), remaining, (readonly(byte)*)text, count);
        if (found == remaining) return NotFound;
        return start + found;
    }

    public immutable nuint IndexOf(readonly(char)* text)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return this.IndexOf(text, count, 0);
    }

    public immutable nuint LastIndexOf(char value) nothrow
    {
        nuint i = length;
        while (i != 0)
        {
            i--;
            if (data[i] == value) return i;
        }
        return NotFound;
    }

    public immutable nuint LastIndexOf(readonly(char)* text, nuint count) nothrow
    {
        if (count == 0) return length;
        if (count > length) return NotFound;
        nuint found = ByteSearch::LastIndexOf((readonly(byte)*)data, length, (readonly(byte)*)text, count);
        if (found == length) return NotFound;
        return found;
    }

    public immutable nuint LastIndexOf(readonly(char)* text) nothrow
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return this.LastIndexOf(text, count);
    }

    public immutable bool Contains(char value) => this.IndexOf(value) != NotFound;
    public immutable bool Contains(readonly(char)* text) => this.IndexOf(text) != NotFound;
    public immutable bool Contains(readonly(char)* text, nuint count) => this.IndexOf(text, count, 0) != NotFound;

    public immutable bool StartsWith(readonly(char)* text, nuint count) nothrow
    {
        if (count > length) return false;
        for (nuint i = 0; i < count; i++) if (data[i] != text[i]) return false;
        return true;
    }

    public immutable bool StartsWith(readonly(char)* text) nothrow
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return this.StartsWith(text, count);
    }

    public immutable bool EndsWith(readonly(char)* text, nuint count) nothrow
    {
        if (count > length) return false;
        nuint start = length - count;
        for (nuint i = 0; i < count; i++) if (data[start + i] != text[i]) return false;
        return true;
    }

    public immutable bool EndsWith(readonly(char)* text) nothrow
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return this.EndsWith(text, count);
    }

    public void Remove(nuint start, nuint count)
    {
        if (start > length || count > length - start) throw new Exception("String removal out of range");
        if (count == 0) return;
        for (nuint i = start; i < length - count; i++) data[i] = data[i + count];
        length -= count;
        data[length] = '\0';
    }

    public void Replace(char oldValue, char newValue) nothrow
    {
        for (nuint i = 0; i < length; i++) if (data[i] == oldValue) data[i] = newValue;
    }

    // Replace left-to-right nonoverlapping byte sequences. Build the result
    // before modifying this string, so both inputs may borrow its storage.
    public void Replace(readonly(char)* oldText, nuint oldCount, readonly(char)* newText, nuint newCount)
    {
        if (oldCount == 0) throw new Exception("String replacement pattern is empty");
        nuint matches = 0;
        nuint position = 0;
        nuint found = this.IndexOf(oldText, oldCount, position);
        while (found != NotFound)
        {
            matches++;
            position = found + oldCount;
            found = this.IndexOf(oldText, oldCount, position);
        }
        if (matches == 0) return;
        nuint required = length;
        if (newCount > oldCount)
        {
            nuint increase = newCount - oldCount;
            nuint maximum = (nuint)0 - (nuint)1;
            if (matches > (maximum - 1 - length) / increase) throw new Exception("String length overflow");
            required += matches * increase;
        }
        else required -= matches * (oldCount - newCount);
        String result = new();
        result.Reserve(required);
        position = 0;
        found = this.IndexOf(oldText, oldCount, position);
        while (found != NotFound)
        {
            result.AppendLiteral(data + position, found - position);
            result.AppendLiteral(newText, newCount);
            position = found + oldCount;
            found = this.IndexOf(oldText, oldCount, position);
        }
        result.AppendLiteral(data + position, length - position);
        *this = move result;
    }

    public void Replace(readonly(char)* oldText, readonly(char)* newText)
    {
        nuint oldCount = 0;
        while (oldText[oldCount] != '\0') oldCount++;
        nuint newCount = 0;
        while (newText[newCount] != '\0') newCount++;
        this.Replace(oldText, oldCount, newText, newCount);
    }

    // Total width is measured in bytes; padding never truncates existing text.
    public void PadLeft(nuint width, char padding = ' ')
    {
        if (width <= length) return;
        this.Reserve(width);
        nuint added = width - length;
        for (nuint i = length; i != 0; i--) data[i + added - 1] = data[i - 1];
        for (nuint i = 0; i < added; i++) data[i] = padding;
        length = width;
        data[length] = '\0';
    }

    public void PadRight(nuint width, char padding = ' ')
    {
        if (width <= length) return;
        this.Reserve(width);
        for (nuint i = length; i < width; i++) data[i] = padding;
        length = width;
        data[length] = '\0';
    }

    private static bool IsAsciiWhitespace(char value) nothrow
        => value == ' ' || value == '\t' || value == '\r' || value == '\n' || value == (char)11 || value == (char)12;

    // Trims ASCII whitespace only; no locale or Unicode category assumptions.
    public void TrimStart() nothrow
    {
        nuint start = 0;
        while (start < length && IsAsciiWhitespace(data[start])) start++;
        if (start == 0) return;
        for (nuint i = 0; i < length - start; i++) data[i] = data[i + start];
        length -= start;
        data[length] = '\0';
    }

    public void TrimEnd() nothrow
    {
        nuint end = length;
        while (end != 0 && IsAsciiWhitespace(data[end - 1])) end--;
        if (end == length) return;
        length = end;
        data[length] = '\0';
    }

    public void Trim() nothrow
    {
        this.TrimEnd();
        this.TrimStart();
    }

    public void Clear() nothrow
    {
        length = 0;
        if (capacity != 0) data[0] = '\0';
    }

    public immutable String ToString() => String::From(data, length);

    // Borrowed text: valid until mutation, movement, or destruction of this string.
    public immutable readonly(char)* ToCString() nothrow => data;

    public void Write(readonly(char)* text, nuint count) { this.AppendLiteral(text, count); }
    public immutable void Format(ITextWriter* output) { output.Write(data, length); }

    public immutable String Clone() { return String::From(data, length); }

    public immutable bool Equals(immutable(String)* other) nothrow
    {
        if (length != other.Length) return false;
        for (nuint i = 0; i < length; i++) if (data[i] != other.Data[i]) return false;
        return true;
    }

    // Exact counted bytes, including embedded NUL. A length mismatch reads no input.
    public immutable bool Equals(readonly(char)* text, nuint count) nothrow
        => length == count && this.StartsWith(text, count);

    // Null-terminated input. Stop on mismatch or its first terminator.
    public immutable bool Equals(readonly(char)* text) nothrow
    {
        for (nuint i = 0; i < length; i++)
            if (text[i] == '\0' || data[i] != text[i]) return false;
        return text[length] == '\0';
    }

    public immutable nuint GetHashCode() nothrow => Hashing::Bytes(data, length);

    // Ordinal UTF-8 byte order, independent of locale and null termination.
    public immutable int CompareTo(immutable(String)* other) nothrow
    {
        nuint limit = length;
        if (other.Length < limit) limit = other.Length;
        for (nuint i = 0; i < limit; i++)
        {
            if ((byte)data[i] < (byte)other.Data[i]) return -1;
            if ((byte)data[i] > (byte)other.Data[i]) return 1;
        }
        if (length < other.Length) return -1;
        if (length > other.Length) return 1;
        return 0;
    }

    public char this[nuint index]
    {
        immutable get
        {
            if (index >= length) throw new Exception("String index out of range", 3);
            return data[index];
        }
        set
        {
            if (index >= length) throw new Exception("String index out of range", 3);
            data[index] = value;
        }
    }

    ~String() { if (capacity != 0) Memory::Free((void*)data); }
}

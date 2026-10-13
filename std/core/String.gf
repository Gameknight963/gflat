namespace std;

// Owns UTF-8 bytes. Length and Capacity exclude the trailing null byte.
struct String : ITextWriter, IFormattable
{
    private char* data;
    private nuint length;
    private nuint capacity;

    public String()
    {
        // Empty storage is never written or freed; the first append allocates.
        data = (char*)c"";
        length = 0;
        capacity = 0;
    }

    // A value copy owns an independent buffer. Moving needs no user-written code.
    public copy String(readonly(String)* source)
    {
        data = (char*)c"";
        length = 0;
        capacity = 0;
        this.AppendLiteral(source.data, source.length);
    }

    public nuint Length { readonly get => length; }
    public nuint Capacity { readonly get => capacity; }
    public readonly(char)* Data { readonly get => data; }

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
        if (requested == maximum) throw new Exception(c"String capacity overflow", 1);
        void*? allocation = Memory::Allocate(requested + (nuint)1);
        if (allocation == null) throw new Exception(c"String allocation failed", 2);
        char* replacement = (char*)allocation;
        for (nuint i = 0; i < length; i++) replacement[i] = data[i];
        replacement[length] = '\0';
        if (capacity != (nuint)0) Memory::Free((void*)data);
        data = replacement;
        capacity = requested;
    }

    public void AppendLiteral(readonly(char)* text, nuint count)
    {
        if (count == (nuint)0) return;
        nuint maximum = (nuint)0 - (nuint)1;
        if (count > maximum - (nuint)1 - length)
            throw new Exception(c"String length overflow", 1);
        nuint required = length + count;
        if (required > capacity)
        {
            nuint grown = required;
            if (capacity <= (maximum - (nuint)1) / (nuint)2 && capacity * (nuint)2 > grown)
                grown = capacity * (nuint)2;
            void*? allocation = Memory::Allocate(grown + (nuint)1);
            if (allocation == null) throw new Exception(c"String allocation failed", 2);
            char* replacement = (char*)allocation;
            for (nuint i = 0; i < length; i++) replacement[i] = data[i];
            // Copy before freeing: text may refer to this string's own buffer.
            for (nuint i = 0; i < count; i++) replacement[length + i] = text[i];
            replacement[required] = '\0';
            if (capacity != (nuint)0) Memory::Free((void*)data);
            data = replacement;
            capacity = grown;
        }
        else
        {
            // memmove order also handles overlapping input without libc.
            if ((readonly(char)*)(data + length) > text && (readonly(char)*)(data + length) < text + count)
            {
                nuint i = count;
                while (i != (nuint)0) { i--; data[length + i] = text[i]; }
            }
            else
                for (nuint i = 0; i < count; i++) data[length + i] = text[i];
            data[required] = '\0';
        }
        length = required;
    }

    public void Append(readonly(char)* text, nuint count) { this.AppendLiteral(text, count); }

    public void Clear()
    {
        length = 0;
        if (capacity != (nuint)0) data[0] = '\0';
    }

    // Borrowed text: valid until mutation, movement, or destruction of this string.
    public readonly readonly(char)* ToString() => data;

    public void Write(readonly(char)* text, nuint count) { this.AppendLiteral(text, count); }
    public readonly void Format(ITextWriter* output) { output.Write(data, length); }

    public readonly String Clone() { return String::From(data, length); }

    public readonly bool Equals(readonly(String)* other)
    {
        if (length != other.Length) return false;
        for (nuint i = 0; i < length; i++) if (data[i] != other.Data[i]) return false;
        return true;
    }

    public char this[nuint index]
    {
        readonly get
        {
            if (index >= length) throw new Exception(c"String index out of range", 3);
            return data[index];
        }
        set
        {
            if (index >= length) throw new Exception(c"String index out of range", 3);
            data[index] = value;
        }
    }

    ~String() { if (capacity != (nuint)0) Memory::Free((void*)data); }
}

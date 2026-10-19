namespace std;

TextLines Lines(this Span<readonly(char)> text) => new TextLines(text);
TextLines Lines(this Span<char> text) => new TextLines(text.AsReadOnly());

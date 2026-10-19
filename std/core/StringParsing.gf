namespace std;

// Borrow the counted bytes; parsing does not copy or modify the owning String.
int ParseInt(this ref readonly(String) text) => std::ParseInt(text.Data, text.Length);
bool TryParseInt(this ref readonly(String) text, int* result) nothrow
    => std::TryParseInt(text.Data, text.Length, result);

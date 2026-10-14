namespace std;

// A compiler-produced snapshot. Names point to static UTF-8 text.
// This describes the queried type, not an object's dynamic runtime type.
public struct TypeInfo
{
    public readonly(char*) Name;
    public readonly(nuint) Size;
    public readonly(nuint) Alignment;
    public readonly(bool) IsPointer;
    public readonly(bool) IsManaged;
    public readonly(bool) IsNullable;
    public readonly(bool) IsReadOnly;
    public readonly(bool) HasDestructor;
}

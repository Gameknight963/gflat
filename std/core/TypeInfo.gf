namespace std;

public enum TypeKind
{
    Primitive = 0,
    Class = 1,
    Struct = 2,
    Enum = 3,
    Interface = 4,
    Pointer = 5,
    ManagedPointer = 6,
    Array = 7,
    FunctionPointer = 8,
    Reference = 9
}

// A compiler-produced snapshot. Names point to static UTF-8 text.
// This describes the queried type, not an object's dynamic runtime type.
public struct TypeInfo
{
    public readonly(char*) Name;
    public readonly(TypeKind) Kind;
    // No value layout exists for void or an interface itself. Their size and
    // alignment are zero; an interface pointer has its own meaningful layout.
    public readonly(bool) HasLayout;
    public readonly(nuint) Size;
    public readonly(nuint) Alignment;
    public readonly(bool) IsPointer;
    public readonly(bool) IsManaged;
    public readonly(bool) IsNullable;
    public readonly(bool) IsReadOnly;
    public readonly(bool) HasDestructor;
    // Pointer/managed/array/reference element; null for other kinds.
    // Descriptors point to immutable static storage, never GC allocations.
    public readonly(TypeInfo*?) ElementType;
    public readonly(bool) HasFixedLength;
    public readonly(nuint) Length;
    // Function signature only. Other kinds use null/null/0. ParameterTypes
    // points to a contiguous immutable descriptor array; zero parameters use null.
    public readonly(TypeInfo*?) ReturnType;
    public readonly(TypeInfo*?) ParameterTypes;
    public readonly(nuint) ParameterCount;
}

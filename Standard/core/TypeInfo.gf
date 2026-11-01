namespace Standard;

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
    Reference = 9,
    FunctionSignature = 10
}

// Runtime type data returned through Describe<T>()'s immutable static view.
public struct TypeInfo
{
    public readonly(char)* Name;
    public TypeKind Kind;
    public bool HasLayout;
    public nuint Size;
    public nuint Alignment;
    public bool IsPointer;
    public bool IsManaged;
    public bool IsNullable;
    public bool IsReadOnly;
    public bool IsImmutable;
    public bool HasDestructor;
    public immutable(TypeInfo)*? ElementType;
    public bool HasFixedLength;
    public nuint Length;
    public immutable(TypeInfo)*? ReturnType;
    // Each array entry points to the shared descriptor for one parameter type.
    public readonly(immutable(TypeInfo)*)*? ParameterTypes;
    public nuint ParameterCount;
    public immutable(FieldInfo)*? Fields;
    public nuint FieldCount;
    public immutable(TypeInfo)*? BaseType;
    public immutable(TypeInfo)*? CollectorType;

    public const TypeInfo() nothrow
    {
        Name = "";
        Kind = TypeKind::Primitive;
        HasLayout = false;
        Size = 0;
        Alignment = 0;
        IsPointer = false;
        IsManaged = false;
        IsNullable = false;
        IsReadOnly = false;
        IsImmutable = false;
        HasDestructor = false;
        ElementType = null;
        HasFixedLength = false;
        Length = 0;
        ReturnType = null;
        ParameterTypes = null;
        ParameterCount = 0;
        Fields = null;
        FieldCount = 0;
        BaseType = null;
        CollectorType = null;
    }
}

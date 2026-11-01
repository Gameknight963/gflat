namespace Standard;

// Describes one declared instance field. Offset counts bytes from the value's start.
public struct FieldInfo
{
    public readonly(char)* Name;
    public immutable(TypeInfo)* Type;
    public nuint Offset;
    public bool IsPublic;
    public bool IsPrivate;

    public const FieldInfo(readonly(char)* name, immutable(TypeInfo)* type,
        nuint offset, bool isPublic, bool isPrivate) nothrow
    {
        Name = name;
        Type = type;
        Offset = offset;
        IsPublic = isPublic;
        IsPrivate = isPrivate;
    }
}

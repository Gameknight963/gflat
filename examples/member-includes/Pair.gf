struct Pair<T>
{
    private T first;
    private T second;

    public Pair(T left, T right)
    {
        first = left;
        second = right;
    }

    #include "members/equality.gfh"
    #include "members/clear_fields.gfh"
    #include "members/field_metadata.gfh"
}

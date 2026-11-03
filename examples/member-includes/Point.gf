struct Point
{
    private int x;
    private int y;

    public Point(int horizontal, int vertical)
    {
        x = horizontal;
        y = vertical;
    }

    #include "members/equality.gfh"
    #include "members/clear_fields.gfh"
    #include "members/field_metadata.gfh"
}

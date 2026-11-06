extern "C" int printf(readonly(char)* format, ...);

readonly(char)* BooleanText(bool value)
{
    if (value) return "true";
    return "false";
}

int main()
{
    Point first = new(3, 4);
    Point second = new(3, 4);
    printf("Equal points: %s\n", BooleanText(first.Equals(second)));

    second.Clear();
    printf("Equal after clearing: %s\n", BooleanText(first.Equals(second)));

    Pair<int> counts = new(8, 13);
    Pair<int> empty = new(0, 0);
    counts.Clear();
    printf("Cleared pair: %s\n", BooleanText(counts.Equals(empty)));

    Point::PrintFields();
    Pair<int>::PrintFields();
    if (Point::FieldCount() != 2 || Pair<int>::FieldCount() != 2) return 1;
    return 0;
}

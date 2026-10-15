using std;

class Node
{
    public int id;
    public Node^? next;
    public Node(int value) { id = value; }
}

// References leave this function in the Vector's native allocation. Each node
// also retains itself, so reference counting alone would not reclaim this graph.
void BuildGraph(Vector<Node^>* roots)
{
    for (int i = 0; i < 20; i++)
    {
        Node^ node = new^ Node(i);
        node.next = node;
        roots.Add(node);
    }
}

int CheckGraph(Vector<Node^>* roots)
{
    int sum = 0;
    for (nuint i = 0; i < roots.Count; i++) sum += (*roots)[i].next!.id;
    return sum;
}

int main()
{
    Vector<Node^> roots = new Vector<Node^>();
    BuildGraph(&roots);
    GC::Collect();
    if (std::TracingCollector::ManagedCount != (nuint)20 || CheckGraph(&roots) != 190) return 1;
    Console::WriteLine("20 cyclic nodes survive through a native Vector.");

    roots.Clear();
    if (GC::Collect() != (nuint)20 || std::TracingCollector::ManagedCount != (nuint)0) return 2;
    Console::WriteLine("Removing the roots reclaims all 20 cycles.");
    return 0;
}

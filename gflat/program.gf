extern int printf(char* fmt, ...);
extern void* malloc(long size);

struct Point
{
    int x;
    int y;

    void Move(int dx, int dy)
    {
        this.x += dx;
        this.y += dy;
    }

    void Print()
    {
        printf("Point(%d, %d)\n", this.x, this.y);
    }

    public static Point operator +(Point a, Point b)
    {
        Point res;
        res.x = a.x + b.x;
        res.y = a.y + b.y;
        return res;
    }

    public static Point operator -(Point a)
    {
        Point res;
        res.x = 0 - a.x;
        res.y = 0 - a.y;
        return res;
    }

    public static Point operator *(Point p, int scale)
    {
        Point res;
        res.x = p.x * scale;
        res.y = p.y * scale;
        return res;
    }

    public static bool operator ==(Point a, Point b)
    {
        return a.x == b.x && a.y == b.y;
    }

    public static bool operator !=(Point a, Point b)
    {
        return !(a == b);
    }
}

interface IShape
{
    int Area();
    int Perimeter();
}

struct Rect : IShape
{
    int w;
    int h;

    int Area()
    {
        return this.w * this.h;
    }

    int Perimeter()
    {
        return 2 * (this.w + this.h);
    }
}

void PrintShape(char* name, IShape* s)
{
    printf("%s - Area: %d, Perimeter: %d\n", name, s.Area(), s.Perimeter());
}

void LogMessage(char* msg)
{
    printf("%s\n", msg);
}

long Add64(long a, long b)
{
    return a + b;
}

namespace Math
{
    public alias BinaryOp = int(int, int)*;

    public int Add(int a, int b)
    {
        return a + b;
    }

    public int Double(int x)
    {
        return x * 2;
    }
}

enum LogLevel : char
{
    Info = 1,
    Warning = 2,
    Error = 3
}

enum Status
{
    Pending,
    Running = 10,
    Done
}

int main()
{
    LogMessage("Top-level functions work!");

    Point p;
    p.x = 10;
    p.y = 20;
    p.Print();

    char[] a = "string";
    printf("buffer before: %s\n", a);
    a[0] = 'S';
    printf("buffer after: %s\n", a);

    p.Move(5, 5);
    p.Print();

    Point* ptr = &p;
    ptr.Move(100, 200);
    ptr.Print();

    printf("ptr address: 0x%llx, is_null: %d\n", ptr->address, ptr->is_null);

    defer printf("deferred cleanup: main finished!\n");

    Point* heapPoint = malloc(8L);
    defer heapPoint->free();
    heapPoint.x = 42;
    heapPoint.y = 84;
    heapPoint.Print();
    printf("heapPoint address: 0x%llx\n", heapPoint->address);

    long sum = Add64(1000000000L, 2000000000L);
    printf("sum: %lld\n", sum);

    int d = Math::Double(21);
    printf("double: %d\n", d);

    alias LocalOp = Math::BinaryOp;
    LocalOp op = &Math::Add;
    printf("function pointer call: %d\n", op(20, 22));

    int(int, int)*? maybeOp = null;
    printf("maybeOp is_null: %d\n", maybeOp->is_null);
    maybeOp = &Math::Add;
    printf("maybeOp is_null after assignment: %d, address: 0x%llx\n", maybeOp->is_null, maybeOp->address);

    Status status = Status::Running;
    printf("enum status: %d (expected 10), is Done: %d\n", status, Status.Done);
    LogLevel level = LogLevel::Info;
    printf("enum level: %d\n", level);

    uint u = 3000000000u;
    ulong ul = 10000000000000000000ul;
    printf("unsigned int: %u, unsigned long: %llu\n", u, ul);

    uint shifted = 1u << 4;
    uint rshifted = 0x80000000u >> 1;
    printf("shifted: %u, rshifted: 0x%x\n", shifted, rshifted);

    int convertedFromUInt = u;
    printf("implicit uint to int: %d\n", convertedFromUInt);

    long largeNum = 0x123456789abcdef0l;
    int truncated = (int)largeNum;
    printf("explicit cast long to int: 0x%x\n", truncated);

    nuint ptrAddress = (nuint)ptr;
    Point* restoredPtr = (Point*)ptrAddress;
    printf("nuint roundtrip address: 0x%llx\n", restoredPtr->address);

    int(int, int)* addLambda = (int a, int b) => a + b;
    printf("lambda add: %d\n", addLambda(19, 23));

    int(int, int)* mulLambda = static (int a, int b) => a * b;
    printf("static lambda mul: %d\n", mulLambda(6, 7));

    int(int)* loopLambda = (int n) =>
    {
        int s = 0;
        for (int i = 1; i <= n; i = i + 1)
        {
            s = s + i;
        }
        return s;
    };
    printf("block lambda sum 1..10: %d\n", loopLambda(10));

    Point pt1;
    pt1.x = 10;
    pt1.y = 20;
    Point pt2;
    pt2.x = 5;
    pt2.y = 7;
    Point pt3 = pt1 + pt2;
    printf("operator + : Point(%d, %d)\n", pt3.x, pt3.y);

    Point ptNeg = -pt1;
    printf("operator - : Point(%d, %d)\n", ptNeg.x, ptNeg.y);

    Point ptScaled = pt1 * 2;
    printf("operator * : Point(%d, %d)\n", ptScaled.x, ptScaled.y);

    pt1 += pt2;
    printf("operator +=: Point(%d, %d)\n", pt1.x, pt1.y);

    printf("operator ==: %d, !=: %d\n", pt1 == pt3, pt1 != pt2);

    byte byteVal = 200;
    sbyte sbyteVal = -50;
    short shortVal = 1000;
    ushort ushortVal = 50000;
    printf("small primitives: byte=%u, sbyte=%d, short=%d, ushort=%u\n", byteVal, sbyteVal, shortVal, ushortVal);

    int sumSmall = byteVal + sbyteVal + shortVal + ushortVal;
    printf("sum of small primitives widened to int: %d\n", sumSmall);

    byte byteTrunc = (byte)300;
    printf("byte truncated from 300: %u\n", byteTrunc);

    Rect rectStack;
    rectStack.w = 5;
    rectStack.h = 10;
    PrintShape("Stack Rect", &rectStack);

    Rect* rectHeap = (Rect*)malloc(8L);
    rectHeap.w = 7;
    rectHeap.h = 3;
    PrintShape("Heap Rect", rectHeap);
    rectHeap->free();

    return 0;
}
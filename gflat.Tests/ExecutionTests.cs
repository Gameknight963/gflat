using Xunit;

namespace gflat.Tests
{
    public class ExecutionTests
    {
        [Fact]
        public void SimpleReturnExitCode()
        {
            string code = """
                int main()
                {
                    int a = 20;
                    int b = 22;
                    return a + b;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void StructFieldsAndPointers()
        {
            string code = """
                struct Vector
                {
                    int x;
                    int y;
                }

                int main()
                {
                    Vector v;
                    v.x = 10;
                    v.y = 20;

                    Vector* ptr = &v;
                    ptr.x = 35;

                    return v.x + v.y;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(55, result.ExitCode);
        }

        [Fact]
        public void LoopsAndConditionals()
        {
            string code = """
                int main()
                {
                    int sum = 0;
                    for (int i = 0; i < 10; i++)
                    {
                        if (i == 5)
                        {
                            continue;
                        }
                        sum += i;
                    }
                    return sum;
                }
                """;

            // 0+1+2+3+4+6+7+8+9 = 40
            var result = CompilerTestHelper.Run(code);
            Assert.Equal(40, result.ExitCode);
        }

        [Fact]
        public void PrintfStandardOutput()
        {
            string code = """
                extern int printf(char* fmt, ...);

                void Greet(char* name)
                {
                    printf("Hello, %s!\n", name);
                }

                int main()
                {
                    Greet("gflat");
                    return 0;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Hello, gflat!", result.StandardOutput);
        }

        [Fact]
        public void StructMethodOnValueInstance()
        {
            string code = """
                struct Counter
                {
                    int count;

                    void Increment(int amount)
                    {
                        this.count += amount;
                    }

                    int Get()
                    {
                        return this.count;
                    }
                }

                int main()
                {
                    Counter c;
                    c.count = 5;
                    c.Increment(10);
                    return c.Get();
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(15, result.ExitCode);
        }

        [Fact]
        public void StructMethodOnPointerWithAutoDeref()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    void Move(int dx, int dy)
                    {
                        this.x += dx;
                        this.y += dy;
                    }
                }

                int main()
                {
                    Point p;
                    p.x = 10;
                    p.y = 20;

                    Point* ptr = &p;
                    ptr.Move(5, 10);

                    return p.x + p.y;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(45, result.ExitCode);
        }

        [Fact]
        public void StructMethodWithImplicitThisFields()
        {
            string code = """
                struct Vector
                {
                    int x;
                    int y;

                    void Add(int dx, int dy)
                    {
                        x += dx;
                        y += dy;
                    }

                    int Sum()
                    {
                        return x + y;
                    }
                }

                int main()
                {
                    Vector v;
                    v.x = 100;
                    v.y = 200;
                    v.Add(50, 50);
                    return v.Sum();
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(400, result.ExitCode);
        }

        [Fact]
        public void StructMethodInsideNamespace()
        {
            string code = """
                namespace Geometry
                {
                    struct Rect
                    {
                        int width;
                        int height;

                        int Area()
                        {
                            return this.width * this.height;
                        }
                    }
                }

                int main()
                {
                    Geometry::Rect r;
                    r.width = 6;
                    r.height = 7;
                    return r.Area();
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void PointerIsNullTest()
        {
            string code = """
                struct Node
                {
                    int value;
                }

                int main()
                {
                    Node*? empty = null;
                    if (!empty->is_null)
                    {
                        return 1;
                    }

                    Node n;
                    n.value = 42;
                    Node* ptr = &n;
                    if (ptr->is_null)
                    {
                        return 2;
                    }

                    return n.value;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void PointerAddressTest()
        {
            string code = """
                int main()
                {
                    int val = 123;
                    int* ptr = &val;
                    long addr = ptr->address;
                    if (addr == 0L)
                    {
                        return 1;
                    }

                    int*? nullPtr = null;
                    long nullAddr = nullPtr->address;
                    if (nullAddr != 0L)
                    {
                        return 2;
                    }

                    return 0;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(0, result.ExitCode);
        }

        [Fact]
        public void PointerFreeTest()
        {
            string code = """
                extern void* malloc(long size);

                struct Item
                {
                    int id;
                    int count;
                }

                int main()
                {
                    Item* item = malloc(16L);
                    if (item->is_null)
                    {
                        return 1;
                    }

                    item.id = 100;
                    item.count = 25;
                    int total = item.id + item.count;

                    item->free();
                    return total;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(125, result.ExitCode);
        }

        [Fact]
        public void FixedSizeArrayBufferAndIndex()
        {
            string code = """
                int main()
                {
                    int[4] nums;
                    nums[0] = 10;
                    nums[1] = 20;
                    nums[2] = 30;
                    nums[3] = 40;
                    return nums[0] + nums[1] + nums[2] + nums[3];
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(100, result.ExitCode);
        }

        [Fact]
        public void StringLiteralToStackArrayAndModification()
        {
            string code = """
                int main()
                {
                    char[] a = "hello";
                    a[0] = 'H';
                    // 'H' is ASCII 72
                    return a[0];
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(72, result.ExitCode);
        }

        [Fact]
        public void ArrayDecayToFunctionPointer()
        {
            string code = """
                int StrLen(char* s)
                {
                    int len = 0;
                    while (s[len] != '\0')
                    {
                        len++;
                    }
                    return len;
                }

                int main()
                {
                    char[7] a = "string";
                    return StrLen(a);
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(6, result.ExitCode);
        }

        [Fact]
        public void PointerIndexing()
        {
            string code = """
                int main()
                {
                    int[3] arr;
                    arr[0] = 5;
                    arr[1] = 15;
                    arr[2] = 25;

                    int* ptr = arr;
                    return ptr[0] + ptr[1] + ptr[2];
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(45, result.ExitCode);
        }
    }
}

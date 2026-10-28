using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ClassValueHeaderTests
{
    private const string Classes = """
        class Base {
            public int n = 11;
            public Base() {}
            public Base(immutable(Base)* source) { *this = *source; }
            public immutable virtual int Get() => 1;
        }
        class Derived : Base {
            public int[16] tail;
            public immutable override int Get() => 2;
        }
        """;

    [Theory]
    [InlineData("Base value = source;")]
    [InlineData("Consume(source);")]
    [InlineData("Base value = (Base)source;")]
    public void DirectDerivedToBaseValueConversionsRemainRejected(string statement)
        => Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(Classes + """
            void Consume(Base value) {}
            int main() {
                Derived source = new();
            """ + statement + "return 0;}"));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwnedValuesMatchTheirStorageAndBorrowedAssignmentsRetainDynamicIdentity(int optimization)
        => Run(Classes + """
            int Value(Base value) => value.Get();
            Base Returned(Base* value) => *value;
            int Borrow(ref Base value) => value.Get();
            void Assign(ref Base target, ref Base source) { target = source; }
            int main() {
                Derived source = new();
                Base* view = &source;
                Base initialized = *view;
                Base returned = Returned(view);
                Base casted = (Base)(*view);
                Base assigned = new();
                assigned = *view;
                if (initialized.Get() != 1 || returned.Get() != 1 || casted.Get() != 1 ||
                    assigned.Get() != 1 || Value(*view) != 1) return 1;
                if (view.Get() != 2 || Borrow(ref *view) != 2 || source.Get() != 2) return 2;
                Derived complete = source;
                if (complete.Get() != 2 || sizeof(Derived) <= sizeof(Base)) return 3;
                Base constructed = new(view);
                if (constructed.Get() != 1 || constructed.n != 11 || view.Get() != 2) return 4;

                Derived destination = new();
                Base* part = &destination;
                Base replacement = new();
                replacement.n = 42;
                *part = replacement;
                if (part.Get() != 2 || destination.Get() != 2 || destination.n != 42) return 5;
                replacement.n = 43;
                Assign(ref *part, ref replacement);
                if (part.Get() != 2 || destination.Get() != 2 || destination.n != 43 || view.Get() != 2) return 6;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NestedGenericFieldsAndFixedArraysKeepOwnedHeadersWithoutChangingPointerFields(int optimization)
        => Run("""
            namespace Model {
                class Base<T> {
                    public T payload;
                    public Base(T value) { payload = value; }
                    public immutable virtual int Get() => 1;
                }
                class Derived<T> : Base<T> {
                    public int[4] tail;
                    public Derived(T value) : base(value) {}
                    public immutable override int Get() => 2;
                }
            }
            alias B = Model::Base<int>;
            alias D = Model::Derived<int>;
            struct Holder {
                public B item;
                public B* pointer;
                public Holder(B* value) {
                    item = *value;
                    pointer = value;
                }
            }
            int main() {
                D source = new(17);
                B* view = &source;
                immutable(B)* observed = view;
                Holder original = new(view);
                Holder copied = original;
                B[2] values = [*view, *view];
                B[2] snapshot = values;
                if (original.item.Get() != 1 || copied.item.Get() != 1 ||
                    original.item.payload != 17 || copied.item.payload != 17) return 1;
                if (copied.pointer.Get() != 2 || observed.Get() != 2 || view.Get() != 2) return 2;
                for (nuint i = 0; i < 2; i++)
                    if (values[i].Get() != 1 || snapshot[i].Get() != 1 || snapshot[i].payload != 17) return 3;
                snapshot[0] = *view;
                copied.item = *view;
                if (snapshot[0].Get() != 1 || copied.item.Get() != 1 || copied.pointer.Get() != 2) return 4;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CustomCopyConstructorsObserveBorrowedSourceAndKeepTheirOwnHeader(int optimization)
        => Run("""
            int copies = 0;
            int observed = 0;
            class Base {
                public int n;
                public Base() { n = 11; }
                public copy Base(immutable(Base)* source) {
                    n = source.n;
                    observed = source.Get();
                    copies++;
                }
                public immutable virtual int Get() => 1;
            }
            class Derived : Base {
                public int[8] tail;
                public immutable override int Get() => 2;
            }
            int main() {
                Derived source = new();
                Base* view = &source;
                Base owned = *view;
                if (owned.Get() != 1 || observed != 2 || copies != 1 || view.Get() != 2) return 1;
                Base second = owned;
                if (second.Get() != 1 || observed != 1 || copies != 2) return 2;
                Derived destination = new();
                Base* part = &destination;
                int before = copies;
                *part = owned;
                if (part.Get() != 2 || destination.Get() != 2 || copies != before + 1 || observed != 1) return 3;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ResourcefulBaseAssignmentCleansOnlyItsSubobjectAndCopyFailureLeavesDestinationLive(int optimization)
        => Run("""
            int[5] deaths;
            bool failCopy = false;
            struct Resource {
                public int id;
                public Resource(int value) { id = value; }
                public copy Resource(immutable(Resource)* source) {
                    if (failCopy) throw new Exception("copy denied", 71);
                    id = source.id;
                }
                ~Resource() { deaths[id]++; }
            }
            class Base {
                public Resource item;
                public Base(int id) { item = new Resource(id); }
                public immutable virtual int Get() => 1;
            }
            class Derived : Base {
                public Resource tail;
                public Derived() : base(1) { tail = new Resource(2); }
                public immutable override int Get() => 2;
            }
            class Holder {
                public Base value;
                public Holder(int id) {
                    if (id == 4) value = new Base(4);
                    else value = new Base(4);
                    value = new Base(4);
                }
            }
            int main() {
                {
                    Derived destination = new();
                    Base* part = &destination;
                    Base replacement = new(3);
                    failCopy = true;
                    bool caught = false;
                    try { *part = replacement; }
                    catch (ref Exception error) {
                        if (error.GetCode() != 71) return 1;
                        caught = true;
                    }
                    failCopy = false;
                    if (!caught || part.Get() != 2 || destination.Get() != 2 ||
                        destination.item.id != 1 || destination.tail.id != 2) return 2;
                    if (deaths[1] != 0 || deaths[2] != 0 || deaths[3] != 0) return 3;
                    *part = replacement;
                    if (part.Get() != 2 || destination.Get() != 2 ||
                        destination.item.id != 3 || destination.tail.id != 2) return 4;
                    if (deaths[1] != 1 || deaths[2] != 0 || deaths[3] != 0) return 5;
                }
                if (deaths[1] != 1 || deaths[2] != 1 || deaths[3] != 2) return 6;
                {
                    Holder value = new(4);
                    if (value.value.Get() != 1 || value.value.item.id != 4 || deaths[4] != 1) return 7;
                }
                if (deaths[4] != 2) return 8;
                return 0;
            }
            """, optimization);

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(code, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
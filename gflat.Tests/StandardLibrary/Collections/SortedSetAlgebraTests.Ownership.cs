namespace gflat.Tests.Std.Collections;

public sealed partial class SortedSetAlgebraTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningAlgebraPreservesRepresentativesAndCleansUpEveryObservedFailure(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int allocated = 0;
            int released = 0;
            int attempts = 0;
            int comparisons = 0;
            int copies = 0;
            int predicates = 0;
            int failComparison = 0;
            int failCopy = 0;
            int failPredicate = 0;
            int failAllocation = 0;
            int observedComparisons = 0;
            int observedCopies = 0;
            int observedPredicates = 0;
            int observedAllocations = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (attempts == failAllocation) return null;
                    void*? result = malloc(bytes);
                    if (result != null) allocated++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { released++; free(storage); }
            }
            struct Key
            {
                public int Id;
                public String Label;
                public Key(int id, char label) { Id = id; Label = String::From(&label, 1); live++; }
                public copy Key(immutable(Key)* source)
                {
                    Id = source.Id; Label = source.Label; copies++;
                    if (copies == failCopy) throw new Exception("copy", 93);
                    live++;
                }
                ~Key() { live--; }
            }
            int Ascending(immutable(Key)* left, immutable(Key)* right)
            {
                comparisons++;
                if (comparisons == failComparison) throw new Exception("compare", 91);
                return left.Id - right.Id;
            }
            int Descending(immutable(Key)* left, immutable(Key)* right) => -Ascending(left, right);
            bool Even(immutable(Key)* value)
            {
                predicates++;
                if (predicates == failPredicate) throw new Exception("predicate", 92);
                return value.Id % 2 == 0;
            }
            void Perform(int operation, SortedSet<Key>* target, immutable(SortedSet<Key>)* other)
            {
                if (operation == 0) target.UnionWith(other);
                if (operation == 1) target.IntersectWith(other);
                if (operation == 2) target.ExceptWith(other);
                if (operation == 3) target.SymmetricExceptWith(other);
                if (operation == 4) target.RemoveWhere(Even);
            }
            bool Valid(immutable(SortedSet<Key>)* target, immutable(SortedSet<Key>)* other)
            {
                int previous = -1;
                var iterator = target.GetEnumerator();
                while (iterator.MoveNext())
                {
                    immutable(Key)* key = iterator.Current;
                    if (key.Id <= previous || key.Id > 5) return false;
                    char label = 'T';
                    if (key.Id == 5) label = 'O';
                    if (key.Label.Length != 1 || key.Label[0] != label) return false;
                    previous = key.Id;
                }
                int expected = 5;
                var source = other.GetEnumerator();
                while (source.MoveNext())
                {
                    if (source.Current.Id != expected || !source.Current.Label.Equals("O")) return false;
                    expected -= 2;
                }
                return expected == -1 && live == (int)(target.Count + other.Count);
            }
            int Exercise(int operation, int failureKind, int point)
            {
                {
                    SortedSet<Key> target = new(Ascending);
                    SortedSet<Key> other = new(Descending);
                    target.Reserve(5);
                    for (int i = 0; i < 5; i++) target.Add(new Key(i, 'T'));
                    for (int i = 1; i < 6; i += 2) other.Add(new Key(i, 'O'));
                    attempts = 0; comparisons = 0; copies = 0; predicates = 0;
                    if (failureKind == 1) failComparison = point;
                    if (failureKind == 2) failPredicate = point;
                    if (failureKind == 3) failCopy = point;
                    if (failureKind == 4) failAllocation = point;
                    bool caught = false;
                    int code = 0;
                    try { Perform(operation, &target, &other); }
                    catch (Exception* error) { caught = true; code = error.GetCode(); }
                    failComparison = 0; failPredicate = 0; failCopy = 0; failAllocation = 0;
                    if (failureKind == 0)
                    {
                        observedComparisons = comparisons; observedCopies = copies;
                        observedPredicates = predicates; observedAllocations = attempts;
                        int[5] counts = [6, 2, 3, 4, 2];
                        if (caught || target.Count != (nuint)counts[operation]) return 1;
                    }
                    else
                    {
                        if (!caught) return 2;
                        if (failureKind < 4 && code != 90 + failureKind) return 3;
                    }
                    if (!Valid(&target, &other)) return 4;
                }
                if (live != 0 || allocated != released) return 5;
                return 0;
            }
            int main()
            {
                for (int operation = 0; operation < 5; operation++)
                {
                    int result = Exercise(operation, 0, 0);
                    if (result != 0) return result;
                    int[4] limits = [observedComparisons, observedPredicates, observedCopies, observedAllocations];
                    for (int kind = 1; kind <= 4; kind++)
                    for (int point = 1; point <= limits[kind - 1]; point++)
                    {
                        result = Exercise(operation, kind, point);
                        if (result != 0) return result + 10;
                    }
                }
                {
                    SortedSet<Key> self = new(Ascending);
                    for (int i = 0; i < 3; i++) self.Add(new Key(i, 'T'));
                    comparisons = 0; copies = 0; failComparison = 1; failCopy = 1;
                    self.UnionWith(&self);
                    self.IntersectWith(&self);
                    if (self.Count != 3 || copies != 0 || comparisons != 0) return 20;
                    self.SymmetricExceptWith(&self);
                    if (!self.IsEmpty || live != 0) return 21;
                    failComparison = 0; failCopy = 0;
                }
                if (allocated != released) return 22;
                {
                    SortedSet<Key> target = new(Ascending);
                    SortedSet<Key> source = new(Ascending);
                    for (int i = 0; i < 256; i++) source.Add(new Key(i, 'O'));
                    comparisons = 0;
                    target.SymmetricExceptWith(&source);
                    if (target.Count != 256 || source.Count != 256 || comparisons > 256 * 64) return 23;
                }
                if (live != 0 || allocated != released) return 24;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ErasingOperationsSupportNoncopyableOwnersAndCommitCompletedRemovals(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Text;

            int live = 0;
            int visits = 0;
            struct Owner
            {
                public int Id;
                private String text;
                public Owner(int id) { Id = id; text = s"owned"; live++; }
                ~Owner() { live--; }
            }
            int Compare(immutable(Owner)* left, immutable(Owner)* right) => left.Id - right.Id;
            bool Remove(immutable(Owner)* value)
            {
                visits++;
                if (visits == 3) throw new Exception("predicate", 19);
                return value.Id % 2 == 0;
            }
            int main()
            {
                {
                    SortedSet<Owner> target = new(Compare);
                    SortedSet<Owner> other = new(Compare);
                    for (int i = 0; i < 5; i++) target.Add(new Owner(i));
                    bool caught = false;
                    try { target.RemoveWhere(Remove); }
                    catch (Exception* error) { caught = error.GetCode() == 19; }
                    if (!caught || target.Count != 4 || live != 4) return 1;
                    other.Add(new Owner(1));
                    other.Add(new Owner(3));
                    target.IntersectWith(&other);
                    if (target.Count != 2 || live != 4) return 2;
                    target.IntersectWith(&target);
                    if (target.Count != 2 || live != 4) return 3;
                    target.ExceptWith(&other);
                    if (!target.IsEmpty || live != 2) return 4;
                    other.ExceptWith(&other);
                    if (live != 0) return 5;
                }
                return live;
            }
            """, optimization);
}

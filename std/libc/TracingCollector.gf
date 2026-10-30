using std::Collections;
namespace std
{
    namespace collector_detail
    {
        private extern void abort();
    }

    // A non-moving, single-threaded tracing collector.

    public static class TracingCollector : IGarbageCollector
    {
        private struct Slot
        {
            public void* address;
            // Refreshed for each collection; read only during its mark phase.
            public Allocation*? owner;
            public Slot*? next;
            public Slot*? previous;
            public Slot*? hashNext;
        }

        private struct Allocation
        {
            public void* address;
            public nuint bytes;
            public bool marked;
            public bool scanned;
            public Allocation*? next;
        }

        private struct State
        {
            public Slot*? slots;
            public Slot*? freeSlots;
            public nuint cachedSlots;
            public Slot*?* buckets;
            public nuint bucketCount;
            public nuint slotCount;
            public Allocation*? allocations;
            public nuint bytesSinceCollection;
            public bool collecting;
        }

        private static State*? stateStorage;

        private static State* GetState() nothrow
        {
            // Thread identity and its atomic claim still need a low-level primitive.
            // Collector state itself is ordinary gflat static storage.
            bool owner = __llvm_module<bool>("@owner = private global i8* null\n@identity = private thread_local global i8 0\ndefine i1 @enter() {\n%claim = cmpxchg i8** @owner, i8* null, i8* @identity acq_rel acquire\n%previous = extractvalue { i8*, i1 } %claim, 0\n%empty = icmp eq i8* %previous, null\n%same = icmp eq i8* %previous, @identity\n%allowed = or i1 %empty, %same\nret i1 %allowed\n}", "enter");
            if (!owner) collector_detail::abort();
            if (stateStorage == null)
            {
                State* state = (State*)RequiredAllocate((nuint)sizeof(State));
                state.slots = null;
                state.freeSlots = null;
                state.cachedSlots = 0;
                state.bucketCount = 64;
                state.slotCount = 0;
                state.buckets = (Slot*?*)RequiredAllocate(state.bucketCount * (nuint)sizeof(Slot*));
                for (nuint i = 0; i < state.bucketCount; i++) state.buckets[i] = null;
                state.allocations = null;
                state.bytesSinceCollection = 0;
                state.collecting = false;
                stateStorage = state;
            }
            return stateStorage!;
        }

        private static void* RequiredAllocate(nuint bytes) nothrow
        {
            void*? result = global::Memory::Allocate(bytes);
            if (result == null) collector_detail::abort();
            return result!;
        }

        private static bool Contains(Allocation* allocation, void* address) nothrow
        {
            nuint value = (address).reinterpret<nuint>();
            nuint start = (allocation.address).reinterpret<nuint>();
            return value >= start && value - start < allocation.bytes;
        }

        private static Allocation*? Owner(State* state, void* address) nothrow
        {
            Allocation*? allocation = state.allocations;
            while (allocation != null)
            {
                if (Contains(allocation!, address)) return allocation;
                allocation = allocation!.next;
            }
            return null;
        }

        private static bool ValidRange(void* address, nuint bytes) nothrow
        {
            nuint start = (address).reinterpret<nuint>();
            nuint maximum = (nuint)0 - (nuint)1;
            return start != 0 && bytes != 0 && bytes <= maximum - start;
        }

        private static void Register(void* address, nuint bytes)
        {
            State* state = GetState();
            if (state.collecting || !ValidRange(address, bytes))
                throw new Exception("Invalid collector allocation");
            Allocation*? existing = state.allocations;
            while (existing != null)
            {
                // Reject overlapping and duplicate allocations. Explicit Adopt
                // still requires ownership of valid Memory::Allocate storage.
                if (Contains(existing!, address) ||
                    ((existing!.address).reinterpret<nuint>() >= (address).reinterpret<nuint>() &&
                     (existing!.address).reinterpret<nuint>() - (address).reinterpret<nuint>() < bytes))
                    throw new Exception("Allocation is already managed");
                existing = existing!.next;
            }
            Allocation*? allocation = (Allocation*?)global::Memory::Allocate((nuint)sizeof(Allocation));
            if (allocation == null) throw new BadAllocException();
            allocation!.address = address;
            allocation!.bytes = bytes;
            allocation!.marked = false;
            allocation!.scanned = false;
            allocation!.next = state.allocations;
            state.allocations = allocation;
            nuint maximum = (nuint)0 - (nuint)1;
            if (bytes > maximum - state.bytesSinceCollection) state.bytesSinceCollection = maximum;
            else state.bytesSinceCollection += bytes;
        }

        public static void* Allocate(nuint bytes, nuint alignment)
        {
            State* state = GetState();
            if (bytes == 0) bytes = 1;
            if (alignment == 0) throw new BadAllocException();
            nuint threshold = CollectionThreshold();
            if (threshold != 0 && state.bytesSinceCollection >= threshold) Collect();
            void*? storage = global::Memory::Allocate(bytes);
            if (storage == null) { Collect(); storage = global::Memory::Allocate(bytes); }
            if (storage == null) throw new BadAllocException();
            if ((storage).reinterpret<nuint>() % alignment != 0)
            {
                global::Memory::Free(storage!);
                throw new BadAllocException();
            }
            try { Register(storage!, bytes); }
            catch { global::Memory::Free(storage!); throw; }
            byte* data = (byte*)storage;
            for (nuint index = 0; index < bytes; index++) data[index] = 0;
            return storage!;
        }

        // Unsafe ownership transfer: storage must be a complete allocation from
        // Memory::Allocate, not stack storage, interior storage, or a foreign heap.
        // Failure leaves ownership with the caller. Adoption never collects: a raw
        // pointer is not a root until the returned reference is stored.
        public static managed<T, TracingCollector> Adopt<T>(T* storage)
        {
            nuint bytes = (nuint)sizeof(T);
            if (bytes == 0) bytes = 1;
            Register((void*)storage, bytes);
            return (managed<T, TracingCollector>)storage;
        }

        private static nuint SlotBucket(void* address, nuint capacity) nothrow
        {
            nuint hash = (address).reinterpret<nuint>();
            hash = hash ^ (hash >> 7);
            hash = hash ^ (hash >> 17);
            return (hash >> 3) & (capacity - (nuint)1);
        }

        private static void GrowSlots(State* state) nothrow
        {
            nuint maximum = (nuint)0 - (nuint)1;
            if (state.bucketCount > maximum / (nuint)2 / (nuint)sizeof(Slot*)) return;
            nuint capacity = state.bucketCount * (nuint)2;
            Slot*?* buckets = (Slot*?*)RequiredAllocate(capacity * (nuint)sizeof(Slot*));
            for (nuint i = 0; i < capacity; i++) buckets[i] = null;
            Slot*? slot = state.slots;
            while (slot != null)
            {
                nuint bucket = SlotBucket(slot!.address, capacity);
                slot!.hashNext = buckets[bucket];
                buckets[bucket] = slot;
                slot = slot!.next;
            }
            global::Memory::Free((void*)state.buckets);
            state.buckets = buckets;
            state.bucketCount = capacity;
        }

        public static void ReferenceAdded(void* address) nothrow
        {
            State* state = GetState();
            if (!ValidRange(address, (nuint)sizeof(void*))) collector_detail::abort();
            nuint bucket = SlotBucket(address, state.bucketCount);
            Slot*? slot = state.buckets[bucket];
            while (slot != null)
            {
                if (slot!.address == address) return;
                slot = slot!.hashNext;
            }
            if (state.slotCount >= state.bucketCount)
            {
                GrowSlots(state);
                bucket = SlotBucket(address, state.bucketCount);
            }
            Slot* added;
            if (state.freeSlots == null) added = (Slot*)RequiredAllocate((nuint)sizeof(Slot));
            else
            {
                added = state.freeSlots!;
                state.freeSlots = added.next;
                state.cachedSlots--;
            }
            added.address = address;
            added.owner = null;
            added.previous = null;
            added.next = state.slots;
            if (state.slots != null) state.slots!.previous = added;
            state.slots = added;
            added.hashNext = state.buckets[bucket];
            state.buckets[bucket] = added;
            state.slotCount++;
        }

        public static void ReferenceRemoved(void* address) nothrow
            => RemoveReference(GetState(), address);

        private static void RemoveReference(State* state, void* address) nothrow
        {
            nuint bucket = SlotBucket(address, state.bucketCount);
            Slot*? previous = null;
            Slot*? slot = state.buckets[bucket];
            while (slot != null)
            {
                if (slot!.address == address)
                {
                    if (previous == null) state.buckets[bucket] = slot!.hashNext;
                    else previous!.hashNext = slot!.hashNext;
                    if (slot!.previous == null) state.slots = slot!.next;
                    else slot!.previous!.next = slot!.next;
                    if (slot!.next != null) slot!.next!.previous = slot!.previous;
                    state.slotCount--;
                    // Keep a bounded cache for short-lived compiler roots. A
                    // temporary reference should not require malloc/free.
                    if (state.cachedSlots < (nuint)256)
                    {
                        slot!.previous = null;
                        slot!.hashNext = null;
                        slot!.next = state.freeSlots;
                        state.freeSlots = slot;
                        state.cachedSlots++;
                    }
                    else global::Memory::Free((void*)slot);
                    return;
                }
                previous = slot;
                slot = slot!.hashNext;
            }
        }

        private static void Mark(State* state, void* slot) nothrow
        {
            // Slot addresses can be unaligned inside packed or foreign storage.
            void* value = __llvm<void*>("%slot = bitcast i8* %0 to i8**\n%value = load i8*, i8** %slot, align 1\nret i8* %value", slot);
            Allocation*? allocation = Owner(state, value);
            if (allocation != null) allocation!.marked = true;
        }

        private static void RemoveOwnedSlots(State* state, Allocation* allocation) nothrow
        {
            Slot*? slot = state.slots;
            while (slot != null)
            {
                Slot*? next = slot!.next;
                if (Contains(allocation, slot!.address))
                    RemoveReference(state, slot!.address);
                slot = next;
            }
        }

        // A deallocator failure cannot safely resume a partially completed sweep.
        // The normal nothrow boundary uses the application's unhandled hook.
        private static void Release(void* storage) nothrow => global::Memory::Free(storage);

        public static nuint Collect()
        {
            State* state = GetState();
            if (state.collecting) collector_detail::abort();
            state.collecting = true;
            Allocation*? allocation = state.allocations;
            while (allocation != null)
            {
                allocation!.marked = false;
                allocation!.scanned = false;
                allocation = allocation!.next;
            }
            // Adoption can change a registered slot's owner without a new notification.
            Slot*? slot = state.slots;
            while (slot != null)
            {
                slot!.owner = Owner(state, slot!.address);
                slot = slot!.next;
            }
            slot = state.slots;
            while (slot != null)
            {
                if (slot!.owner == null) Mark(state, slot!.address);
                slot = slot!.next;
            }
            bool work = true;
            while (work)
            {
                work = false;
                allocation = state.allocations;
                while (allocation != null)
                {
                    if (allocation!.marked && !allocation!.scanned)
                    {
                        allocation!.scanned = true;
                        slot = state.slots;
                        while (slot != null)
                        {
                            if (slot!.owner == allocation) Mark(state, slot!.address);
                            slot = slot!.next;
                        }
                        work = true;
                    }
                    allocation = allocation!.next;
                }
            }
            Allocation*? garbage = null;
            Allocation*? previous = null;
            allocation = state.allocations;
            while (allocation != null)
            {
                Allocation*? next = allocation!.next;
                if (allocation!.marked) previous = allocation;
                else
                {
                    if (previous == null) state.allocations = next;
                    else previous!.next = next;
                    allocation!.next = garbage;
                    garbage = allocation;
                }
                allocation = next;
            }
            nuint reclaimed = 0;
            while (garbage != null)
            {
                allocation = garbage;
                garbage = garbage!.next;
                RemoveOwnedSlots(state, allocation!);
                Release(allocation!.address);
                global::Memory::Free((void*)allocation);
                reclaimed++;
            }
            state.collecting = false;
            state.bytesSinceCollection = 0;
            return reclaimed;
        }

        // Policy and diagnostics beyond the common collector contract.
        public static weak nuint CollectionThreshold() => 65536;
        public static nuint ManagedCount
        {
            get
            {
                nuint count = 0;
                Allocation*? allocation = GetState().allocations;
                while (allocation != null) { count++; allocation = allocation!.next; }
                return count;
            }
        }
        public static nuint ManagedBytes
        {
            get
            {
                nuint bytes = 0;
                Allocation*? allocation = GetState().allocations;
                while (allocation != null) { bytes += allocation!.bytes; allocation = allocation!.next; }
                return bytes;
            }
        }
    }
}

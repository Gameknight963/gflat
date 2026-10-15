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
            public Slot*? next;
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
            public Allocation*? allocations;
            public nuint bytesSinceCollection;
            public bool collecting;
        }

        // A private global State* slot. The state itself is allocated at sizeof(State),
        // so this bridge depends on no record offsets or hardcoded record layout.
        private static State* GetState() nothrow
        {
            bool owner = __llvm_module<bool>("@owner = private global i8* null\n@identity = private thread_local global i8 0\ndefine i1 @enter() {\n%claim = cmpxchg i8** @owner, i8* null, i8* @identity acq_rel acquire\n%previous = extractvalue { i8*, i1 } %claim, 0\n%empty = icmp eq i8* %previous, null\n%same = icmp eq i8* %previous, @identity\n%allowed = or i1 %empty, %same\nret i1 %allowed\n}", "enter");
            if (!owner) collector_detail::abort();
            nuint* slot = (nuint*)__llvm_module<void*>("@state = private global i8* null\ndefine i8* @address() {\n%result = bitcast i8** @state to i8*\nret i8* %result\n}", "address");
            if (*slot == (nuint)0)
            {
                State* state = (State*)RequiredAllocate((nuint)sizeof(State));
                state.slots = null;
                state.allocations = null;
                state.bytesSinceCollection = 0;
                state.collecting = false;
                *slot = (nuint)state;
            }
            return (State*)*slot;
        }

        private static void* RequiredAllocate(nuint bytes) nothrow
        {
            void*? result = Memory::Allocate(bytes);
            if (result == null) collector_detail::abort();
            return result!;
        }

        private static bool Contains(Allocation* allocation, void* address) nothrow
        {
            nuint value = (nuint)address;
            nuint start = (nuint)allocation.address;
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
            nuint start = (nuint)address;
            nuint maximum = (nuint)0 - (nuint)1;
            return start != (nuint)0 && bytes != (nuint)0 && bytes <= maximum - start;
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
                    ((nuint)existing!.address >= (nuint)address &&
                     (nuint)existing!.address - (nuint)address < bytes))
                    throw new Exception("Allocation is already managed");
                existing = existing!.next;
            }
            Allocation*? allocation = (Allocation*?)Memory::Allocate((nuint)sizeof(Allocation));
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
            if (bytes == (nuint)0) bytes = 1;
            if (alignment == (nuint)0) throw new BadAllocException();
            nuint threshold = CollectionThreshold();
            if (threshold != (nuint)0 && state.bytesSinceCollection >= threshold) Collect();
            void*? storage = Memory::Allocate(bytes);
            if (storage == null) { Collect(); storage = Memory::Allocate(bytes); }
            if (storage == null) throw new BadAllocException();
            if ((nuint)storage % alignment != (nuint)0)
            {
                Memory::Free(storage!);
                throw new BadAllocException();
            }
            try { Register(storage!, bytes); }
            catch { Memory::Free(storage!); throw; }
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
            if (bytes == (nuint)0) bytes = 1;
            Register((void*)storage, bytes);
            return (managed<T, TracingCollector>)storage;
        }

        public static void ReferenceAdded(void* address) nothrow
        {
            State* state = GetState();
            if (!ValidRange(address, (nuint)sizeof(void*))) collector_detail::abort();
            Slot*? slot = state.slots;
            while (slot != null)
            {
                if (slot!.address == address) return;
                slot = slot!.next;
            }
            Slot* added = (Slot*)RequiredAllocate((nuint)sizeof(Slot));
            added.address = address;
            added.next = state.slots;
            state.slots = added;
        }

        public static void ReferenceRemoved(void* address) nothrow
        {
            State* state = GetState();
            Slot*? previous = null;
            Slot*? slot = state.slots;
            while (slot != null)
            {
                if (slot!.address == address)
                {
                    if (previous == null) state.slots = slot!.next;
                    else previous!.next = slot!.next;
                    Memory::Free((void*)slot);
                    return;
                }
                previous = slot;
                slot = slot!.next;
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
            Slot*? previous = null;
            Slot*? slot = state.slots;
            while (slot != null)
            {
                Slot*? next = slot!.next;
                if (Contains(allocation, slot!.address))
                {
                    if (previous == null) state.slots = next;
                    else previous!.next = next;
                    Memory::Free((void*)slot);
                }
                else previous = slot;
                slot = next;
            }
        }

        // A deallocator failure cannot safely resume a partially completed sweep.
        // The normal nothrow boundary uses the application's unhandled hook.
        private static void Release(void* storage) nothrow => Memory::Free(storage);

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
            Slot*? slot = state.slots;
            while (slot != null)
            {
                if (Owner(state, slot!.address) == null) Mark(state, slot!.address);
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
                            if (Owner(state, slot!.address) == allocation) Mark(state, slot!.address);
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
                Memory::Free((void*)allocation);
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
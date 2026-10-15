// Single-threaded, non-moving mark/sweep. New code reports initialized managed
// reference slots. Slots outside managed allocations are roots; slots inside a
// managed allocation are edges only while that owner is reachable. Ownership is
// looked up at collection time so adopting a native object changes its fields
// from roots to edges without requiring the compiler to know that transition.
// Infrastructure hooks never collect. Allocation/adoption library code chooses
// collection points; release callbacks must not collect or mutate managed graphs.
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>

namespace {
struct ReferenceSlot { void* address; ReferenceSlot* next; };
struct Allocation {
    void* address;
    std::size_t bytes;
    bool marked;
    bool scanned;
    Allocation* next;
};
ReferenceSlot* reference_slots = nullptr;
Allocation* native_allocations = nullptr;
Allocation* managed_allocations = nullptr;
bool collecting = false;
std::size_t bytes_since_collection = 0;
std::atomic<void*> owner{nullptr};
thread_local char thread_identity;

void enter() noexcept {
    void* identity = &thread_identity;
    void* current = owner.load(std::memory_order_acquire);
    if (current == identity) return;
    if (current) std::abort();
    void* expected = nullptr;
    if (!owner.compare_exchange_strong(expected, identity, std::memory_order_acq_rel) && expected != identity)
        std::abort();
}
bool valid_range(void* address, std::size_t bytes) noexcept {
    auto start = reinterpret_cast<std::uintptr_t>(address);
    return address && bytes && bytes <= UINTPTR_MAX - start;
}
void* required_allocate(std::size_t bytes) noexcept {
    void* value = std::malloc(bytes);
    if (!value) std::abort();
    return value;
}
Allocation** find_exact(Allocation*& head, void* address) noexcept {
    auto** slot = &head;
    while (*slot && (*slot)->address != address) slot = &(*slot)->next;
    return slot;
}
bool contains(const Allocation* allocation, const void* address) noexcept {
    const auto value = reinterpret_cast<std::uintptr_t>(address);
    const auto base = reinterpret_cast<std::uintptr_t>(allocation->address);
    return value >= base && value - base < allocation->bytes;
}
Allocation* managed_owner(void* address) noexcept {
    for (auto* allocation = managed_allocations; allocation; allocation = allocation->next)
        if (contains(allocation, address)) return allocation;
    return nullptr;
}
void remove_slots(void* address, std::size_t bytes) noexcept {
    const auto base = reinterpret_cast<std::uintptr_t>(address);
    auto** slot = &reference_slots;
    while (*slot) {
        auto value = reinterpret_cast<std::uintptr_t>((*slot)->address);
        if (value >= base && value - base < bytes) {
            auto* removed = *slot;
            *slot = removed->next;
            std::free(removed);
        } else slot = &(*slot)->next;
    }
}
void mark_candidate(std::uintptr_t candidate) noexcept {
    for (auto* allocation = managed_allocations; allocation; allocation = allocation->next) {
        auto base = reinterpret_cast<std::uintptr_t>(allocation->address);
        if (candidate >= base && candidate - base < allocation->bytes) {
            allocation->marked = true;
            return;
        }
    }
}
void scan_slot(void* address) noexcept {
    std::uintptr_t candidate;
    std::memcpy(&candidate, address, sizeof(candidate));
    mark_candidate(candidate);
}
}

extern "C" {
void __gflat_gc_reference_added(void* address) noexcept {
    enter();
    if (!valid_range(address, sizeof(void*))) std::abort();
    for (auto* slot = reference_slots; slot; slot = slot->next)
        if (slot->address == address) return;
    auto* slot = static_cast<ReferenceSlot*>(required_allocate(sizeof(ReferenceSlot)));
    *slot = {address, reference_slots};
    reference_slots = slot;
}
void __gflat_gc_reference_removed(void* address) noexcept {
    enter();
    auto** slot = &reference_slots;
    while (*slot && (*slot)->address != address) slot = &(*slot)->next;
    if (*slot) {
        auto* removed = *slot;
        *slot = removed->next;
        std::free(removed);
    }
}
bool __gflat_gc_track_native(void* address, std::size_t bytes) noexcept {
    enter();
    // malloc(0) and failed allocations contain no scannable storage.
    if (!address || !bytes) return true;
    if (collecting || !valid_range(address, bytes)) return false;
    if (*find_exact(managed_allocations, address)) return false;
    auto** existing = find_exact(native_allocations, address);
    if (*existing) { (*existing)->bytes = bytes; return true; }
    auto* allocation = static_cast<Allocation*>(std::malloc(sizeof(Allocation)));
    if (!allocation) return false;
    *allocation = {address, bytes, false, false, native_allocations};
    native_allocations = allocation;
    return true;
}
void __gflat_gc_forget_native(void* address) noexcept {
    enter();
    const auto candidate = reinterpret_cast<std::uintptr_t>(address);
    for (auto* allocation = managed_allocations; allocation; allocation = allocation->next) {
        const auto base = reinterpret_cast<std::uintptr_t>(allocation->address);
        // This hook runs before the selected deallocator. Collector sweeping
        // detaches garbage first, so its legitimate frees do not trip this guard.
        if (candidate >= base && candidate - base < allocation->bytes) std::abort();
    }
    // A null pointer or untracked foreign allocation needs no registration work.
    auto** slot = find_exact(native_allocations, address);
    if (*slot) {
        Allocation* allocation = *slot;
        *slot = allocation->next;
        remove_slots(allocation->address, allocation->bytes);
        std::free(allocation);
    }
}
bool __gflat_gc_adopt(void* address, std::size_t bytes) noexcept {
    enter();
    if (collecting || !valid_range(address, bytes)) return false;
    auto** slot = find_exact(native_allocations, address);
    // Only base pointers from the registered allocator are eligible. Adoption
    // cannot manufacture ownership of stack storage or an interior pointer.
    if (!*slot || bytes > (*slot)->bytes) return false;
    Allocation* allocation = *slot;
    *slot = allocation->next;
    // Scan/reclaim the entire underlying allocation, not just the requested T.
    allocation->next = managed_allocations;
    managed_allocations = allocation;
    bytes_since_collection = allocation->bytes > SIZE_MAX - bytes_since_collection
        ? SIZE_MAX : bytes_since_collection + allocation->bytes;
    return true;
}
void* __gflat_gc_adopt_reference(void* address, std::size_t bytes) noexcept {
    return __gflat_gc_adopt(address, bytes) ? address : nullptr;
}
bool __gflat_gc_prepare_allocation(void* address, std::size_t bytes) noexcept {
    enter();
    if (!__gflat_gc_adopt(address, bytes)) return false;
    // No reference slot is live until the compiler reports its initialized store.
    std::memset(address, 0, bytes);
    return true;
}
bool __gflat_gc_should_collect(std::size_t threshold) noexcept {
    enter();
    return threshold != 0 && bytes_since_collection >= threshold;
}
std::size_t __gflat_gc_collect(void (*release)(void*)) noexcept {
    enter();
    if (collecting || !release) std::abort();
    collecting = true;
    for (auto* allocation = managed_allocations; allocation; allocation = allocation->next)
        allocation->marked = allocation->scanned = false;
    for (auto* root = reference_slots; root; root = root->next)
        if (!managed_owner(root->address)) scan_slot(root->address);
    bool work;
    do {
        work = false;
        for (auto* allocation = managed_allocations; allocation; allocation = allocation->next)
            if (allocation->marked && !allocation->scanned) {
                allocation->scanned = true;
                for (auto* root = reference_slots; root; root = root->next)
                    if (managed_owner(root->address) == allocation) scan_slot(root->address);
                work = true;
            }
    } while (work);
    // Detach all garbage before invoking callbacks so no dead allocation remains
    // discoverable by the selected deallocator's ownership validation.
    Allocation* garbage = nullptr;
    auto** slot = &managed_allocations;
    while (*slot) {
        Allocation* allocation = *slot;
        if (allocation->marked) slot = &allocation->next;
        else {
            *slot = allocation->next;
            allocation->next = garbage;
            garbage = allocation;
        }
    }
    std::size_t reclaimed = 0;
    while (garbage) {
        Allocation* allocation = garbage;
        garbage = allocation->next;
        remove_slots(allocation->address, allocation->bytes);
        release(allocation->address); // Throwing across this noexcept ABI terminates.
        std::free(allocation);
        ++reclaimed;
    }
    collecting = false;
    bytes_since_collection = 0;
    return reclaimed;
}
std::size_t __gflat_gc_managed_count() noexcept {
    enter();
    std::size_t result = 0;
    for (auto* allocation = managed_allocations; allocation; allocation = allocation->next) ++result;
    return result;
}
std::size_t __gflat_gc_managed_bytes() noexcept {
    enter();
    std::size_t result = 0;
    for (auto* allocation = managed_allocations; allocation; allocation = allocation->next) result += allocation->bytes;
    return result;
}
}

// First collector: single-threaded, non-moving, conservative mark/sweep.
// Managed references remain addresses. The compiler registers live storage ranges;
// native Memory allocations are roots until freed or transferred by Adopt.
// Adoption can request collection at its caller's configured budget. This ABI
// deliberately separates roots from allocation
// ownership so precise descriptors/relocation can replace scanning later.
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>

namespace {
struct Range { void* address; std::size_t bytes; Range* next; };
struct Frame { Range* roots; Frame* next; };
struct Allocation {
    void* address;
    std::size_t bytes;
    bool marked;
    bool scanned;
    Allocation* next;
};
Frame* frames = nullptr;
Range* globals = nullptr;
Allocation* native_allocations = nullptr;
Allocation* managed_allocations = nullptr;
bool collecting = false;
std::size_t bytes_since_collection = 0;
std::atomic<void*> owner{nullptr};
thread_local char thread_identity;

void enter() noexcept {
    void* identity = &thread_identity;
    void* expected = nullptr;
    owner.compare_exchange_strong(expected, identity, std::memory_order_acq_rel);
    if (owner.load(std::memory_order_acquire) != identity) std::abort();
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
Frame* checked_frame(void* address) noexcept {
    for (auto* frame = frames; frame; frame = frame->next)
        if (frame == address) return frame;
    std::abort();
}
void add_range(Range*& roots, void* address, std::size_t bytes) noexcept {
    if (!bytes) return;
    if (!valid_range(address, bytes)) std::abort();
    for (auto* range = roots; range; range = range->next)
        if (range->address == address) { range->bytes = bytes; return; }
    auto* range = static_cast<Range*>(required_allocate(sizeof(Range)));
    *range = {address, bytes, roots};
    roots = range;
}
Allocation** find_exact(Allocation*& head, void* address) noexcept {
    auto** slot = &head;
    while (*slot && (*slot)->address != address) slot = &(*slot)->next;
    return slot;
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
void scan(void* address, std::size_t bytes) noexcept {
    constexpr auto width = sizeof(std::uintptr_t);
    const auto base = reinterpret_cast<std::uintptr_t>(address);
    // All language pointer fields have natural pointer alignment. Registered
    // ranges may begin at a smaller-alignment field; skip to the next full word.
    const auto offset = (width - base % width) % width;
    if (offset > bytes || bytes - offset < width) return;
    auto* data = static_cast<unsigned char*>(address);
    for (std::size_t at = offset; at <= bytes - width; at += width) {
        std::uintptr_t candidate;
        std::memcpy(&candidate, data + at, width);
        mark_candidate(candidate);
    }
}
}

extern "C" {
void* __gflat_gc_frame_enter() noexcept {
    enter();
    auto* frame = static_cast<Frame*>(required_allocate(sizeof(Frame)));
    *frame = {nullptr, frames};
    frames = frame;
    return frame;
}
void __gflat_gc_frame_add(void* frame, void* address, std::size_t bytes) noexcept {
    enter();
    add_range(checked_frame(frame)->roots, address, bytes);
}
void __gflat_gc_frame_leave(void* address) noexcept {
    enter();
    auto** slot = &frames;
    while (*slot && *slot != address) slot = &(*slot)->next;
    if (!*slot) std::abort();
    Frame* frame = *slot;
    *slot = frame->next;
    while (frame->roots) {
        Range* root = frame->roots;
        frame->roots = root->next;
        std::free(root);
    }
    std::free(frame);
}
void __gflat_gc_global_root(void* address, std::size_t bytes) noexcept {
    enter();
    // Global storage must remain valid for the process lifetime.
    add_range(globals, address, bytes);
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
    for (auto* frame = frames; frame; frame = frame->next)
        for (auto* root = frame->roots; root; root = root->next) scan(root->address, root->bytes);
    for (auto* root = globals; root; root = root->next) scan(root->address, root->bytes);
    for (auto* allocation = native_allocations; allocation; allocation = allocation->next)
        scan(allocation->address, allocation->bytes);
    bool work;
    do {
        work = false;
        for (auto* allocation = managed_allocations; allocation; allocation = allocation->next)
            if (allocation->marked && !allocation->scanned) {
                allocation->scanned = true;
                scan(allocation->address, allocation->bytes);
                work = true;
            }
    } while (work);
    // Detach all garbage before invoking callbacks. Free hooks may register
    // temporary compiler root frames; no dead allocation remains discoverable.
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

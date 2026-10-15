// Native exception transport. Gflat object layout remains private to its compiler.
#include <exception>
#include <cstdlib>
#include <new>
#include <atomic>
#include <cstddef>
#include <limits>
#include <memory>

#ifndef GFLAT_EXCEPTION_RESERVE_BYTES
#define GFLAT_EXCEPTION_RESERVE_BYTES 65536
#endif
static_assert(GFLAT_EXCEPTION_RESERVE_BYTES >= 0, "Negative exception reserve");

namespace {
// No heap allocations are needed to manage the reserve. Blocks split on
// allocation and coalesce on release; the lock also protects cross-thread release.
constexpr std::size_t reserve_size = GFLAT_EXCEPTION_RESERVE_BYTES;
struct alignas(std::max_align_t) Block {
    std::size_t size;
    Block* next;
    bool free;
};
alignas(std::max_align_t) unsigned char reserve[reserve_size ? reserve_size : 1];
std::atomic_flag reserve_lock = ATOMIC_FLAG_INIT;
Block* head = nullptr;
bool initialized = false;
struct Lock {
    Lock() noexcept { while (reserve_lock.test_and_set(std::memory_order_acquire)) {} }
    ~Lock() { reserve_lock.clear(std::memory_order_release); }
};
bool reserved(void* value) noexcept {
    auto address = reinterpret_cast<std::size_t>(value);
    auto start = reinterpret_cast<std::size_t>(reserve);
    return address >= start && address - start < reserve_size;
}
void* reserve_allocate(std::size_t size) noexcept {
    constexpr auto alignment = alignof(std::max_align_t);
    if (size > std::numeric_limits<std::size_t>::max() - alignment + 1) return nullptr;
    size = (size + alignment - 1) / alignment * alignment;
    if (!size) size = alignment;
    Lock lock;
    if (!initialized) {
        initialized = true;
        if (reserve_size >= sizeof(Block) + alignment)
            head = new (reserve) Block{(reserve_size - sizeof(Block)) / alignment * alignment, nullptr, true};
    }
    for (auto* block = head; block; block = block->next) {
        if (!block->free || block->size < size) continue;
        if (block->size - size >= sizeof(Block) + alignment) {
            auto* next = new (reinterpret_cast<unsigned char*>(block + 1) + size)
                Block{block->size - size - sizeof(Block), block->next, true};
            block->next = next;
            block->size = size;
        }
        block->free = false;
        return block + 1;
    }
    return nullptr;
}
void reserve_free(void* value) noexcept {
    Lock lock;
    static_cast<Block*>(value)[-1].free = true;
    for (auto* block = head; block && block->next;) {
        if (block->free && block->next->free) {
            block->size += sizeof(Block) + block->next->size;
            block->next = block->next->next;
        } else block = block->next;
    }
}
void* allocate(std::size_t size) noexcept {
#ifndef GFLAT_EH_TEST_FORCE_RESERVE
    if (auto* value = std::malloc(size)) return value;
#endif
    if (auto* value = reserve_allocate(size)) return value;
    std::terminate();
}
void release(void* value) noexcept {
    if (reserved(value)) reserve_free(value);
    else std::free(value);
}
template<class T> struct RuntimeAllocator {
    using value_type = T;
    RuntimeAllocator() noexcept = default;
    template<class U> RuntimeAllocator(const RuntimeAllocator<U>&) noexcept {}
    T* allocate(std::size_t count) {
        if (count > std::numeric_limits<std::size_t>::max() / sizeof(T)) std::terminate();
        return static_cast<T*>(::allocate(count * sizeof(T)));
    }
    void deallocate(T* value, std::size_t) noexcept { release(value); }
    template<class U> bool operator==(const RuntimeAllocator<U>&) const noexcept { return true; }
    template<class U> bool operator!=(const RuntimeAllocator<U>&) const noexcept { return false; }
};
struct Exception final {
    // MSVC may copy the native exception. Copies share the gflat payload.
    std::shared_ptr<void> object;
    Exception(void* value, void (*cleanup)(void*))
        : object(value, cleanup, RuntimeAllocator<unsigned char>{}) {}
};
struct Handle { std::exception_ptr exception; };
void release_handle(Handle* handle) noexcept {
    handle->~Handle();
    release(handle);
}
}
extern "C" {
// Payload allocation respects the user's allocator before using the reserve.
void* __gflat_eh_allocate_exception(std::size_t size, void* (*allocator)(std::size_t)) {
    if (auto* value = allocator(size)) return value;
    if (auto* value = reserve_allocate(size)) return value;
    std::terminate();
}
void __gflat_eh_free_exception(void* value, void (*deallocator)(void*)) {
    if (reserved(value)) reserve_free(value);
    else deallocator(value);
}
[[noreturn]] void __gflat_eh_throw(void* object, void (*destroy)(void*)) {
    throw Exception(object, destroy);
}
// Called inside a native catch; retain the exception beyond the native catch block.
void* __gflat_eh_capture() noexcept {
    auto* handle = new (allocate(sizeof(Handle))) Handle{std::current_exception()};
    if (!handle->exception) std::terminate();
    return handle;
}
void* __gflat_eh_object(void* value) noexcept {
    try { std::rethrow_exception(static_cast<Handle*>(value)->exception); }
    catch (const Exception& error) { return error.object.get(); }
    catch (...) { return nullptr; }
}
void __gflat_eh_release(void* value) noexcept {
    if (value) release_handle(static_cast<Handle*>(value));
}
[[noreturn]] void __gflat_eh_rethrow(void* value) {
    std::exception_ptr exception = static_cast<Handle*>(value)->exception;
    release_handle(static_cast<Handle*>(value));
    std::rethrow_exception(exception);
}
[[noreturn]] void __gflat_eh_terminate(void* value, void (*hook)(void*)) noexcept {
    void* object = value ? __gflat_eh_object(value) : nullptr;
    try { if (hook) hook(object); } catch (...) {}
    std::exit(1);
}
}

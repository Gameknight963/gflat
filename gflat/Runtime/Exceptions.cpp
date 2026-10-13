// Native exception transport. Gflat object layout remains private to its compiler.
#include <exception>
#include <cstdlib>
#include <new>
#include <memory>

namespace {
struct Exception final {
    // MSVC's current_exception may copy the exception object. Copies share
    // ownership of the gflat payload rather than duplicating its destruction.
    std::shared_ptr<void> object;
    Exception(void* value, void (*cleanup)(void*)) : object(value, cleanup) {}
};
struct Handle { std::exception_ptr exception; };
}
extern "C" {
[[noreturn]] void __gflat_eh_throw(void* object, void (*destroy)(void*)) {
    throw Exception(object, destroy);
}
// Called inside a native catch; retain the exception beyond the native catch block.
void* __gflat_eh_capture() noexcept {
    auto* handle = new (std::nothrow) Handle{std::current_exception()};
    if (!handle || !handle->exception) std::terminate();
    return handle;
}
void* __gflat_eh_object(void* value) noexcept {
    try { std::rethrow_exception(static_cast<Handle*>(value)->exception); }
    catch (const Exception& error) { return error.object.get(); }
    catch (...) { return nullptr; } // Foreign exceptions can propagate without translating their types.
}
void __gflat_eh_release(void* value) noexcept { delete static_cast<Handle*>(value); }
[[noreturn]] void __gflat_eh_rethrow(void* value) {
    std::exception_ptr exception = static_cast<Handle*>(value)->exception;
    delete static_cast<Handle*>(value);
    std::rethrow_exception(exception);
}
[[noreturn]] void __gflat_eh_terminate(void* value, void (*hook)(void*)) noexcept {
    void* object = value ? __gflat_eh_object(value) : nullptr;
    try { if (hook) hook(object); } catch (...) {}
    // Execution cannot resume if a replacement hook returns or throws.
    std::exit(1);
}
}

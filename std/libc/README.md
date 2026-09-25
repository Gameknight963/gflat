# libc-dependent facilities

`Formatting.gf` supplies `std::ToString(float)` and `std::ToString(double)` through
`snprintf`, using 9 and 17 significant digits respectively. Formatting follows the
application's current C locale. Results use the replaceable `Allocator` API and
must be released through `Allocator::Free` (interpolation does this automatically).

It depends on the text allocation helper in `../core/Formatting.gf`. To compile
only portable facilities, supply just the core sources. `std.gfproj` currently
includes both directories; separate project selection can be added later.

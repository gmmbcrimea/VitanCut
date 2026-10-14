#pragma once
#include <cstdint>

namespace installer_ui
{
constexpr int ContentWidth = 518;
constexpr int ContentHeight = 492;

constexpr int Scale(int value, unsigned dpi)
{
    return static_cast<int>((static_cast<std::int64_t>(value) * dpi + 48) / 96);
}

constexpr int ClampScroll(int requested, int extent, int viewport)
{
    const int maximum = extent > viewport ? extent - viewport : 0;
    return requested < 0 ? 0 : requested > maximum ? maximum : requested;
}
}

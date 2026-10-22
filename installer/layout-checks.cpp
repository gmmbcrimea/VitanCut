#include "ui-layout.h"
#include "localization.h"
#include <cassert>
#include <iostream>

int main()
{
    for (const auto& entry : installer_l10n::Entries)
        for (int language = 1; language <= 4; ++language)
            assert(std::wstring(installer_l10n::Translate(entry.source, language)) != entry.source);
    assert(std::wstring(installer_l10n::Translate(L"Установить", 1)) == L"Install");
    assert(std::wstring(installer_l10n::Translate(L"Готово", 4)) == L"完成");
    using namespace installer_ui;
    assert(Scale(17, 96) == 17);
    assert(Scale(17, 144) == 26);
    assert(Scale(17, 192) == 34);
    assert(Scale(14, 120) == 18);
    assert(Scale(34, 96) == 34);
    assert(Scale(34, 120) == 43);
    assert(Scale(34, 144) == 51);
    assert(Scale(34, 192) == 68);
    assert(ClampScroll(-10, 500, 300) == 0);
    assert(ClampScroll(900, 500, 300) == 200);
    assert(ClampScroll(80, 500, 600) == 0);
    assert(ClampScroll(100, 500, 300) == 100);
    for (const unsigned dpi : { 96u, 120u, 144u, 192u })
    {
        assert(Scale(490, dpi) <= Scale(ContentWidth, dpi));
        assert(Scale(413 + 56, dpi) <= Scale(ContentHeight, dpi));
        assert(Scale(398 + 44, dpi) <= Scale(ContentHeight, dpi));
        assert(Scale(462 + 24, dpi) <= Scale(ContentHeight, dpi));
    }
    std::cout << "Installer layout checks passed: DPI100/125/150/200, content bounds and scroll clamping.\n";
}

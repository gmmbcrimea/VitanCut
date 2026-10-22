#include <windows.h>
#include <commctrl.h>
#include <uxtheme.h>
#include <dwmapi.h>
#include <vsstyle.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <winhttp.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <thread>
#include <vector>
#include <stdexcept>
#include <map>
#include "resource.h"
#include "ui-layout.h"
#include "localization.h"

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "uxtheme.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "winhttp.lib")
#pragma comment(lib, "dwmapi.lib")

namespace fs = std::filesystem;

constexpr wchar_t WindowClass[] = L"VitanCutInstallerWindow";
constexpr wchar_t ReleasesUrl[] = L"https://api.github.com/repos/gmmbcrimea/VitanCut/releases?per_page=100";
constexpr UINT WM_INSTALL_PROGRESS = WM_APP + 1;
constexpr UINT WM_INSTALL_STATUS = WM_APP + 2;
constexpr UINT WM_INSTALL_FINISHED = WM_APP + 3;
constexpr wchar_t UninstallRegistryPath[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\VitanCut";

HWND pathBox, desktopBox, startMenuBox, taskbarBox, installButton, progressBar, statusLabel;
HFONT fontRegular, fontSmall, fontTitle, fontBold;
bool installationComplete = false;
bool uiPreview = false;
bool uiPreviewFailure = false;
int previewProgress = 0;
constexpr UINT_PTR PreviewTimer = 9001;
UINT uiDpi = 96;
int scrollX = 0, scrollY = 0;
bool controlsReady = false, layingOut = false;
struct ControlLayout { HWND window; int x, y, width, height; HFONT* font; };
std::vector<ControlLayout> controlLayouts;
HBRUSH windowBrush = CreateSolidBrush(RGB(32, 32, 32));
HBRUSH inputBrush = CreateSolidBrush(RGB(45, 45, 45));
bool windowMaterial = false;
constexpr int LanguageControlId = 1010;
int uiLanguage = 0;
std::map<HWND, std::wstring> localizedControls;

void SetLocalizedText(HWND control, const std::wstring& source)
{
    localizedControls[control] = source;
    SetWindowTextW(control, installer_l10n::Translate(source, uiLanguage));
    InvalidateRect(control, nullptr, TRUE);
}

void RefreshLanguage(HWND window)
{
    for (const auto& item : localizedControls)
        SetWindowTextW(item.first, installer_l10n::Translate(item.second, uiLanguage));
    SetWindowTextW(window, installer_l10n::Translate(uiPreview ? L"VitanCut - проверка интерфейса" : L"Установка VitanCut", uiLanguage));
    RedrawWindow(window, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN);
}

void ApplyWindowMaterial(HWND window)
{
    HIGHCONTRASTW contrast{ sizeof(contrast) };
    SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(contrast), &contrast, 0);
    DWORD transparency = 1, bytes = sizeof(transparency);
    RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
        L"EnableTransparency", RRF_RT_REG_DWORD, nullptr, &transparency, &bytes);
    const bool allowMaterial = !(contrast.dwFlags & HCF_HIGHCONTRASTON) && transparency != 0;
    OSVERSIONINFOW version{ sizeof(version) };
    using ReadVersion = LONG(WINAPI*)(OSVERSIONINFOW*);
    const auto readVersion = reinterpret_cast<ReadVersion>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion"));
    if (readVersion) readVersion(&version);
    const BOOL dark = TRUE;
    DwmSetWindowAttribute(window, DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(dark));
    windowMaterial = false;
    if (version.dwBuildNumber >= 22621)
    {
        const DWM_SYSTEMBACKDROP_TYPE material = allowMaterial ? DWMSBT_MAINWINDOW : DWMSBT_NONE;
        const HRESULT result = DwmSetWindowAttribute(window, DWMWA_SYSTEMBACKDROP_TYPE, &material, sizeof(material));
        windowMaterial = allowMaterial && SUCCEEDED(result);
    }
    else
    {
        // Windows10 has no documented DWM Acrylic attribute; resolve its optional native entry point at runtime.
        struct AccentPolicy { int state; int flags; DWORD tint; int animation; };
        struct CompositionData { int attribute; AccentPolicy* policy; SIZE_T size; };
        using SetComposition = BOOL(WINAPI*)(HWND, CompositionData*);
        const auto setComposition = reinterpret_cast<SetComposition>(GetProcAddress(GetModuleHandleW(L"user32.dll"), "SetWindowCompositionAttribute"));
        AccentPolicy policy{ allowMaterial && version.dwBuildNumber >= 17134 ? 4 : 0, 0, 0xD92D261F, 0 };
        CompositionData data{ 19, &policy, sizeof(policy) };
        windowMaterial = setComposition && setComposition(window, &data) && policy.state == 4;
    }
    const MARGINS margins = windowMaterial ? MARGINS{ -1, -1, -1, -1 } : MARGINS{};
    DwmExtendFrameIntoClientArea(window, &margins);
    SetClassLongPtrW(window, GCLP_HBRBACKGROUND, reinterpret_cast<LONG_PTR>(windowMaterial ? GetStockObject(BLACK_BRUSH) : windowBrush));
    if (controlsReady) RedrawWindow(window, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN);
}

struct InstallResult { bool success; std::wstring message; };

int Scale(int value) { return installer_ui::Scale(value, uiDpi); }

void ApplyControlLayout(HWND window)
{
    if (!controlsReady || layingOut) return;
    layingOut = true;
    RECT client{};
    // Each scrollbar can change the other axis's viewport.
    for (int pass = 0; pass < 3; ++pass)
    {
        GetClientRect(window, &client);
        scrollX = installer_ui::ClampScroll(scrollX, Scale(installer_ui::ContentWidth), client.right);
        scrollY = installer_ui::ClampScroll(scrollY, Scale(installer_ui::ContentHeight), client.bottom);
        SCROLLINFO horizontal{ sizeof(horizontal), SIF_RANGE | SIF_PAGE | SIF_POS, 0, Scale(installer_ui::ContentWidth) - 1, static_cast<UINT>(client.right), scrollX };
        SCROLLINFO vertical{ sizeof(vertical), SIF_RANGE | SIF_PAGE | SIF_POS, 0, Scale(installer_ui::ContentHeight) - 1, static_cast<UINT>(client.bottom), scrollY };
        SetScrollInfo(window, SB_HORZ, &horizontal, TRUE);
        SetScrollInfo(window, SB_VERT, &vertical, TRUE);
    }
    for (const auto& item : controlLayouts)
    {
        MoveWindow(item.window, Scale(item.x) - scrollX, Scale(item.y) - scrollY, Scale(item.width), Scale(item.height), FALSE);
        SendMessageW(item.window, WM_SETFONT, reinterpret_cast<WPARAM>(*item.font), FALSE);
    }
    layingOut = false;
    RedrawWindow(window, nullptr, nullptr, RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_ERASE);
}

void EnsureControlVisible(HWND window, HWND control)
{
    RECT client{}; GetClientRect(window, &client);
    for (const auto& item : controlLayouts)
    {
        if (item.window != control) continue;
        const int left = Scale(item.x), top = Scale(item.y);
        if (left < scrollX) scrollX = left;
        else if (left + Scale(item.width) > scrollX + client.right) scrollX = left + Scale(item.width) - client.right;
        if (top < scrollY) scrollY = top;
        else if (top + Scale(item.height) > scrollY + client.bottom) scrollY = top + Scale(item.height) - client.bottom;
        ApplyControlLayout(window);
        break;
    }
}

LRESULT CALLBACK ControlInput(HWND control, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR)
{
    const int controlId = GetDlgCtrlID(control);
    if (controlId == IDC_BROWSE || controlId == IDC_INSTALL)
    {
        if (message == WM_MOUSEMOVE && !GetPropW(control, L"VitanCut.Hover"))
        {
            SetPropW(control, L"VitanCut.Hover", reinterpret_cast<HANDLE>(1));
            TRACKMOUSEEVENT track{ sizeof(track), TME_LEAVE, control, 0 }; TrackMouseEvent(&track);
            InvalidateRect(control, nullptr, FALSE);
        }
        if (message == WM_MOUSELEAVE)
        {
            RemovePropW(control, L"VitanCut.Hover"); InvalidateRect(control, nullptr, FALSE);
        }
    }
    if (message == WM_SETFOCUS) EnsureControlVisible(GetParent(control), control);
    if ((message == WM_SETFOCUS || message == WM_KILLFOCUS) && controlId == IDC_INSTALL_PATH)
        InvalidateRect(GetParent(control), nullptr, FALSE);
    if (message == WM_MOUSEWHEEL)
        return SendMessageW(GetParent(control), message, wParam, lParam);
    if (message == WM_NCDESTROY) RemoveWindowSubclass(control, ControlInput, id);
    return DefSubclassProc(control, message, wParam, lParam);
}

HWND TrackControl(HWND control, int x, int y, int width, int height, HFONT* font)
{
    controlLayouts.push_back({ control, x, y, width, height, font });
    SendMessageW(control, WM_SETFONT, reinterpret_cast<WPARAM>(*font), FALSE);
    SetWindowSubclass(control, ControlInput, 1, 0);
    return control;
}

void RecreateFonts(HWND window)
{
    const HFONT old[] = { fontRegular, fontSmall, fontTitle, fontBold };
    auto create = [](int size, int weight) { return CreateFontW(-Scale(size), 0, 0, 0, weight, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI"); };
    fontRegular = create(14, FW_NORMAL); fontSmall = create(12, FW_NORMAL);
    fontTitle = create(26, FW_SEMIBOLD); fontBold = create(14, FW_SEMIBOLD);
    ApplyControlLayout(window);
    for (auto font : old) if (font) DeleteObject(font);
}

void FitWindow(HWND window, const RECT* suggested = nullptr)
{
    const bool previousLayout = layingOut;
    layingOut = true;
    ShowScrollBar(window, SB_BOTH, FALSE);
    MONITORINFO monitor{ sizeof(monitor) };
    GetMonitorInfoW(suggested ? MonitorFromRect(suggested, MONITOR_DEFAULTTONEAREST) : MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), &monitor);
    const DWORD style = static_cast<DWORD>(GetWindowLongPtrW(window, GWL_STYLE));
    RECT outer{ 0, 0, Scale(installer_ui::ContentWidth), Scale(installer_ui::ContentHeight) };
    AdjustWindowRectExForDpi(&outer, style, FALSE, WS_EX_CONTROLPARENT, uiDpi);
    const int width = min(outer.right - outer.left, monitor.rcWork.right - monitor.rcWork.left);
    const int height = min(outer.bottom - outer.top, monitor.rcWork.bottom - monitor.rcWork.top);
    const int x = suggested ? max(monitor.rcWork.left, min(suggested->left, monitor.rcWork.right - width)) : monitor.rcWork.left + (monitor.rcWork.right - monitor.rcWork.left - width) / 2;
    const int y = suggested ? max(monitor.rcWork.top, min(suggested->top, monitor.rcWork.bottom - height)) : monitor.rcWork.top + (monitor.rcWork.bottom - monitor.rcWork.top - height) / 2;
    SetWindowPos(window, nullptr, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
    layingOut = previousLayout;
    ApplyControlLayout(window);
}

class ComApartment
{
public:
    ComApartment() : _result(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED)) { }
    ~ComApartment() { if (SUCCEEDED(_result)) CoUninitialize(); }
    bool IsReady() const { return SUCCEEDED(_result) || _result == RPC_E_CHANGED_MODE; }
private:
    HRESULT _result;
};

std::wstring GetText(HWND control)
{
    const int length = GetWindowTextLengthW(control);
    std::wstring result(length + 1, L'\0');
    GetWindowTextW(control, result.data(), length + 1);
    result.resize(length);
    return result;
}

void PostStatus(HWND window, const std::wstring& text)
{
    PostMessageW(window, WM_INSTALL_STATUS, 0, reinterpret_cast<LPARAM>(new std::wstring(text)));
}

bool IsSafeArchivePath(const fs::path& root, const fs::path& child)
{
    const auto normalizedRoot = fs::weakly_canonical(root);
    const auto normalizedChild = fs::weakly_canonical(child.parent_path()) / child.filename();
    const auto rootText = normalizedRoot.wstring();
    const auto childText = normalizedChild.wstring();
    return childText.size() >= rootText.size() && _wcsnicmp(rootText.c_str(), childText.c_str(), rootText.size()) == 0;
}

void DrawSurfaceText(HWND control, HDC dc, RECT rect, HFONT font, COLORREF color, DWORD flags)
{
    wchar_t text[512]{}; GetWindowTextW(control, text, static_cast<int>(std::size(text)));
    const HTHEME theme = windowMaterial ? OpenThemeData(control, L"Window") : nullptr;
    HDC bufferDc = nullptr;
    const HPAINTBUFFER buffer = theme ? BeginBufferedPaint(dc, &rect, BPBF_TOPDOWNDIB, nullptr, &bufferDc) : nullptr;
    if (buffer)
    {
        BufferedPaintClear(buffer, nullptr);
        if ((GetWindowLongPtrW(control, GWL_STYLE) & BS_TYPEMASK) == BS_OWNERDRAW)
        {
            BitBlt(bufferDc, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, dc, rect.left, rect.top, SRCCOPY);
            BufferedPaintSetAlpha(buffer, nullptr, 255);
        }
        const auto oldFont = SelectObject(bufferDc, font);
        DTTOPTS options{ sizeof(options) };
        options.dwFlags = DTT_COMPOSITED | DTT_TEXTCOLOR;
        options.crText = color;
        DrawThemeTextEx(theme, bufferDc, 0, 0, text, -1, flags, &rect, &options);
        SelectObject(bufferDc, oldFont);
        EndBufferedPaint(buffer, TRUE);
    }
    else
    {
        const auto oldFont = SelectObject(dc, font);
        SetBkMode(dc, TRANSPARENT); SetTextColor(dc, color);
        DrawTextW(dc, text, -1, &rect, flags);
        SelectObject(dc, oldFont);
    }
    if (theme) CloseThemeData(theme);
}

void DrawButton(const DRAWITEMSTRUCT* item)
{
    const bool primary = item->CtlID == IDC_INSTALL;
    const bool disabled = (item->itemState & ODS_DISABLED) != 0;
    const bool pressed = (item->itemState & ODS_SELECTED) != 0;
    POINT cursor{};
    const bool hovered = GetCursorPos(&cursor) && ScreenToClient(item->hwndItem, &cursor) && PtInRect(&item->rcItem, cursor);
    const COLORREF fill = disabled ? RGB(58, 58, 58) : primary ? (pressed ? RGB(0, 91, 170) : hovered ? RGB(18, 143, 237) : RGB(0, 120, 212)) : (pressed ? RGB(45, 45, 45) : hovered ? RGB(65, 65, 65) : RGB(56, 56, 56));
    const COLORREF border = primary ? RGB(18, 156, 239) : RGB(106, 106, 106);
    HBRUSH brush = CreateSolidBrush(fill);
    HPEN pen = CreatePen(PS_SOLID, Scale(1), border);
    const auto oldBrush = SelectObject(item->hDC, brush);
    const auto oldPen = SelectObject(item->hDC, pen);
    RoundRect(item->hDC, item->rcItem.left, item->rcItem.top, item->rcItem.right, item->rcItem.bottom, Scale(8), Scale(8));
    SelectObject(item->hDC, oldBrush); SelectObject(item->hDC, oldPen);
    DeleteObject(brush); DeleteObject(pen);
    DrawSurfaceText(item->hwndItem, item->hDC, item->rcItem, item->CtlID == IDC_INSTALL ? fontBold : fontRegular,
        disabled ? RGB(157, 171, 181) : RGB(255, 255, 255), DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    if ((item->itemState & ODS_FOCUS) && !(item->itemState & ODS_NOFOCUSRECT))
    {
        auto focus = item->rcItem; InflateRect(&focus, -Scale(4), -Scale(4)); DrawFocusRect(item->hDC, &focus);
    }
}

LRESULT DrawCheckbox(const NMCUSTOMDRAW* item)
{
    if (item->dwDrawStage != CDDS_PREPAINT) return CDRF_DODEFAULT;
    const HWND control = item->hdr.hwndFrom;
    const HTHEME theme = OpenThemeData(control, L"Button");
    if (!theme) return CDRF_DODEFAULT;
    const bool checked = SendMessageW(control, BM_GETCHECK, 0, 0) == BST_CHECKED;
    const bool disabled = !IsWindowEnabled(control);
    const int state = (checked ? CBS_CHECKEDNORMAL : CBS_UNCHECKEDNORMAL)
        + (disabled ? 3 : (item->uItemState & CDIS_SELECTED) ? 2 : (item->uItemState & CDIS_HOT) ? 1 : 0);
    FillRect(item->hdc, &item->rc, windowMaterial ? static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)) : windowBrush);
    const int side = Scale(18), top = item->rc.top + (item->rc.bottom - item->rc.top - side) / 2;
    const RECT glyph{ item->rc.left, top, item->rc.left + side, top + side };
    DrawThemeBackground(theme, item->hdc, BP_CHECKBOX, state, &glyph, nullptr);
    RECT textRect = item->rc; textRect.left += side + Scale(10);
    DrawSurfaceText(control, item->hdc, textRect, fontRegular,
        disabled ? RGB(150, 150, 150) : RGB(235, 235, 235), DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    if ((item->uItemState & CDIS_FOCUS) && !(SendMessageW(control, WM_QUERYUISTATE, 0, 0) & UISF_HIDEFOCUS))
        DrawFocusRect(item->hdc, &textRect);
    CloseThemeData(theme);
    return CDRF_SKIPDEFAULT;
}

std::wstring ReadAll(HINTERNET request, HWND window, const fs::path& output)
{
    DWORD length = 0;
    DWORD lengthSize = sizeof(length);
    WinHttpQueryHeaders(request, WINHTTP_QUERY_CONTENT_LENGTH | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX, &length, &lengthSize, WINHTTP_NO_HEADER_INDEX);
    std::ofstream file(output, std::ios::binary);
    if (!file) throw std::runtime_error("file");
    std::vector<char> buffer(64 * 1024);
    DWORD read = 0, downloaded = 0;
    for (;;)
    {
        if (!WinHttpReadData(request, buffer.data(), static_cast<DWORD>(buffer.size()), &read)) throw std::runtime_error("network");
        if (read == 0) break;
        file.write(buffer.data(), read);
        downloaded += read;
        if (length > 0) PostMessageW(window, WM_INSTALL_PROGRESS, min(78, max(1, static_cast<int>(downloaded * 78ull / length))), 0);
    }
    if (length > 0 && downloaded != length) throw std::runtime_error("network");
    if (!file.good()) throw std::runtime_error("file");
    return output.wstring();
}

std::wstring DownloadUrl(HWND window, const std::wstring& url, const fs::path& output, int redirects = 0)
{
    if (redirects > 5) throw std::runtime_error("redirect");
    URL_COMPONENTS parts{};
    parts.dwStructSize = sizeof(parts);
    std::wstring host(256, L'\0'), path(4096, L'\0'), extra(4096, L'\0');
    parts.lpszHostName = host.data(); parts.dwHostNameLength = static_cast<DWORD>(host.size());
    parts.lpszUrlPath = path.data(); parts.dwUrlPathLength = static_cast<DWORD>(path.size());
    parts.lpszExtraInfo = extra.data(); parts.dwExtraInfoLength = static_cast<DWORD>(extra.size());
    if (!WinHttpCrackUrl(url.c_str(), 0, 0, &parts)) throw std::runtime_error("url");
    host.resize(parts.dwHostNameLength); path.resize(parts.dwUrlPathLength); extra.resize(parts.dwExtraInfoLength); path += extra;
    HINTERNET session = WinHttpOpen(L"VitanCut-Setup/1.0", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!session) throw std::runtime_error("network");
    HINTERNET connect = WinHttpConnect(session, host.c_str(), parts.nPort, 0);
    HINTERNET request = connect ? WinHttpOpenRequest(connect, L"GET", path.c_str(), nullptr, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, parts.nScheme == INTERNET_SCHEME_HTTPS ? WINHTTP_FLAG_SECURE : 0) : nullptr;
    if (!request || !WinHttpAddRequestHeaders(request, L"User-Agent: VitanCut-Setup/1.0\r\n", -1, WINHTTP_ADDREQ_FLAG_ADD) || !WinHttpSendRequest(request, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0) || !WinHttpReceiveResponse(request, nullptr))
    {
        if (request) WinHttpCloseHandle(request); if (connect) WinHttpCloseHandle(connect); WinHttpCloseHandle(session); throw std::runtime_error("network");
    }
    DWORD code = 0, codeSize = sizeof(code);
    WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX, &code, &codeSize, WINHTTP_NO_HEADER_INDEX);
    if (code >= 300 && code < 400)
    {
        DWORD size = 0;
        WinHttpQueryHeaders(request, WINHTTP_QUERY_LOCATION, WINHTTP_HEADER_NAME_BY_INDEX, nullptr, &size, WINHTTP_NO_HEADER_INDEX);
        std::wstring location(size / sizeof(wchar_t), L'\0');
        WinHttpQueryHeaders(request, WINHTTP_QUERY_LOCATION, WINHTTP_HEADER_NAME_BY_INDEX, location.data(), &size, WINHTTP_NO_HEADER_INDEX);
        WinHttpCloseHandle(request); WinHttpCloseHandle(connect); WinHttpCloseHandle(session);
        return DownloadUrl(window, location.c_str(), output, redirects + 1);
    }
    if (code < 200 || code >= 300) { WinHttpCloseHandle(request); WinHttpCloseHandle(connect); WinHttpCloseHandle(session); throw std::runtime_error("network"); }
    std::wstring result;
    try { result = ReadAll(request, window, output); }
    catch (...) { WinHttpCloseHandle(request); WinHttpCloseHandle(connect); WinHttpCloseHandle(session); throw; }
    WinHttpCloseHandle(request); WinHttpCloseHandle(connect); WinHttpCloseHandle(session);
    return result;
}

std::wstring ReadUtf8File(const fs::path& path)
{
    std::ifstream file(path, std::ios::binary);
    std::string bytes((std::istreambuf_iterator<char>(file)), {});
    if (bytes.empty()) throw std::runtime_error("release");
    const int size = MultiByteToWideChar(CP_UTF8, 0, bytes.data(), static_cast<int>(bytes.size()), nullptr, 0);
    std::wstring result(size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, bytes.data(), static_cast<int>(bytes.size()), result.data(), size);
    return result;
}

void WriteScriptResource(int id, const fs::path& output)
{
    const auto module = GetModuleHandleW(nullptr);
    const auto resource = FindResourceW(module, MAKEINTRESOURCEW(id), RT_RCDATA);
    if (!resource) throw std::runtime_error("resource");
    const auto data = LockResource(LoadResource(module, resource));
    std::ofstream file(output, std::ios::binary);
    file.write(static_cast<const char*>(data), SizeofResource(module, resource));
    if (!file.good()) throw std::runtime_error("file");
}

void WriteUtf8Line(std::ofstream& file, const std::wstring& text)
{
    const int size = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    std::string bytes(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), bytes.data(), size, nullptr, nullptr);
    file << bytes << '\n';
}

void RunProcess(const std::wstring& command)
{
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    std::vector<wchar_t> mutableCommand(command.begin(), command.end()); mutableCommand.push_back(L'\0');
    if (!CreateProcessW(nullptr, mutableCommand.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process)) throw std::runtime_error("unpack");
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD code = 1; GetExitCodeProcess(process.hProcess, &code);
    CloseHandle(process.hThread); CloseHandle(process.hProcess);
    if (code != 0) throw std::runtime_error("unpack");
}

void CreateShortcut(const fs::path& shortcut, const fs::path& executable)
{
    IShellLinkW* link = nullptr;
    if (FAILED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&link)))) throw std::runtime_error("shortcut");
    link->SetPath(executable.c_str());
    link->SetWorkingDirectory(executable.parent_path().c_str());
    link->SetIconLocation(executable.c_str(), 0);
    link->SetDescription(L"VitanCut");
    IPersistFile* persist = nullptr;
    const HRESULT result = SUCCEEDED(link->QueryInterface(IID_PPV_ARGS(&persist))) ? persist->Save(shortcut.c_str(), TRUE) : E_FAIL;
    if (persist) persist->Release();
    link->Release();
    if (FAILED(result)) throw std::runtime_error("shortcut");
}

void TryPinToTaskbar(const fs::path& executable)
{
    IShellDispatch* shell = nullptr;
    if (FAILED(CoCreateInstance(CLSID_Shell, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&shell)))) return;
    Folder* folder = nullptr;
    VARIANT directory; VariantInit(&directory); directory.vt = VT_BSTR; directory.bstrVal = SysAllocString(executable.parent_path().c_str());
    if (SUCCEEDED(shell->NameSpace(directory, &folder)) && folder)
    {
        FolderItem* item = nullptr;
        VARIANT name; VariantInit(&name); name.vt = VT_BSTR; name.bstrVal = SysAllocString(executable.filename().c_str());
        if (SUCCEEDED(folder->ParseName(name.bstrVal, &item)) && item)
        {
            VARIANT verb; VariantInit(&verb); verb.vt = VT_BSTR; verb.bstrVal = SysAllocString(L"taskbarpin");
            item->InvokeVerb(verb); VariantClear(&verb); item->Release();
        }
        VariantClear(&name); folder->Release();
    }
    VariantClear(&directory); shell->Release();
}

void TryTaskbarVerb(const fs::path& executable, const wchar_t* verbName)
{
    IShellDispatch* shell = nullptr;
    if (FAILED(CoCreateInstance(CLSID_Shell, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&shell)))) return;
    Folder* folder = nullptr;
    VARIANT directory; VariantInit(&directory); directory.vt = VT_BSTR; directory.bstrVal = SysAllocString(executable.parent_path().c_str());
    if (SUCCEEDED(shell->NameSpace(directory, &folder)) && folder)
    {
        FolderItem* item = nullptr;
        VARIANT name; VariantInit(&name); name.vt = VT_BSTR; name.bstrVal = SysAllocString(executable.filename().c_str());
        if (SUCCEEDED(folder->ParseName(name.bstrVal, &item)) && item)
        {
            VARIANT verb; VariantInit(&verb); verb.vt = VT_BSTR; verb.bstrVal = SysAllocString(verbName);
            item->InvokeVerb(verb); VariantClear(&verb); item->Release();
        }
        VariantClear(&name); folder->Release();
    }
    VariantClear(&directory); shell->Release();
}

fs::path GetDesktopShortcut()
{
    wchar_t folder[MAX_PATH]{};
    SHGetFolderPathW(nullptr, CSIDL_DESKTOPDIRECTORY, nullptr, 0, folder);
    return fs::path(folder) / L"VitanCut.lnk";
}

fs::path GetStartMenuShortcut()
{
    wchar_t folder[MAX_PATH]{};
    SHGetFolderPathW(nullptr, CSIDL_PROGRAMS, nullptr, 0, folder);
    return fs::path(folder) / L"VitanCut" / L"VitanCut.lnk";
}

fs::path GetCurrentExecutable()
{
    wchar_t path[MAX_PATH]{};
    const auto length = GetModuleFileNameW(nullptr, path, MAX_PATH);
    if (length == 0 || length == MAX_PATH) throw std::runtime_error("path");
    return fs::path(path, path + length);
}

void SetRegistryString(HKEY key, const wchar_t* name, const std::wstring& value)
{
    RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t)));
}

void RegisterInstallation(const fs::path& target, const fs::path& executable, const fs::path& uninstaller, const std::wstring& version)
{
    HKEY key = nullptr;
    if (RegCreateKeyExW(HKEY_CURRENT_USER, UninstallRegistryPath, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr) != ERROR_SUCCESS)
        throw std::runtime_error("registry");
    SetRegistryString(key, L"DisplayName", L"VitanCut");
    SetRegistryString(key, L"DisplayVersion", version);
    SetRegistryString(key, L"Publisher", L"GMMB");
    SetRegistryString(key, L"InstallLocation", target.wstring());
    SetRegistryString(key, L"InstallSource", target.wstring());
    SetRegistryString(key, L"DisplayIcon", executable.wstring());
    SetRegistryString(key, L"UninstallString", L"\"" + uninstaller.wstring() + L"\"");
    SetRegistryString(key, L"QuietUninstallString", L"\"" + uninstaller.wstring() + L"\" /quiet");
    DWORD enabled = 1;
    RegSetValueExW(key, L"NoModify", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&enabled), sizeof(enabled));
    RegSetValueExW(key, L"NoRepair", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&enabled), sizeof(enabled));
    RegCloseKey(key);
}

void RemoveInstallationRegistration()
{
    RegDeleteTreeW(HKEY_CURRENT_USER, UninstallRegistryPath);
}

void ScheduleInstallDirectoryRemoval(const fs::path& target)
{
    const auto script = fs::temp_directory_path() / (L"VitanCut-uninstall-" + std::to_wstring(GetTickCount64()) + L".ps1");
    WriteScriptResource(202, script);
    const auto command = L"powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + script.wstring() + L"\" -Target \"" + target.wstring() + L"\" -ParentPid " + std::to_wstring(GetCurrentProcessId());
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    std::vector<wchar_t> mutableCommand(command.begin(), command.end()); mutableCommand.push_back(L'\0');
    if (CreateProcessW(nullptr, mutableCommand.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process))
    {
        CloseHandle(process.hThread); CloseHandle(process.hProcess);
    }
    else throw std::runtime_error("uninstall");
}

int RunUninstaller()
{
    const auto uninstaller = GetCurrentExecutable();
    const auto target = uninstaller.parent_path();
    if (!fs::exists(target / L"VitanCut.install-files.txt"))
    {
        MessageBoxW(nullptr, L"Не найден список установленных файлов. Автоматическое удаление отменено, чтобы сохранить ваши данные.", L"Удаление VitanCut", MB_ICONERROR | MB_OK);
        return 1;
    }
    if (MessageBoxW(nullptr, L"Удалить VitanCut и созданные им ярлыки?\nБаза данных и другие пользовательские файлы будут сохранены.", L"Удаление VitanCut", MB_ICONQUESTION | MB_YESNO) != IDYES)
        return 0;
    ScheduleInstallDirectoryRemoval(target);
    TryTaskbarVerb(target / L"VitanCut.WinUI.exe", L"taskbarunpin");
    std::error_code ignored;
    fs::remove(GetDesktopShortcut(), ignored);
    fs::remove(GetStartMenuShortcut(), ignored);
    fs::remove(GetStartMenuShortcut().parent_path(), ignored);
    RemoveInstallationRegistration();
    MessageBoxW(nullptr, L"Удаление файлов завершится после закрытия этого окна. База данных останется сохранена.", L"Удаление VitanCut", MB_ICONINFORMATION | MB_OK);
    return 0;
}

InstallResult Install(HWND window, const fs::path& target, bool desktop, bool startMenu, bool taskbar)
{
    ComApartment com;
    if (!com.IsReady()) return { false, L"Не удалось подготовить Windows для создания ярлыков." };
    const auto staging = fs::temp_directory_path() / (L"VitanCut-setup-" + std::to_wstring(GetTickCount64()));
    try
    {
        fs::create_directories(staging);
        PostStatus(window, L"Получаем информацию о последней версии...");
        DownloadUrl(window, ReleasesUrl, staging / L"release.json");
        WriteScriptResource(201, staging / L"select-release.ps1");
        try {
            RunProcess(L"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + (staging / L"select-release.ps1").wstring() + L"\" -Metadata \"" + (staging / L"release.json").wstring() + L"\" -Output \"" + staging.wstring() + L"\"");
        } catch (...) { throw std::runtime_error("release"); }
        const auto archiveUrl = ReadUtf8File(staging / L"archive-url.txt");
        const auto appVersion = ReadUtf8File(staging / L"app-version.txt");
        PostStatus(window, L"Скачиваем программу...");
        DownloadUrl(window, archiveUrl, staging / L"vitan-cut.zip");
        PostMessageW(window, WM_INSTALL_PROGRESS, 82, 0);
        PostStatus(window, L"Распаковываем файлы...");
        const auto extracted = staging / L"files";
        fs::create_directories(extracted);
        RunProcess(L"tar.exe -xf \"" + (staging / L"vitan-cut.zip").wstring() + L"\" -C \"" + extracted.wstring() + L"\"");
        fs::path executable;
        for (const auto& entry : fs::recursive_directory_iterator(extracted)) if (entry.path().filename() == L"VitanCut.WinUI.exe") { executable = entry.path(); break; }
        if (executable.empty()) throw std::runtime_error("unpack");
        PostStatus(window, L"Устанавливаем VitanCut...");
        fs::create_directories(target);
        std::ofstream manifest(staging / L"install-files.txt", std::ios::binary);
        if (!manifest) throw std::runtime_error("file");
        for (const auto& entry : fs::directory_iterator(executable.parent_path()))
        {
            // Installed data is migrated to the user profile by VitanCut 1.0.13.
            // Do not overwrite a previous local database before that migration can run.
            if (entry.path().filename() == L"data") continue;
            fs::copy(entry.path(), target / entry.path().filename(), fs::copy_options::recursive | fs::copy_options::overwrite_existing);
            if (entry.is_regular_file()) WriteUtf8Line(manifest, entry.path().filename().wstring());
            else for (const auto& file : fs::recursive_directory_iterator(entry.path()))
                if (file.is_regular_file()) WriteUtf8Line(manifest, fs::relative(file.path(), executable.parent_path()).wstring());
        }
        const auto installed = target / L"VitanCut.WinUI.exe";
        if (!fs::exists(installed)) throw std::runtime_error("unpack");
        const auto uninstaller = target / L"VitanCut.Uninstall.exe";
        fs::copy_file(GetCurrentExecutable(), uninstaller, fs::copy_options::overwrite_existing);
        WriteUtf8Line(manifest, L"VitanCut.Uninstall.exe");
        manifest.close();
        if (!manifest.good()) throw std::runtime_error("file");
        fs::copy_file(staging / L"install-files.txt", target / L"VitanCut.install-files.txt", fs::copy_options::overwrite_existing);
        RegisterInstallation(target, installed, uninstaller, appVersion);
        if (desktop) CreateShortcut(fs::path([] { wchar_t folder[MAX_PATH]; SHGetFolderPathW(nullptr, CSIDL_DESKTOPDIRECTORY, nullptr, 0, folder); return std::wstring(folder); }()) / L"VitanCut.lnk", installed);
        if (startMenu)
        {
            wchar_t folder[MAX_PATH]; SHGetFolderPathW(nullptr, CSIDL_PROGRAMS, nullptr, 0, folder);
            const auto menu = fs::path(folder) / L"VitanCut"; fs::create_directories(menu); CreateShortcut(menu / L"VitanCut.lnk", installed);
        }
        if (taskbar) TryPinToTaskbar(installed);
        fs::remove_all(staging);
        return { true, L"VitanCut установлен. Программу можно открыть через созданный ярлык." };
    }
    catch (const std::exception& error)
    {
        std::error_code ignored; fs::remove_all(staging, ignored);
        const std::string reason = error.what();
        if (reason == "network") return { false, L"Не удалось скачать программу. Проверьте подключение к интернету." };
        if (reason == "release") return { false, L"В последнем релизе GitHub не найден архив VitanCut." };
        if (reason == "unpack") return { false, L"Не удалось распаковать архив программы." };
        if (reason == "shortcut") return { false, L"Программа установлена, но не удалось создать один из ярлыков." };
        return { false, L"Не удалось записать файлы в выбранную папку. Проверьте путь и права доступа." };
    }
}

LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message)
    {
    case WM_SETTINGCHANGE:
    case WM_THEMECHANGED:
    case WM_DWMCOMPOSITIONCHANGED:
        ApplyWindowMaterial(window); return 0;
    case WM_SIZE: ApplyControlLayout(window); return 0;
    case WM_DPICHANGED:
        uiDpi = HIWORD(wParam); scrollX = scrollY = 0;
        RecreateFonts(window); FitWindow(window, reinterpret_cast<const RECT*>(lParam)); ApplyControlLayout(window);
        EnsureControlVisible(window, GetFocus()); return 0;
    case WM_HSCROLL:
    case WM_VSCROLL:
    {
        const int bar = message == WM_VSCROLL ? SB_VERT : SB_HORZ;
        SCROLLINFO info{ sizeof(info), SIF_ALL }; GetScrollInfo(window, bar, &info);
        int position = info.nPos;
        switch (LOWORD(wParam))
        {
        case SB_LINEUP: position -= Scale(20); break;
        case SB_LINEDOWN: position += Scale(20); break;
        case SB_PAGEUP: position -= static_cast<int>(info.nPage); break;
        case SB_PAGEDOWN: position += static_cast<int>(info.nPage); break;
        case SB_THUMBPOSITION:
        case SB_THUMBTRACK: position = info.nTrackPos; break;
        case SB_TOP: position = 0; break;
        case SB_BOTTOM: position = info.nMax; break;
        default: return 0;
        }
        if (bar == SB_VERT) scrollY = position; else scrollX = position;
        ApplyControlLayout(window); return 0;
    }
    case WM_MOUSEWHEEL:
        scrollY -= MulDiv(static_cast<short>(HIWORD(wParam)), Scale(60), WHEEL_DELTA);
        ApplyControlLayout(window); return 0;
    case WM_TIMER:
        if (wParam == PreviewTimer && uiPreview)
        {
            previewProgress += 10;
            SendMessageW(progressBar, PBM_SETPOS, previewProgress, 0);
            if (previewProgress >= 100)
            {
                KillTimer(window, PreviewTimer);
                PostMessageW(window, WM_INSTALL_FINISHED, !uiPreviewFailure, reinterpret_cast<LPARAM>(new std::wstring(uiPreviewFailure
                    ? L"Проверка интерфейса: нет соединения. Установка не выполнялась."
                    : L"Проверка интерфейса завершена. Файлы и ярлыки не изменялись.")));
            }
            return 0;
        }
        break;
    case WM_CTLCOLORSTATIC:
    {
        auto dc = reinterpret_cast<HDC>(wParam); SetTextColor(dc, RGB(225, 232, 238)); SetBkMode(dc, TRANSPARENT); return reinterpret_cast<LRESULT>(windowMaterial ? GetStockObject(BLACK_BRUSH) : windowBrush);
    }
    case WM_CTLCOLOREDIT:
    {
        auto dc = reinterpret_cast<HDC>(wParam); SetTextColor(dc, RGB(241, 247, 251)); SetBkColor(dc, RGB(45, 45, 45)); return reinterpret_cast<LRESULT>(inputBrush);
    }
    case WM_CTLCOLORBTN:
    {
        auto dc = reinterpret_cast<HDC>(wParam); SetTextColor(dc, RGB(225, 232, 238)); SetBkColor(dc, RGB(32, 32, 32)); return reinterpret_cast<LRESULT>(windowBrush);
    }
    case WM_DRAWITEM:
        if (const auto* item = reinterpret_cast<DRAWITEMSTRUCT*>(lParam); item->CtlType == ODT_STATIC)
        {
            FillRect(item->hDC, &item->rcItem, windowMaterial ? static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)) : windowBrush);
            DrawSurfaceText(item->hwndItem, item->hDC, item->rcItem,
                reinterpret_cast<HFONT>(SendMessageW(item->hwndItem, WM_GETFONT, 0, 0)), RGB(235, 235, 235), DT_LEFT | DT_WORDBREAK);
            return TRUE;
        }
        if (const auto* item = reinterpret_cast<DRAWITEMSTRUCT*>(lParam); item->CtlType == ODT_BUTTON && (item->CtlID == IDC_BROWSE || item->CtlID == IDC_INSTALL)) { DrawButton(item); return TRUE; }
        break;
    case WM_NOTIFY:
    {
        const auto* item = reinterpret_cast<NMCUSTOMDRAW*>(lParam);
        if (item->hdr.code == NM_CUSTOMDRAW && (item->hdr.idFrom == IDC_DESKTOP || item->hdr.idFrom == IDC_START_MENU || item->hdr.idFrom == IDC_TASKBAR))
            return DrawCheckbox(item);
        break;
    }
    case WM_COMMAND:
        if (LOWORD(wParam) == LanguageControlId && HIWORD(wParam) == CBN_SELCHANGE)
        {
            const auto selected = SendMessageW(reinterpret_cast<HWND>(lParam), CB_GETCURSEL, 0, 0);
            if (selected >= 0 && selected <= 4) { uiLanguage = static_cast<int>(selected); RefreshLanguage(window); }
            return 0;
        }
        if (HIWORD(wParam) != BN_CLICKED) break;
        if (LOWORD(wParam) == IDC_BROWSE)
        {
            const std::wstring title = installer_l10n::Translate(L"Выберите папку для установки VitanCut", uiLanguage);
            BROWSEINFOW info{}; info.hwndOwner = window; info.lpszTitle = title.c_str(); info.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;
            if (PIDLIST_ABSOLUTE item = SHBrowseForFolderW(&info)) { wchar_t folder[MAX_PATH]; if (SHGetPathFromIDListW(item, folder)) SetWindowTextW(pathBox, folder); CoTaskMemFree(item); }
        }
        else if (LOWORD(wParam) == IDC_INSTALL)
        {
            if (installationComplete) { DestroyWindow(window); return 0; }
            if (uiPreview)
            {
                previewProgress = 0;
                SendMessageW(progressBar, PBM_SETPOS, 0, 0);
                ShowWindow(progressBar, SW_SHOW);
                SetLocalizedText(statusLabel, L"Проверяем интерфейс...");
                EnableWindow(installButton, FALSE);
                SetTimer(window, PreviewTimer, 100, nullptr);
                return 0;
            }
#ifndef VITANCUT_UI_ONLY
            const auto path = GetText(pathBox);
            if (path.empty()) { SetLocalizedText(statusLabel, L"Укажите папку установки."); return 0; }
            EnableWindow(installButton, FALSE); EnableWindow(pathBox, FALSE); EnableWindow(desktopBox, FALSE); EnableWindow(startMenuBox, FALSE); EnableWindow(taskbarBox, FALSE);
            EnableWindow(GetDlgItem(window, IDC_BROWSE), FALSE);
            SendMessageW(progressBar, PBM_SETPOS, 1, 0); ShowWindow(progressBar, SW_SHOW); SetLocalizedText(statusLabel, L"Готовим установку...");
            const bool desktop = SendMessageW(desktopBox, BM_GETCHECK, 0, 0) == BST_CHECKED;
            const bool start = SendMessageW(startMenuBox, BM_GETCHECK, 0, 0) == BST_CHECKED;
            const bool taskbar = SendMessageW(taskbarBox, BM_GETCHECK, 0, 0) == BST_CHECKED;
            std::thread([window, path, desktop, start, taskbar] { auto result = Install(window, fs::path(path), desktop, start, taskbar); PostMessageW(window, WM_INSTALL_FINISHED, result.success, reinterpret_cast<LPARAM>(new std::wstring(result.message))); }).detach();
#endif
        }
        break;
    case WM_INSTALL_PROGRESS: SendMessageW(progressBar, PBM_SETPOS, wParam, 0); return 0;
    case WM_INSTALL_STATUS:
    {
        auto text = reinterpret_cast<std::wstring*>(lParam); SetLocalizedText(statusLabel, *text); delete text; return 0;
    }
    case WM_INSTALL_FINISHED:
    {
        auto text = reinterpret_cast<std::wstring*>(lParam); SetLocalizedText(statusLabel, *text); delete text;
        SendMessageW(progressBar, PBM_SETPOS, wParam ? 100 : 0, 0);
        if (wParam) { installationComplete = true; SetLocalizedText(installButton, L"Готово"); }
        else { SetLocalizedText(installButton, L"Повторить"); }
        EnableWindow(GetDlgItem(window, IDC_BROWSE), TRUE);
        EnableWindow(installButton, TRUE); EnableWindow(pathBox, TRUE); EnableWindow(desktopBox, TRUE); EnableWindow(startMenuBox, TRUE); EnableWindow(taskbarBox, TRUE); return 0;
    }
    case WM_PAINT:
    {
        PAINTSTRUCT paint{}; HDC dc = BeginPaint(window, &paint);
        const auto pathPen = CreatePen(PS_SOLID, Scale(1), GetFocus() == pathBox ? RGB(0, 156, 240) : RGB(106, 106, 106));
        auto oldPathPen = SelectObject(dc, pathPen); auto oldPathBrush = SelectObject(dc, inputBrush);
        RoundRect(dc, Scale(28) - scrollX, Scale(196) - scrollY, Scale(396) - scrollX, Scale(236) - scrollY, Scale(8), Scale(8));
        SelectObject(dc, oldPathBrush); SelectObject(dc, oldPathPen); DeleteObject(pathPen);
        HICON icon = reinterpret_cast<HICON>(LoadImageW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDI_INSTALLER), IMAGE_ICON, Scale(56), Scale(56), LR_SHARED)); DrawIconEx(dc, Scale(28) - scrollX, Scale(30) - scrollY, icon, Scale(56), Scale(56), 0, nullptr, DI_NORMAL);
        HPEN pen = CreatePen(PS_SOLID, Scale(1), RGB(69, 84, 97)); auto oldPen = SelectObject(dc, pen); MoveToEx(dc, Scale(28) - scrollX, Scale(117) - scrollY, nullptr); LineTo(dc, Scale(490) - scrollX, Scale(117) - scrollY); SelectObject(dc, oldPen); DeleteObject(pen);
        EndPaint(window, &paint); return 0;
    }
    case WM_DESTROY: PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

int APIENTRY wWinMain(HINSTANCE instance, HINSTANCE, PWSTR commandLine, int show)
{
    uiPreviewFailure = commandLine && std::wstring(commandLine) == L"--ui-preview-error";
#ifdef VITANCUT_UI_ONLY
    uiPreview = true;
#else
    uiPreview = uiPreviewFailure || (commandLine && std::wstring(commandLine) == L"--ui-preview");
    if (!uiPreview && GetCurrentExecutable().filename() == L"VitanCut.Uninstall.exe")
    {
        try { return RunUninstaller(); }
        catch (...) { MessageBoxW(nullptr, L"Не удалось начать удаление. Файлы программы и база не удалялись.", L"Удаление VitanCut", MB_ICONERROR | MB_OK); return 1; }
    }
#endif
    INITCOMMONCONTROLSEX controls{ sizeof(controls), ICC_PROGRESS_CLASS | ICC_STANDARD_CLASSES }; InitCommonControlsEx(&controls); CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED); BufferedPaintInit();
    uiDpi = GetDpiForSystem();
    WNDCLASSW cls{}; cls.hInstance = instance; cls.lpszClassName = WindowClass; cls.lpfnWndProc = WindowProcedure; cls.hCursor = LoadCursor(nullptr, IDC_ARROW); cls.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(IDI_INSTALLER)); cls.hbrBackground = CreateSolidBrush(RGB(31, 38, 45)); RegisterClassW(&cls);
    HWND window = CreateWindowExW(WS_EX_CONTROLPARENT, WindowClass, uiPreview ? L"VitanCut - проверка интерфейса" : L"Установка VitanCut", WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_VSCROLL | WS_HSCROLL, CW_USEDEFAULT, CW_USEDEFAULT, Scale(534), Scale(526), nullptr, nullptr, instance, nullptr);
    uiDpi = GetDpiForWindow(window); RecreateFonts(window);
    ApplyWindowMaterial(window);
    auto createLabel = [window](const wchar_t* text, int x, int y, int width, int height, HFONT* font) { auto control = TrackControl(CreateWindowW(L"STATIC", text, WS_CHILD | WS_VISIBLE | SS_OWNERDRAW, Scale(x), Scale(y), Scale(width), Scale(height), window, nullptr, nullptr, nullptr), x, y, width, height, font); SetLocalizedText(control, text); return control; };
    createLabel(L"VitanCut", 102, 30, 340, 34, &fontTitle); createLabel(uiPreview ? L"Проверка интерфейса" : L"Установка программы", 104, 64, 300, 24, &fontRegular); createLabel(L"Программа будет установлена в выбранную папку.", 28, 168, 380, 22, &fontSmall);
    createLabel(L"GMMB", 28, 469, 100, 18, &fontSmall);
    wchar_t documents[MAX_PATH]{}; SHGetFolderPathW(nullptr, CSIDL_PERSONAL, nullptr, 0, documents);
    // Native accessibility associates an edit with its preceding static label.
    createLabel(L"Папка установки", 28, 142, 230, 24, &fontBold);
    pathBox = TrackControl(CreateWindowExW(0, L"EDIT", (fs::path(documents) / L"VitanCut").c_str(), WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL, Scale(38), Scale(208), Scale(348), Scale(22), window, reinterpret_cast<HMENU>(static_cast<INT_PTR>(IDC_INSTALL_PATH)), instance, nullptr), 38, 208, 348, 22, &fontRegular);
    SetWindowLongPtrW(pathBox, GWL_EXSTYLE, GetWindowLongPtrW(pathBox, GWL_EXSTYLE) | WS_EX_LAYERED);
    SetLayeredWindowAttributes(pathBox, 0, 255, LWA_ALPHA);
    auto createButton = [window, instance](const wchar_t* text, DWORD style, int id, int x, int y, int width, int height) { auto control = TrackControl(CreateWindowW(L"BUTTON", text, WS_CHILD | WS_VISIBLE | WS_TABSTOP | style, Scale(x), Scale(y), Scale(width), Scale(height), window, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)), instance, nullptr), x, y, width, height, id == IDC_INSTALL ? &fontBold : &fontRegular); SetLocalizedText(control, text); return control; };
    createButton(L"Обзор", BS_OWNERDRAW, IDC_BROWSE, 406, 196, 84, 40);
    createLabel(L"Ярлыки", 28, 260, 230, 24, &fontBold);
    desktopBox = createButton(L"Ярлык на рабочем столе", BS_AUTOCHECKBOX, IDC_DESKTOP, 28, 288, 300, 27); startMenuBox = createButton(L"Ярлык в меню Пуск", BS_AUTOCHECKBOX, IDC_START_MENU, 28, 320, 300, 27); taskbarBox = createButton(L"Закрепить на панели задач", BS_AUTOCHECKBOX, IDC_TASKBAR, 28, 352, 300, 27); SendMessageW(desktopBox, BM_SETCHECK, BST_CHECKED, 0); SendMessageW(startMenuBox, BM_SETCHECK, BST_CHECKED, 0);
    progressBar = TrackControl(CreateWindowW(PROGRESS_CLASSW, nullptr, WS_CHILD | PBS_SMOOTH, Scale(28), Scale(397), Scale(320), Scale(8), window, reinterpret_cast<HMENU>(IDC_PROGRESS), instance, nullptr), 28, 397, 320, 8, &fontRegular); SendMessageW(progressBar, PBM_SETRANGE, 0, MAKELPARAM(0, 100)); statusLabel = createLabel(L"", 28, 413, 320, 56, &fontSmall); installButton = createButton(uiPreview ? L"Проверить" : L"Установить", BS_OWNERDRAW, IDC_INSTALL, 360, 398, 130, 44);
    createLabel(L"Язык", 145, 467, 90, 24, &fontSmall);
    const auto languageBox = TrackControl(CreateWindowW(L"COMBOBOX", nullptr,
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | CBS_DROPDOWNLIST | WS_VSCROLL,
        Scale(240), Scale(462), Scale(250), Scale(190), window,
        reinterpret_cast<HMENU>(static_cast<INT_PTR>(LanguageControlId)), instance, nullptr),
        240, 462, 250, 190, &fontSmall);
    for (const auto* name : { L"Русский", L"English", L"Deutsch", L"Español", L"简体中文" })
        SendMessageW(languageBox, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(name));
    SendMessageW(languageBox, CB_SETCURSEL, uiLanguage, 0);
    controlsReady = true; FitWindow(window); ApplyControlLayout(window);
    ShowWindow(window, show); UpdateWindow(window);
    SetFocus(pathBox);
    MSG message; while (GetMessageW(&message, nullptr, 0, 0)) { if (IsDialogMessageW(window, &message)) continue; TranslateMessage(&message); DispatchMessageW(&message); }
    DeleteObject(fontRegular); DeleteObject(fontSmall); DeleteObject(fontTitle); DeleteObject(fontBold); DeleteObject(windowBrush); DeleteObject(inputBrush); BufferedPaintUnInit(); CoUninitialize(); return 0;
}

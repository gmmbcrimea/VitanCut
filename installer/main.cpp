#include <windows.h>
#include <commctrl.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <winhttp.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <thread>
#include <vector>
#include "resource.h"

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "winhttp.lib")

namespace fs = std::filesystem;

constexpr wchar_t WindowClass[] = L"VitanCutInstallerWindow";
constexpr wchar_t ReleasesUrl[] = L"https://api.github.com/repos/gmmbcrimea/VitanCut/releases/latest";
constexpr UINT WM_INSTALL_PROGRESS = WM_APP + 1;
constexpr UINT WM_INSTALL_STATUS = WM_APP + 2;
constexpr UINT WM_INSTALL_FINISHED = WM_APP + 3;

HWND pathBox, desktopBox, startMenuBox, taskbarBox, installButton, progressBar, statusLabel;
HFONT fontRegular, fontSmall, fontTitle, fontBold;
HBRUSH windowBrush = CreateSolidBrush(RGB(31, 38, 45));
HBRUSH inputBrush = CreateSolidBrush(RGB(48, 60, 70));

struct InstallResult { bool success; std::wstring message; };

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

void DrawButton(const DRAWITEMSTRUCT* item)
{
    const bool primary = item->CtlID == IDC_INSTALL;
    const bool disabled = (item->itemState & ODS_DISABLED) != 0;
    const bool pressed = (item->itemState & ODS_SELECTED) != 0;
    const COLORREF fill = disabled ? RGB(61, 73, 83) : primary ? (pressed ? RGB(0, 91, 170) : RGB(0, 126, 224)) : (pressed ? RGB(70, 84, 96) : RGB(55, 68, 79));
    const COLORREF border = primary ? RGB(18, 156, 239) : RGB(109, 132, 148);
    HBRUSH brush = CreateSolidBrush(fill);
    HPEN pen = CreatePen(PS_SOLID, 1, border);
    const auto oldBrush = SelectObject(item->hDC, brush);
    const auto oldPen = SelectObject(item->hDC, pen);
    RoundRect(item->hDC, item->rcItem.left, item->rcItem.top, item->rcItem.right, item->rcItem.bottom, 8, 8);
    SelectObject(item->hDC, oldBrush); SelectObject(item->hDC, oldPen);
    DeleteObject(brush); DeleteObject(pen);
    wchar_t text[128]{}; GetWindowTextW(item->hwndItem, text, static_cast<int>(std::size(text)));
    SetBkMode(item->hDC, TRANSPARENT);
    SetTextColor(item->hDC, disabled ? RGB(157, 171, 181) : RGB(255, 255, 255));
    SelectObject(item->hDC, item->CtlID == IDC_INSTALL ? fontBold : fontRegular);
    DrawTextW(item->hDC, text, -1, const_cast<RECT*>(&item->rcItem), DT_CENTER | DT_VCENTER | DT_SINGLELINE);
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
    while (WinHttpReadData(request, buffer.data(), static_cast<DWORD>(buffer.size()), &read) && read > 0)
    {
        file.write(buffer.data(), read);
        downloaded += read;
        if (length > 0) PostMessageW(window, WM_INSTALL_PROGRESS, min(78, max(1, static_cast<int>(downloaded * 78ull / length))), 0);
    }
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
    const auto result = ReadAll(request, window, output);
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

std::wstring FindPortableArchiveUrl(const std::wstring& json)
{
    const auto asset = json.find(L"VitanCut-portable-win-x64-");
    if (asset == std::wstring::npos) throw std::runtime_error("release");
    const auto key = json.find(L"\"browser_download_url\":\"", asset);
    if (key == std::wstring::npos) throw std::runtime_error("release");
    const auto start = key + wcslen(L"\"browser_download_url\":\"");
    const auto end = json.find(L'\"', start);
    if (end == std::wstring::npos) throw std::runtime_error("release");
    return json.substr(start, end - start);
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

InstallResult Install(HWND window, const fs::path& target, bool desktop, bool startMenu, bool taskbar)
{
    const auto staging = fs::temp_directory_path() / (L"VitanCut-setup-" + std::to_wstring(GetTickCount64()));
    try
    {
        fs::create_directories(staging);
        PostStatus(window, L"Получаем информацию о последней версии...");
        DownloadUrl(window, ReleasesUrl, staging / L"release.json");
        const auto archiveUrl = FindPortableArchiveUrl(ReadUtf8File(staging / L"release.json"));
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
        for (const auto& entry : fs::directory_iterator(executable.parent_path()))
            fs::copy(entry.path(), target / entry.path().filename(), fs::copy_options::recursive | fs::copy_options::overwrite_existing);
        const auto installed = target / L"VitanCut.WinUI.exe";
        if (!fs::exists(installed)) throw std::runtime_error("unpack");
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
    case WM_CTLCOLORSTATIC:
    {
        auto dc = reinterpret_cast<HDC>(wParam); SetTextColor(dc, RGB(225, 232, 238)); SetBkColor(dc, RGB(31, 38, 45)); return reinterpret_cast<LRESULT>(windowBrush);
    }
    case WM_CTLCOLOREDIT:
    {
        auto dc = reinterpret_cast<HDC>(wParam); SetTextColor(dc, RGB(241, 247, 251)); SetBkColor(dc, RGB(48, 60, 70)); return reinterpret_cast<LRESULT>(inputBrush);
    }
    case WM_CTLCOLORBTN:
    {
        auto dc = reinterpret_cast<HDC>(wParam); SetTextColor(dc, RGB(225, 232, 238)); SetBkColor(dc, RGB(31, 38, 45)); return reinterpret_cast<LRESULT>(windowBrush);
    }
    case WM_DRAWITEM:
        if (const auto* item = reinterpret_cast<DRAWITEMSTRUCT*>(lParam); item->CtlType == ODT_BUTTON && (item->CtlID == IDC_BROWSE || item->CtlID == IDC_INSTALL)) { DrawButton(item); return TRUE; }
        break;
    case WM_COMMAND:
        if (LOWORD(wParam) == IDC_BROWSE)
        {
            BROWSEINFOW info{}; info.hwndOwner = window; info.lpszTitle = L"Выберите папку для установки VitanCut"; info.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;
            if (PIDLIST_ABSOLUTE item = SHBrowseForFolderW(&info)) { wchar_t folder[MAX_PATH]; if (SHGetPathFromIDListW(item, folder)) SetWindowTextW(pathBox, folder); CoTaskMemFree(item); }
        }
        else if (LOWORD(wParam) == IDC_INSTALL)
        {
            const auto path = GetText(pathBox);
            if (path.empty()) { SetWindowTextW(statusLabel, L"Укажите папку установки."); return 0; }
            EnableWindow(installButton, FALSE); EnableWindow(pathBox, FALSE); EnableWindow(desktopBox, FALSE); EnableWindow(startMenuBox, FALSE); EnableWindow(taskbarBox, FALSE);
            SendMessageW(progressBar, PBM_SETPOS, 1, 0); ShowWindow(progressBar, SW_SHOW); SetWindowTextW(statusLabel, L"Готовим установку...");
            const bool desktop = SendMessageW(desktopBox, BM_GETCHECK, 0, 0) == BST_CHECKED;
            const bool start = SendMessageW(startMenuBox, BM_GETCHECK, 0, 0) == BST_CHECKED;
            const bool taskbar = SendMessageW(taskbarBox, BM_GETCHECK, 0, 0) == BST_CHECKED;
            std::thread([window, path, desktop, start, taskbar] { auto result = Install(window, fs::path(path), desktop, start, taskbar); PostMessageW(window, WM_INSTALL_FINISHED, result.success, reinterpret_cast<LPARAM>(new std::wstring(result.message))); }).detach();
        }
        break;
    case WM_INSTALL_PROGRESS: SendMessageW(progressBar, PBM_SETPOS, wParam, 0); return 0;
    case WM_INSTALL_STATUS:
    {
        auto text = reinterpret_cast<std::wstring*>(lParam); SetWindowTextW(statusLabel, text->c_str()); delete text; return 0;
    }
    case WM_INSTALL_FINISHED:
    {
        auto text = reinterpret_cast<std::wstring*>(lParam); SetWindowTextW(statusLabel, text->c_str()); delete text;
        SendMessageW(progressBar, PBM_SETPOS, wParam ? 100 : 0, 0);
        if (wParam) SetWindowTextW(installButton, L"Готово");
        EnableWindow(installButton, TRUE); EnableWindow(pathBox, TRUE); EnableWindow(desktopBox, TRUE); EnableWindow(startMenuBox, TRUE); EnableWindow(taskbarBox, TRUE); return 0;
    }
    case WM_PAINT:
    {
        PAINTSTRUCT paint{}; HDC dc = BeginPaint(window, &paint);
        HICON icon = LoadIconW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDI_INSTALLER)); DrawIconEx(dc, 28, 26, icon, 76, 76, 0, nullptr, DI_NORMAL);
        HPEN pen = CreatePen(PS_SOLID, 1, RGB(69, 84, 97)); auto oldPen = SelectObject(dc, pen); MoveToEx(dc, 28, 117, nullptr); LineTo(dc, 490, 117); SelectObject(dc, oldPen); DeleteObject(pen);
        EndPaint(window, &paint); return 0;
    }
    case WM_DESTROY: PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

int APIENTRY wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int show)
{
    INITCOMMONCONTROLSEX controls{ sizeof(controls), ICC_PROGRESS_CLASS | ICC_STANDARD_CLASSES }; InitCommonControlsEx(&controls); CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    fontRegular = CreateFontW(-17, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    fontSmall = CreateFontW(-14, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    fontTitle = CreateFontW(-32, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    fontBold = CreateFontW(-17, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    WNDCLASSW cls{}; cls.hInstance = instance; cls.lpszClassName = WindowClass; cls.lpfnWndProc = WindowProcedure; cls.hCursor = LoadCursor(nullptr, IDC_ARROW); cls.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(IDI_INSTALLER)); cls.hbrBackground = CreateSolidBrush(RGB(31, 38, 45)); RegisterClassW(&cls);
    HWND window = CreateWindowExW(0, WindowClass, L"Установка VitanCut", WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX, CW_USEDEFAULT, CW_USEDEFAULT, 534, 514, nullptr, nullptr, instance, nullptr);
    auto createLabel = [window](const wchar_t* text, int x, int y, int width, int height, HFONT font) { HWND label = CreateWindowW(L"STATIC", text, WS_CHILD | WS_VISIBLE, x, y, width, height, window, nullptr, nullptr, nullptr); SendMessageW(label, WM_SETFONT, reinterpret_cast<WPARAM>(font), TRUE); return label; };
    createLabel(L"VitanCut", 118, 34, 340, 34, fontTitle); createLabel(L"Установка программы", 120, 70, 300, 24, fontRegular); createLabel(L"Папка установки", 28, 142, 230, 24, fontBold); createLabel(L"Программа будет установлена в выбранную папку.", 28, 168, 380, 22, fontSmall); createLabel(L"Ярлыки", 28, 260, 230, 24, fontBold);
    pathBox = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", (fs::path(getenv("LOCALAPPDATA")) / L"Programs" / L"VitanCut").c_str(), WS_CHILD | WS_VISIBLE | ES_AUTOHSCROLL, 28, 196, 368, 34, window, reinterpret_cast<HMENU>(static_cast<INT_PTR>(IDC_INSTALL_PATH)), instance, nullptr); SendMessageW(pathBox, WM_SETFONT, reinterpret_cast<WPARAM>(fontRegular), TRUE);
    auto createButton = [window, instance](const wchar_t* text, DWORD style, int id, int x, int y, int width, int height) { HWND button = CreateWindowW(L"BUTTON", text, WS_CHILD | WS_VISIBLE | style, x, y, width, height, window, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)), instance, nullptr); SendMessageW(button, WM_SETFONT, reinterpret_cast<WPARAM>(fontRegular), TRUE); return button; };
    createButton(L"Обзор", BS_OWNERDRAW, IDC_BROWSE, 406, 196, 84, 34); desktopBox = createButton(L"Ярлык на рабочем столе", BS_AUTOCHECKBOX, IDC_DESKTOP, 28, 288, 300, 27); startMenuBox = createButton(L"Ярлык в меню Пуск", BS_AUTOCHECKBOX, IDC_START_MENU, 28, 320, 300, 27); taskbarBox = createButton(L"Закрепить на панели задач", BS_AUTOCHECKBOX, IDC_TASKBAR, 28, 352, 300, 27); SendMessageW(desktopBox, BM_SETCHECK, BST_CHECKED, 0); SendMessageW(startMenuBox, BM_SETCHECK, BST_CHECKED, 0);
    progressBar = CreateWindowW(PROGRESS_CLASSW, nullptr, WS_CHILD, 28, 397, 320, 8, window, reinterpret_cast<HMENU>(IDC_PROGRESS), instance, nullptr); SendMessageW(progressBar, PBM_SETRANGE, 0, MAKELPARAM(0, 100)); statusLabel = createLabel(L"", 28, 413, 320, 42, fontSmall); installButton = createButton(L"Установить", BS_OWNERDRAW, IDC_INSTALL, 360, 398, 130, 44); SendMessageW(installButton, WM_SETFONT, reinterpret_cast<WPARAM>(fontBold), TRUE);
    ShowWindow(window, show); UpdateWindow(window);
    MSG message; while (GetMessageW(&message, nullptr, 0, 0)) { TranslateMessage(&message); DispatchMessageW(&message); }
    DeleteObject(fontRegular); DeleteObject(fontSmall); DeleteObject(fontTitle); DeleteObject(fontBold); DeleteObject(windowBrush); DeleteObject(inputBrush); CoUninitialize(); return 0;
}

// Harmless CI-only process fixture. This is not ASSDraw source or a runtime dependency.
#include <windows.h>
#include <string>
#include <cstdio>

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    wchar_t buffer[32768]{};
    DWORD count = GetModuleFileNameW(nullptr, buffer, 32768);
    if (!count || count >= 32768) return 2;
    std::wstring path(buffer, count);
    path.resize(path.find_last_of(L"\\/") + 1);
    path += L"assdraw-started.txt";
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return 3;
    char pid[32]{};
    int length = std::snprintf(pid, sizeof(pid), "%lu", GetCurrentProcessId());
    DWORD written{};
    bool ok = WriteFile(file, pid, static_cast<DWORD>(length), &written, nullptr) && written == static_cast<DWORD>(length);
    FlushFileBuffers(file);
    CloseHandle(file);
    HWND window = CreateWindowExW(0, L"STATIC", L"Yoake external-tool acceptance fixture", WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, 360, 160, nullptr, nullptr, instance, nullptr);
    if (window) ShowWindow(window, SW_SHOWNOACTIVATE);
    ULONGLONG until = GetTickCount64() + 1800;
    MSG message{};
    while (GetTickCount64() < until) {
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        Sleep(10);
    }
    if (window) DestroyWindow(window);
    return ok ? 0 : 4;
}

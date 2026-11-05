#define UNICODE
#define _UNICODE
#include <windows.h>

static WCHAR folder[32768], application[32768];
static STARTUPINFOW startup;
static PROCESS_INFORMATION process;

// Native GUI launcher: no console and no extra runtime. Resolve relative to this EXE,
// so moving the extracted folder or starting from a shortcut keeps working.
void WINAPI Start(void)
{
    DWORD length = GetModuleFileNameW(NULL, folder, 32768);
    if (!length || length >= 32700) goto error;
    while (length && folder[length - 1] != L'\\') --length;
    if (!length) goto error;
    folder[length] = 0;
    lstrcpyW(application, folder);
    lstrcatW(application, L"app\\BlueforsAvalonia.exe");
    startup.cb = sizeof(startup);
    if (!CreateProcessW(application, NULL, NULL, NULL, FALSE, 0, NULL, folder, &startup, &process)) goto error;
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    ExitProcess(0);
error:
    MessageBoxW(NULL, L"Please extract the complete ZIP before starting Bluefors Monitor. Keep the app folder beside this launcher.", L"Bluefors Monitor", MB_OK | MB_ICONERROR);
    ExitProcess(1);
}

#include "../.thirdparty/Aurie/Aurie/source/framework/Debugging/debug.hpp"
#include <CommCtrl.h>
#include <thread>
#include <atomic>

// Exercise the actual handler without presenting its confirmation dialog during the test.
HRESULT WINAPI PickCloseLog(const TASKDIALOGCONFIG*, int* button, int*, BOOL*) { *button = IDNO; return S_OK; }
#define TaskDialogIndirect PickCloseLog
#include "../.thirdparty/Aurie/Aurie/source/framework/Debugging/debug.cpp"
#undef TaskDialogIndirect

static HANDLE returned;
static BOOL WINAPI Handle(DWORD control) {
    BOOL result = Aurie::Internal::DbgpConsoleEventHandler(control);
    SetEvent(returned);
    return result;
}

int main() {
    // Started in its own hidden console. Never attaches to the game or the shell console.
    HWND window = GetConsoleWindow();
    if (!window) return 10;
    Aurie::Internal::DbgpCreateConsole("StoneForge console regression test");
    Aurie::Internal::DbgpInitLogger();
    std::atomic<bool> logError = false;
    spdlog::default_logger()->set_error_handler([&](const std::string&) { logError = true; });
    returned = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    SetConsoleCtrlHandler(Handle, TRUE);
    std::atomic<bool> running = true;
    std::thread writer([&] {
        while (running) {
            Aurie::DbgPrintEx(Aurie::LOG_SEVERITY_INFO, "Writing while the log closes");
            Sleep(1);
        }
    });
    if (!GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)) { running = false; writer.join(); return 11; }
    DWORD wait = WaitForSingleObject(returned, 5000);
    running = false;
    if (wait != WAIT_OBJECT_0) { writer.detach(); return 12; }
    writer.join();
    if (IsWindowVisible(window) || GetConsoleWindow() != window) return 13;
    Aurie::DbgPrintEx(Aurie::LOG_SEVERITY_INFO, "LOG_AFTER_CLOSE");
    CloseHandle(returned);
    return logError ? 14 : 0;
}

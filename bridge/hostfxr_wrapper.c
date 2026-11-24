// hostfxr.dll wrapper for Melvor Idle 2 — bootstraps the MCP bridge into the
// game's .NET runtime.
//
// Why: the game is a Godot 4 export template; Godot hosts CoreCLR through
// hostfxr_initialize_for_dotnet_command_line + a load_assembly_and_get_
// function_pointer delegate (gd_mono.cpp). That hosting mode ignores
// DOTNET_STARTUP_HOOKS (verified experimentally, see HookHostTest/), so the
// old WINMM-proxy env-var injection could never work.
//
// How: Godot probes exactly <data_dir>\hostfxr.dll (find_hostfxr). This
// wrapper is installed under that name. Every export is an asm tail-jump
// thunk (hostfxr_thunks.asm) into the real runtime host — kept right next
// to the wrapper as hostfxr_clr.dll (the original file renamed in place, so
// its own hostpolicy/coreclr resolution is unaffected) — except
// hostfxr_get_runtime_delegate, which is real C code: when Godot asks for
// the load_assembly delegate, the wrapper first lets the real hostfxr
// produce it, then reuses that very delegate to load Melvor2BridgeHook.dll
// and call StartupHook::Bootstrap — which starts the bridge HTTP server on
// 127.0.0.1:17817. Nothing else runs in this DLL.
//
// Install (see InstallBridge.ps1):
//   <data_dir>\hostfxr_clr.dll         original hostfxr, renamed in place
//   <data_dir>\hostfxr.dll             this wrapper
//   <game_root>\Melvor2BridgeHook.dll  managed bridge
// A Steam "verify integrity" restores the original <data_dir>\hostfxr.dll
// (over the wrapper); the game keeps working, only the bridge disappears
// (re-run install).
//
// Build: gcc -shared -O2 -o hostfxr_wrapper.dll hostfxr_wrapper.c \
//            hostfxr_thunks.asm hostfxr_wrapper.def

#include <windows.h>
#include <stdio.h>

typedef void *hostfxr_handle;
typedef int hostfxr_delegate_type;
typedef int (WINAPI *getdeg_fn)(const hostfxr_handle, hostfxr_delegate_type, void **);
typedef int (WINAPI *load_asm_fn)(const wchar_t *, const wchar_t *, const wchar_t *,
                                  void *, void *, void **);

// hostfxr_delegate_type from hostfxr.h
#define HDT_LOAD_ASSEMBLY_AND_GET_FUNCTION_POINTER 5
#define UNMANAGEDCALLERSONLY_METHOD ((const wchar_t *) -1)

static HMODULE s_self;
static HMODULE s_real;
static getdeg_fn s_real_getdeg;
static volatile LONG s_booted;

static void WriteLog(const char *msg)
{
    char path[MAX_PATH];
    DWORD n = GetEnvironmentVariableA("APPDATA", path, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return;
    strncat(path, "\\Games By Malcs\\Melvor Idle 2\\logs\\hostfxr_wrapper.log", MAX_PATH - strlen(path) - 1);
    FILE *f = fopen(path, "a");
    if (!f) return;
    SYSTEMTIME st;
    GetLocalTime(&st);
    fprintf(f, "%04d-%02d-%02d %02d:%02d:%02d %s\n",
            st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, msg);
    fclose(f);
}

// The real hostfxr sits next to this wrapper as hostfxr_clr.dll (the
// original file, renamed in place — same directory, so its self-contained
// hostpolicy/coreclr resolution is untouched). Loaded by full path, no
// loader search involved.
static void EnsureReal(void)
{
    if (s_real) return;
    wchar_t dir[MAX_PATH], real[MAX_PATH];
    DWORD n = GetModuleFileNameW(s_self, dir, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return;
    for (DWORD i = n; i > 0; i--) {
        if (dir[i - 1] == L'\\' || dir[i - 1] == L'/') { dir[i] = 0; break; }
    }
    _snwprintf(real, MAX_PATH, L"%s%s", dir, L"hostfxr_clr.dll");
    s_real = LoadLibraryW(real);
    if (!s_real) {
        WriteLog("EnsureReal: LoadLibraryW(hostfxr_clr.dll) failed");
        return;
    }
    s_real_getdeg = (getdeg_fn) GetProcAddress(s_real, "hostfxr_get_runtime_delegate");
    if (!s_real_getdeg)
        WriteLog("EnsureReal: real hostfxr lacks hostfxr_get_runtime_delegate");
}

// Called from the asm thunks (hostfxr_thunks.asm) with an export name;
// returns the real hostfxr export address, or NULL.
void *hostfxr_resolve(const char *name)
{
    EnsureReal();
    if (!s_real) return NULL;
    return GetProcAddress(s_real, name);
}

// Bootstrap the managed bridge hook through the delegate Godot just asked
// for. Runs once, on Godot's own init thread, before any game code loads —
// the hook only starts a listener thread, so it returns immediately.
static void PiggybackBridge(load_asm_fn load)
{
    wchar_t dir[MAX_PATH], hook[MAX_PATH];
    DWORD n = GetModuleFileNameW(s_self, dir, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return;
    for (DWORD i = n; i > 0; i--) {
        if (dir[i - 1] == L'\\' || dir[i - 1] == L'/') { dir[i] = 0; break; }
    }
    for (DWORD i = (DWORD) wcslen(dir) - 1; i > 0; i--) {
        if (dir[i - 1] == L'\\' || dir[i - 1] == L'/') { dir[i] = 0; break; }
    }
    _snwprintf(hook, MAX_PATH, L"%s%s", dir, L"Melvor2BridgeHook.dll");

    void *boot = NULL;
    int rc = load(hook, L"StartupHook, Melvor2BridgeHook", L"Bootstrap",
                  (void *) UNMANAGEDCALLERSONLY_METHOD, NULL, &boot);
    wchar_t wmsg[300];
    if (rc == 0 && boot) {
        ((void (WINAPI *)(void)) boot)();
        _snwprintf(wmsg, 300, L"bridge bootstrapped via delegate: %ls", hook);
    } else {
        _snwprintf(wmsg, 300, L"PiggybackBridge failed rc=0x%x boot=%p hook=%ls",
                   (unsigned) rc, boot, hook);
    }
    char mb[900];
    int len = WideCharToMultiByte(CP_UTF8, 0, wmsg, -1, mb, sizeof(mb), NULL, NULL);
    WriteLog(len > 0 ? mb : "PiggybackBridge (log conversion failed)");
}

__declspec(dllexport) int WINAPI wrapper_hostfxr_get_runtime_delegate(
        const hostfxr_handle cxt, hostfxr_delegate_type type, void **delegate)
{
    EnsureReal();
    if (!s_real_getdeg) return -2147450749; // E_FAIL-ish; never expected
    int rc = s_real_getdeg(cxt, type, delegate);
    char dbg[120];
    _snprintf(dbg, sizeof(dbg), "get_runtime_delegate type=%d rc=0x%x out=%p",
              type, (unsigned) rc, delegate ? *delegate : NULL);
    WriteLog(dbg);
    if (rc == 0 && delegate && *delegate &&
            type == HDT_LOAD_ASSEMBLY_AND_GET_FUNCTION_POINTER &&
            InterlockedCompareExchange(&s_booted, 1, 0) == 0) {
        PiggybackBridge((load_asm_fn) *delegate);
    }
    return rc;
}

BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID reserved)
{
    if (reason == DLL_PROCESS_ATTACH) {
        s_self = (HMODULE) hinst;
        DisableThreadLibraryCalls(hinst);
    }
    return TRUE;
}

/* Subnautica-only bootstrap derived from UnityDoorstop 3.4.0.0 (CC0).
 * Hooks Unity Mono startup; no root INI, IL2CPP or alternate runtimes.
 */
#include "winapi_util.h"
#include "mono.h"
#include "hook.h"
#include "proxy.h"

static wchar_t *target;
static BOOL initialized;
static void *start_mono(const char *name, const char *version) {
    void *domain = mono_jit_init_version(name, version);
    if (!domain || initialized) return domain;
    initialized = TRUE;
    mono_thread_set_main(mono_thread_current());
    wchar_t *exe = NULL;
    size_t length = get_module_path(NULL, &exe, NULL, STR_LEN(L".config"));
    wchar_t *folder = get_folder_name(exe, length, TRUE);
    if (mono_domain_set_config) {
        wmemcpy(exe + length, L".config", STR_LEN(L".config"));
        char *base = narrow(folder);
        char *config = narrow(exe);
        mono_domain_set_config(domain, base, config);
        free(base); free(config);
    }
    char *path = narrow(target);
    void *assembly = mono_domain_assembly_open(domain, path);
    if (assembly) {
        void *image = mono_assembly_get_image(assembly);
        void *desc = mono_method_desc_new("SNSRModLoader.Bootstrap.Entrypoint:Main()", TRUE);
        void *method = mono_method_desc_search_in_image(desc, image);
        if (method) {
            void *error = NULL;
            mono_runtime_invoke(method, NULL, NULL, &error);
            if (error) MessageBoxW(NULL, L"SNSRModLoader bootstrap failed before managed logging was available.", L"SNSRModLoader", MB_OK | MB_ICONWARNING);
        }
        mono_method_desc_free(desc);
    }
    free(path); free(exe); free(folder);
    return domain;
}
static void *WINAPI resolve_mono(HMODULE module, const char *name) {
    if ((ULONG_PTR)name > 0xFFFF && lstrcmpA(name, "mono_jit_init_version") == 0) {
        load_mono_functions(module);
        return start_mono;
    }
    return (void *)GetProcAddress(module, name);
}
BOOL WINAPI DllEntry(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    h_heap = GetProcessHeap();
    wchar_t system[MAX_PATH];
    UINT size = GetSystemDirectoryW(system, MAX_PATH);
    if (!size || size + STR_LEN(L"\\winhttp.dll") >= MAX_PATH) return FALSE;
    wmemcpy(system + size, L"\\winhttp.dll", STR_LEN(L"\\winhttp.dll"));
    HMODULE original = LoadLibraryW(system);
    if (!original) return FALSE;
    load_functions(original);
    wchar_t *exe = NULL;
    size_t length = get_module_path(NULL, &exe, NULL, 0);
    wchar_t *file = get_file_name(exe, length, TRUE);
    if (lstrcmpiW(file, L"Subnautica.exe") != 0) { free(file); free(exe); return TRUE; }
    wchar_t *folder = get_folder_name(exe, length, TRUE);
    size_t folder_length = wcslen(folder);
    const wchar_t suffix[] = L"\\Mods\\Loader\\SNSRModLoader.Bootstrap.dll";
    target = calloc(folder_length + STR_LEN(suffix), sizeof(wchar_t));
    wmemcpy(target, folder, folder_length);
    wmemcpy(target + folder_length, suffix, STR_LEN(suffix));
    if (file_exists(target) && GetEnvironmentVariableW(L"SNSR_DISABLE", NULL, 0) == 0) {
        HMODULE player = GetModuleHandleW(L"UnityPlayer.dll");
        if (!player) player = GetModuleHandleW(NULL);
        if (!iat_hook(player, "kernel32.dll", &GetProcAddress, &resolve_mono))
            MessageBoxW(NULL, L"SNSRModLoader could not install the Unity Mono startup hook.", L"SNSRModLoader", MB_OK | MB_ICONWARNING);
    }
    free(file); free(folder); free(exe);
    return TRUE;
}

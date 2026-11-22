#pragma once
#include <windows.h>

/* Mono embedding functions used by the Subnautica-only bootstrap. */
void *(*mono_jit_init_version)(const char *, const char *);
void *(*mono_thread_current)(void);
void (*mono_thread_set_main)(void *);
void (*mono_domain_set_config)(void *, const char *, const char *);
void *(*mono_domain_assembly_open)(void *, const char *);
void *(*mono_assembly_get_image)(void *);
void *(*mono_method_desc_new)(const char *, int);
void *(*mono_method_desc_search_in_image)(void *, void *);
void (*mono_method_desc_free)(void *);
void *(*mono_runtime_invoke)(void *, void *, void **, void **);
inline void load_mono_functions(HMODULE module) {
#define MONO(name) name = (void *)GetProcAddress(module, #name)
    MONO(mono_jit_init_version);
    MONO(mono_thread_current);
    MONO(mono_thread_set_main);
    MONO(mono_domain_set_config);
    MONO(mono_domain_assembly_open);
    MONO(mono_assembly_get_image);
    MONO(mono_method_desc_new);
    MONO(mono_method_desc_search_in_image);
    MONO(mono_method_desc_free);
    MONO(mono_runtime_invoke);
#undef MONO
}

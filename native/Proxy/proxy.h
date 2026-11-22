#pragma once
#include <windows.h>
/* Generated forwarding thunks call the original system WinHTTP functions. */
extern void load_functions(HMODULE dll);

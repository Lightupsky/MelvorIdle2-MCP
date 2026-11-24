# x64 forwarding thunks for the hostfxr wrapper.
#
# Each thunk saves the Win64 volatile argument registers (RCX/RDX/R8/R9 +
# XMM0-3), calls the C helper `hostfxr_resolve(name)` which LoadLibrary's the
# real hostfxr (hostfxr_clr.dll, same directory as this wrapper) by full path
# and GetProcAddress's the named export, restores the registers, then TAIL
# JUMPS to the real function. A tail jump keeps the caller's return address
# and stack arguments (5th+ params) intact, so signatures and return values
# pass through unmodified — a plain C void(void) relay would clobber the
# argument registers (this exact bug killed Melvor2.exe with c0000005 in an
# earlier winmm proxy revision).
#
# If resolution fails the thunk returns 0x80008083
# (CoreHostLibMissingFailure); every hostfxr API returns an HRESULT-style
# int, so this is a sane failure path.

.intel_syntax noprefix

.macro THUNK name
    .text
    .globl thunk_\name
thunk_\name:
    sub     rsp, 0x88                  # arg saves + helper shadow space
    mov     [rsp+0x20], rcx
    mov     [rsp+0x28], rdx
    mov     [rsp+0x30], r8
    mov     [rsp+0x38], r9
    movdqu  [rsp+0x40], xmm0
    movdqu  [rsp+0x50], xmm1
    movdqu  [rsp+0x60], xmm2
    movdqu  [rsp+0x70], xmm3
    lea     rcx, [rip + namestr_\name]
    call    hostfxr_resolve
    mov     rcx, [rsp+0x20]
    mov     rdx, [rsp+0x28]
    mov     r8,  [rsp+0x30]
    mov     r9,  [rsp+0x38]
    movdqu  xmm0, [rsp+0x40]
    movdqu  xmm1, [rsp+0x50]
    movdqu  xmm2, [rsp+0x60]
    movdqu  xmm3, [rsp+0x70]
    add     rsp, 0x88
    test    rax, rax
    jz      9f
    jmp     rax
9:  mov     eax, 0x80008083
    ret
    .section .rdata
namestr_\name:
    .asciz "\name"
    .text
.endm

THUNK hostfxr_close
THUNK hostfxr_get_available_sdks
THUNK hostfxr_get_dotnet_environment_info
THUNK hostfxr_get_native_search_directories
THUNK hostfxr_get_runtime_properties
THUNK hostfxr_get_runtime_property_value
THUNK hostfxr_initialize_for_dotnet_command_line
THUNK hostfxr_initialize_for_runtime_config
THUNK hostfxr_main
THUNK hostfxr_main_bundle_startupinfo
THUNK hostfxr_main_startupinfo
THUNK hostfxr_resolve_frameworks_for_runtime_config
THUNK hostfxr_resolve_sdk
THUNK hostfxr_resolve_sdk2
THUNK hostfxr_run_app
THUNK hostfxr_set_error_writer
THUNK hostfxr_set_runtime_property_value

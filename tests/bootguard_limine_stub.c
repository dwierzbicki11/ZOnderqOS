/*
 * Test-only EFI chainload target. Replaces Limine on a disposable FAT32 image
 * to record the selected one-shot boot slot and deliberately reboot.
 * Never deploy this test executable on real hardware.
 */
#include <efi.h>
#include <efilib.h>

static EFI_GUID bli_guid = {
    0x4a67b082,0x0a4c,0x41cf,{0xb6,0xc7,0x44,0x0b,0x29,0xbb,0x8c,0x4f}
};

static void debug_char(char c) {
#if defined(__x86_64__)
    __asm__ __volatile__("outb %0, %1" : : "a"((unsigned char)c), "Nd"((unsigned short)0xe9));
#endif
}

static void debug_line(const char *s) {
    for (; *s; s++) debug_char(*s);
}

EFI_STATUS efi_main(EFI_HANDLE image, EFI_SYSTEM_TABLE *table) {
    InitializeLib(image, table);
    CHAR16 chosen[64];
    UINTN bytes = sizeof(chosen);
    UINT32 flags = 0;
    EFI_STATUS result = uefi_call_wrapper(RT->GetVariable, 5,
        L"LoaderEntryOneShot", &bli_guid, &flags, &bytes, chosen);
    if (EFI_ERROR(result)) {
        debug_line("ZQBOOT:NONE\n");
    } else if (StrCmp(chosen, L"ZonderqOS Slot A") == 0) {
        debug_line("ZQBOOT:A\n");
    } else if (StrCmp(chosen, L"ZonderqOS Slot B") == 0) {
        debug_line("ZQBOOT:B\n");
    } else {
        debug_line("ZQBOOT:UNKNOWN\n");
    }
    uefi_call_wrapper(BS->Stall, 1, (UINTN)300000);
    uefi_call_wrapper(RT->ResetSystem, 4, EfiResetCold, EFI_SUCCESS, 0, NULL);
    for (;;) { }
}

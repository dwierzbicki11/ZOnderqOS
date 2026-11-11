/*
 * ZonderqOS EFI BootGuard, x86_64 UEFI only.
 *
 * Runs BEFORE Limine and BEFORE a potentially corrupted kernel ELF. Scans
 * ONLY FAT volumes labelled ZONDERQDATA, and requires a magic boot-state
 * header plus BOOTGUARD_PREBOOT=1. Writes a single ASCII digit to the boot
 * attempt / fallback flag, flushes it, sets a one-shot Limine entry via the
 * Boot Loader Interface, then chainloads the original Limine EFI executable.
 *
 * No automatic install: EFI replacement must be staged on an isolated test
 * image, validated on QEMU/OVMF and approved before real hardware deployment.
 * The old Limine BOOTX64.EFI must be preserved as LIMINE.EFI.
 */
#include <efi.h>
#include <efilib.h>
#include "bootguard_policy.h"
#include "bootguard_elf.h"

#define BOOT_STATE_PATH L"\\boot\\boot_state.cfg"
#define LIMINE_EFI_PATH L"\\EFI\\BOOT\\LIMINE.EFI"
#define STATE_LIMIT 4096

static EFI_GUID bli_guid = {
    0x4a67b082, 0x0a4c, 0x41cf,
    {0xb6, 0xc7, 0x44, 0x0b, 0x29, 0xbb, 0x8c, 0x4f}
};

static int key_equal(const CHAR8 *line, UINTN len, const char *key) {
    UINTN i = 0;
    while (key[i] != 0 && i < len && line[i] == (CHAR8)key[i]) i++;
    return key[i] == 0 && i < len && line[i] == '=';
}

/* Find one specific key, refusing duplicates and multi-line values. */
static int find_value(const CHAR8 *data, UINTN length, const char *key,
                      UINTN *offset, UINTN *size) {
    UINTN start = 0;
    int matches = 0;
    while (start < length) {
        UINTN end = start;
        while (end < length && data[end] != '\n' && data[end] != '\r') end++;
        if (key_equal(data + start, end - start, key)) {
            UINTN keylen = 0;
            while (key[keylen]) keylen++;
            *offset = start + keylen + 1;
            *size = end - *offset;
            matches++;
        }
        start = end;
        while (start < length && (data[start] == '\n' || data[start] == '\r')) start++;
    }
    return matches == 1;
}

static int read_char_field(const CHAR8 *data, UINTN length,
                           const char *key, char *value) {
    UINTN at=0, n=0;
    if (!find_value(data,length,key,&at,&n) || n != 1) return 0;
    *value = (char)data[at];
    return 1;
}

static int read_state(const CHAR8 *data, UINTN length,
                      zq_boot_state *s) {
    const CHAR8 magic[] = "# ZonderqOS A/B Bootloader State Configuration";
    UINTN i=0, pos=0, n=0;
    if (length < sizeof(magic)-1) return 0;
    for (i=0; i<sizeof(magic)-1; i++)
        if (data[i] != magic[i]) return 0;
    if (!read_char_field(data,length,"ACTIVE_SLOT",&s->active) ||
        !read_char_field(data,length,"FALLBACK_SLOT",&s->fallback))
        return 0;

    if (!find_value(data,length,"PENDING_SLOT",&pos,&n) || n > 1)
        return 0;
    s->pending = n ? (char)data[pos] : 0;
    char digit;
    if (!read_char_field(data,length,"BOOT_ATTEMPTS",&digit) ||
        digit < '0' || digit > '3')
        return 0;
    s->attempts = (unsigned)(digit - '0');
    if (!read_char_field(data,length,"BOOTGUARD_PREBOOT",&digit))
        return 0;
    s->enabled = digit == '1';
    if (!read_char_field(data,length,"BOOTGUARD_FALLBACK",&digit) ||
        (digit != '0' && digit != '1'))
        return 0;
    s->recovery_requested = digit == '1';
    return 1;
}

static int is_labelled_volume(EFI_FILE_HANDLE root) {
    /* EFI_FILE_SYSTEM_INFO is variable-sized, aligned scratch buffer. */
    UINT64 info[128];
    UINTN size = sizeof(info);
    EFI_STATUS status = uefi_call_wrapper(root->GetInfo, 4, root,
                                          &gEfiFileSystemInfoGuid,
                                          &size, (VOID*)info);
    if (EFI_ERROR(status)) return 0;
    EFI_FILE_SYSTEM_INFO *fs = (EFI_FILE_SYSTEM_INFO*)info;
    return StrCmp(fs->VolumeLabel, L"ZONDERQDATA") == 0;
}

/* Keep the selected volume root open for on-disk ELF verification.
 * Refuse ambiguous ZONDERQDATA state volumes (never pick a random disk).
 */
static EFI_STATUS open_state(EFI_FILE_HANDLE *result, EFI_FILE_HANDLE *selected_root) {
    EFI_HANDLE *volumes = NULL;
    UINTN count = 0;
    EFI_STATUS status = uefi_call_wrapper(
        BS->LocateHandleBuffer, 5, ByProtocol,
        &gEfiSimpleFileSystemProtocolGuid, NULL, &count, &volumes);
    if (EFI_ERROR(status)) return status;

    *result = NULL;
    *selected_root = NULL;
    UINTN found = 0;
    for (UINTN i = 0; i < count; i++) {
        EFI_SIMPLE_FILE_SYSTEM_PROTOCOL *fs = NULL;
        EFI_FILE_HANDLE root = NULL, file = NULL;
        status = uefi_call_wrapper(BS->HandleProtocol, 3, volumes[i],
                                   &gEfiSimpleFileSystemProtocolGuid, (VOID**)&fs);
        if (EFI_ERROR(status) || fs == NULL) continue;
        status = uefi_call_wrapper(fs->OpenVolume, 2, fs, &root);
        if (EFI_ERROR(status) || root == NULL) continue;
        if (is_labelled_volume(root)) {
            status = uefi_call_wrapper(root->Open, 5, root, &file,
                    BOOT_STATE_PATH, EFI_FILE_MODE_READ|EFI_FILE_MODE_WRITE, 0);
            if (!EFI_ERROR(status) && file != NULL) {
                found++;
                if (found == 1) {
                    *result = file;
                    *selected_root = root;
                    root = NULL; /* retained by caller */
                } else {
                    uefi_call_wrapper(file->Close, 1, file);
                }
            }
        }
        if (root != NULL)
            uefi_call_wrapper(root->Close, 1, root);
    }
    uefi_call_wrapper(BS->FreePool, 1, volumes);
    if (found != 1) {
        if (*result != NULL) {
            uefi_call_wrapper((*result)->Close, 1, *result);
            *result = NULL;
        }
        if (*selected_root != NULL) {
            uefi_call_wrapper((*selected_root)->Close, 1, *selected_root);
            *selected_root = NULL;
        }
        return EFI_NOT_FOUND;
    }
    return EFI_SUCCESS;
}

/* Read the real image from the unique managed volume, not a host-supplied
 * test byte array or the merely configured Limine path.
 */
static int slot_has_valid_elf(EFI_FILE_HANDLE root, char slot) {
    if (root == NULL || (slot != 'A' && slot != 'B')) return 0;
    CHAR16 *path = slot == 'A'
        ? L"\\boot\\slot_a\\ZonderqOS.elf"
        : L"\\boot\\slot_b\\ZonderqOS.elf";
    EFI_FILE_HANDLE file = NULL;
    EFI_STATUS status = uefi_call_wrapper(root->Open, 5, root, &file,
                                          path, EFI_FILE_MODE_READ, 0);
    if (EFI_ERROR(status) || file == NULL) return 0;

    UINT64 buffer[128];
    UINTN info_length = sizeof(buffer);
    status = uefi_call_wrapper(file->GetInfo, 4, file,
                               &gEfiFileInfoGuid, &info_length, (VOID*)buffer);
    int valid = 0;
    if (!EFI_ERROR(status)) {
        EFI_FILE_INFO *info = (EFI_FILE_INFO *)buffer;
        if (info->FileSize >= 120u && info->FileSize <= (UINT64)512*1024*1024) {
            UINT8 header[64];
            UINTN length = sizeof(header);
            status = uefi_call_wrapper(file->Read, 3, file, &length, header);
            if (!EFI_ERROR(status) && length == sizeof(header))
                valid = zq_elf64_x64_header_valid(header, info->FileSize);
        }
    }
    uefi_call_wrapper(file->Close, 1, file);
    return valid;
}

static int write_digit(EFI_FILE_HANDLE file, const CHAR8 *data, UINTN length,
                       const char *key, char value) {
    UINTN pos=0, n=0, size=1;
    if (!find_value(data,length,key,&pos,&n) || n!=1) return 0;
    EFI_STATUS status = uefi_call_wrapper(file->SetPosition, 2, file, (UINT64)pos);
    if (EFI_ERROR(status)) return 0;
    CHAR8 digit = (CHAR8)value;
    status = uefi_call_wrapper(file->Write, 3, file, &size, &digit);
    if (EFI_ERROR(status) || size != 1) return 0;
    status = uefi_call_wrapper(file->Flush, 1, file);
    return !EFI_ERROR(status);
}

static EFI_STATUS configure_one_shot(char slot) {
    /* Stable Limine entry names are defined by BootSlotManager.SyncLimineConfig.
       These are Boot Loader Interface entry paths, not user-controlled data. */
    CHAR16 *entry = slot == 'A' ? L"ZonderqOS Slot A" : L"ZonderqOS Slot B";
    UINTN length = (StrLen(entry) + 1) * sizeof(CHAR16);
    return uefi_call_wrapper(RT->SetVariable, 5,
        L"LoaderEntryOneShot", &bli_guid,
        EFI_VARIABLE_BOOTSERVICE_ACCESS | EFI_VARIABLE_RUNTIME_ACCESS,
        length, entry);
}

static EFI_STATUS chainload_limine(EFI_HANDLE image) {
    EFI_LOADED_IMAGE_PROTOCOL *loaded = NULL;
    EFI_STATUS status = uefi_call_wrapper(
        BS->HandleProtocol, 3, image, &gEfiLoadedImageProtocolGuid,
        (VOID**)&loaded);
    if (EFI_ERROR(status) || loaded == NULL) return EFI_NOT_FOUND;
    EFI_DEVICE_PATH *path = FileDevicePath(loaded->DeviceHandle, LIMINE_EFI_PATH);
    if (path == NULL) return EFI_OUT_OF_RESOURCES;
    EFI_HANDLE child = NULL;
    status = uefi_call_wrapper(BS->LoadImage, 6, FALSE, image, path,
                               NULL, 0, &child);
    if (!EFI_ERROR(status))
        status = uefi_call_wrapper(BS->StartImage, 3, child, NULL, NULL);
    FreePool(path);
    return status;
}

EFI_STATUS efi_main(EFI_HANDLE image, EFI_SYSTEM_TABLE *system_table) {
    InitializeLib(image, system_table);
    Print(L"ZonderqOS EFI BootGuard: pre-kernel A/B selector\r\n");

    EFI_FILE_HANDLE file = NULL, volume_root = NULL;
    EFI_STATUS status = open_state(&file, &volume_root);
    if (!EFI_ERROR(status) && file != NULL && volume_root != NULL) {
        CHAR8 data[STATE_LIMIT];
        UINTN length = sizeof(data);
        status = uefi_call_wrapper(file->Read, 3, file, &length, data);
        zq_boot_state state = {0,0,0,0,0,0};
        char chosen = 0;
        int dirty = 0;
        int valid = !EFI_ERROR(status) && length < STATE_LIMIT &&
            read_state(data,length,&state);
        if (valid) {
            /*
             * Candidate may be corrupt before any C# code starts. Only select
             * a slot after inspecting its real on-disk ELF structure.
             *
             * When pending is broken, force the normal policy into fallback
             * mode without consuming three unnecessary boot attempts.
             * Write BOOTGUARD_FALLBACK=1 so the previous kernel restores the
             * matching shared /system and /usr payload at early boot.
             */
            if (state.pending) {
                if (!slot_has_valid_elf(volume_root, state.fallback)) {
                    Print(L"BootGuard: fallback image invalid or missing; refusing chainload\r\n");
                    uefi_call_wrapper(file->Close, 1, file);
                    uefi_call_wrapper(volume_root->Close, 1, volume_root);
                    return EFI_LOAD_ERROR;
                }
                if (!slot_has_valid_elf(volume_root, state.pending)) {
                    Print(L"BootGuard: corrupt pending ELF; switching directly to fallback\r\n");
                    state.attempts = 3;
                }
            } else if (!slot_has_valid_elf(volume_root, state.active)) {
                Print(L"BootGuard: active image invalid; refusing chainload\r\n");
                uefi_call_wrapper(file->Close, 1, file);
                uefi_call_wrapper(volume_root->Close, 1, volume_root);
                return EFI_LOAD_ERROR;
            }
        }

        if (valid && zq_boot_decide(&state,&chosen,&dirty)) {
            int durable = 1;
            if (dirty) {
                if (state.recovery_requested)
                    durable = write_digit(file,data,length,
                                          "BOOTGUARD_FALLBACK",'1');
                else
                    durable = write_digit(file,data,length,
                                          "BOOT_ATTEMPTS", (char)('0'+state.attempts));
            }
            if (durable) {
                status = configure_one_shot(chosen);
                if (EFI_ERROR(status))
                    Print(L"BootGuard: one-shot selection not supported; Limine safe default\r\n");
                else
                    Print(L"BootGuard: choosing slot %c (attempts=%d)\r\n",
                          chosen, state.attempts);
            } else
                Print(L"BootGuard: state write failed; Limine safe default\r\n");
        } else
            Print(L"BootGuard: invalid state; Limine safe default\r\n");
        uefi_call_wrapper(file->Close, 1, file);
        uefi_call_wrapper(volume_root->Close, 1, volume_root);
    } else
        Print(L"BootGuard: no unique armed ZONDERQDATA; Limine default\r\n");

    status = chainload_limine(image);
    Print(L"BootGuard: LIMINE.EFI exited, status=%r\r\n", status);
    return status;
}

# Cosmos Gen3 keyboard event API compatibility

- 3.0.85: `Cosmos.Kernel.System.Keyboard.KeyEvent`, `ConsoleKeyEx`, `KeyboardManager`.
- 3.0.94: `Cosmos.Kernel.System.Input.KeyEvent`, `Key`, `KeyboardManager`.
- The members of the renamed key enum remain aligned in these two releases.
- `source/Common/CosmosKeyboardApiCompat.cs` selects the namespace and key alias centrally. Remove direct legacy namespace imports from every GUI source. The callbacks use the original Cosmos `KeyEvent`, not custom conversion objects.

`ZonderqCosmosKeyboardApi=Legacy` remains set during Cosmos 3.0.85 SMP recovery.
Switch it to `Input` **together with the complete patched runtime and SDK**
when the Stage 23 port to 3.0.94 is ready. The property can also be
overridden with `-p:ZonderqCosmosKeyboardApi=Input` for an isolated
3.0.94 compatibility compilation. Never mix this with 3.0.85 packages.

## Runtime acceptance checklist

When SMP/runtime migration is completed, run keyboard checks in QEMU:
PS/2/virtio/USB press, navigation keys, Shift/CapsLock, Ctrl+Alt+Del,
Ctrl+Alt+L, terminal editing and history, Notepad, Nano, login/lock
screens and focus/event routing. Current PR does not claim Cosmos
3.0.94 ISO or SMP passed; the patched 3.0.94 feed is still outstanding.

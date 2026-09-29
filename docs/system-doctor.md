# System Doctor

`sysdoctor` is a read-only health-check command for ZOnderqOS Gen 3.

## Checks

- page allocator invariants
- scheduler online CPU/thread-slot sanity
- block-device inventory
- root filesystem and core system directories
- authenticated security-context consistency
- persistent system logger state
- active process-registry entry validity

## Usage

```text
sysdoctor
sysdoctor --strict
```

Normal mode fails only when a hard invariant fails. Strict mode also returns command failure when warnings are present.

## Safety contract

System Doctor must not repair state automatically. It only observes and reports. Repairs belong to explicit administrative commands so a diagnostic check can never mutate the kernel while investigating a fault.

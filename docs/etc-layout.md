# `/etc` configuration in ZonderqOS Gen 3

These files are created when missing and are used by the running system. Existing
files are preserved across boots. Root can edit text configuration with `nano`.

| Path | Used for |
| --- | --- |
| `/etc/hostname` | Persistent hostname shown in the shell prompt and `hostname`. After editing manually, run `hostname reload`. |
| `/etc/issue` | Text displayed before a console login. |
| `/etc/motd` | Text displayed after a successful console login. |
| `/etc/os-release` | Human-readable Gen 3 identity for tools and inspection. |
| `/etc/profile` | Existing shell environment exports, loaded during boot. |
| `/etc/zservices/*.conf` | Background service definitions loaded at boot or with `service reload`. |
| `/etc/zonderq/settings.conf` | Existing GUI/system settings store. |
| `/etc/passwd`, `/etc/shadow`, `/etc/acl.map` | Existing accounts, password hashes and access rules; use account commands rather than editing these by hand. |

`hostname set <name>` updates `/etc/hostname` and the live prompt. It requires
authenticated root. Hostnames contain 1–63 ASCII letters, digits or hyphens and
cannot begin or end with a hyphen.

`/etc/os-release` is identity metadata; editing it does not change kernel
features. Files such as `/etc/fstab` or `/etc/hosts` will be introduced only
when the boot mount and network resolver paths actually consume them.

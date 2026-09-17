# WinDivert 2.2.2-A (x64)

The packet capture driver Crapnet is built on, vendored so a clone builds and runs without a
separate download step.

| File | SHA-256 |
|---|---|
| `x64/WinDivert.dll` | `c1e060ee19444a259b2162f8af0f3fe8c4428a1c6f694dce20de194ac8d7d9a2` |
| `x64/WinDivert64.sys` | `8da085332782708d8767bcace5327a6ec7283c17cfb85e40b03cd2323a90ddc2` |

Both files are unmodified copies from the official release archive
[`WinDivert-2.2.2-A.zip`](https://github.com/basil00/WinDivert/releases/tag/v2.2.2). Only the
64-bit payload is kept: Crapnet targets x64 and the driver must match. The driver is signed by its
author; do not rebuild or alter it, or Windows will refuse to load it.

`Crapnet.App.csproj` copies both files next to `Crapnet.exe` on build and publish.

## Licence

WinDivert is by Basil Fierz and is dual-licensed under LGPLv3 or GPLv2 (see [`LICENSE`](LICENSE)).
Crapnet uses it under LGPLv3 as a separate, dynamically loaded library. Source is available at
<https://github.com/basil00/WinDivert>.

## Updating

1. Download the new `WinDivert-<version>-A.zip` from the upstream releases page.
2. Replace `x64/WinDivert.dll`, `x64/WinDivert64.sys`, `LICENSE` and `VERSION` with the archive's.
3. Update the version and checksums above (`sha256sum x64/*`).
4. Check `WinDivertNative.VerifyLayout` still passes: the address struct layout must match.

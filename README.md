# crapnet

Make a Wi-Fi hotspot on Windows, point an Android device at it, and ruin the connection on purpose.

Crapnet is a network conditioner for testing how an app behaves on a bad link. Windows shares its
Ethernet connection over a hotspot, the device under test connects to it, and every packet the
device sends or receives passes through a rule engine that can drop, delay, duplicate, reorder,
corrupt, rate-limit, stall, reset or blackhole it.

It takes the packet-mangling idea from [clumsy](https://github.com/jagt/clumsy) and the
hotspot-for-a-device idea from [inssidious](https://github.com/shanselman/Inssidious), and adds the
part both leave out: **rules**. Impairments are not global. Each one is scoped to the slice of
traffic you point it at, and each toggles independently while traffic is flowing.

> **Early software.** The rule engine is covered by an extensive test suite, but the Windows side
> (capture driver, Mobile Hotspot, ICS) has had little real-hardware mileage. Bug reports welcome.

---

## Download

Grab the latest `crapnet-<version>-win-x64.zip` from
[**Releases**](https://github.com/vlegeron/crapnet/releases), extract it anywhere and run
`Crapnet.exe` **as administrator**. Everything it needs, including the WinDivert capture driver,
is in the zip. A `.sha256` file is published next to each archive if you want to verify it.

### Requirements

- Windows 10 2004 or later (Windows 11 fine), 64-bit
- A Wi-Fi adapter that supports Mobile Hotspot, **and** a wired connection for the internet
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Administrator rights, needed both to load the capture driver and to share a connection

## Quick start

1. Plug in Ethernet and pick it as the **uplink**.
2. Set a network name and password and hit the power toggle. Windows brings up the hotspot and
   shares the uplink's connection with it.
3. Connect the Android device to that network. It appears in the client list once it has a lease.
4. Pick a preset, or add a rule and switch on the impairments you want.
5. Toggle things while traffic flows. Edits take effect on the next packet.

**Bypass** passes everything through untouched without dropping the device off the network, which
is the quickest way to compare impaired against clean behaviour.

---

## Rules

Every rule matches on six independent dimensions, any of which can be left as "any":

| Dimension | Examples |
|---|---|
| Direction | uplink (from the device), downlink (to it), or both |
| Protocol | TCP, UDP, ICMP, any |
| Device address | `192.168.137.42`, `192.168.137.0/24`, `!192.168.137.9` |
| Remote address | `8.8.8.8`, `93.184.216.0/24`, `1.1.1.1-1.1.1.9` |
| Device port | `51000-52000` |
| Remote port | `443`, `53, 80, 8000-8100`, `!443` |

Addresses and ports are named for **the device** and **the remote peer**, not source and
destination, so one rule reads the same in both directions: "device `.42`, remote port 443" means
that device's HTTPS traffic, whichever way it is flowing.

Rules resolve **first match wins**, like a firewall. A rule with no impairments enabled claims its
traffic and passes it through untouched, so placing one above a broad rule carves out an exception:

```
1  DNS            remote :53      (nothing enabled)   ← stays clean
2  Everything     any             block               ← everything else disappears
```

## Impairments

| | Impairment | Parameters | What it models |
|---|---|---|---|
| ⛔ | **Block** | — | A blackhole. Associated, no traffic. |
| ✂ | **Drop** | chance % | Random loss. |
| ⏱ | **Lag** | delay ms, jitter ms, chance % | Latency, steady or unstable. |
| 🐌 | **Bandwidth** | kbps, burst | A narrow pipe, with queueing delay and congestive loss. |
| ⏸ | **Throttle** | window ms, chance %, drop | Traffic stalls, then arrives all at once. |
| 🔀 | **Reorder** | chance %, max delay ms | Packets arriving out of order. |
| ⧉ | **Duplicate** | chance %, copies | A link that echoes frames. |
| ☣ | **Tamper** | chance %, max bytes, fix checksum | Corruption. Leave the checksum wrong and the peer drops it itself. |
| ⚡ | **Reset** | chance % | TCP connections refused mid-flight. |

Built-in presets: 2G/EDGE, 3G, weak LTE, lossy Wi-Fi, bufferbloat, satellite, dead air, no DNS,
TLS refused, corruption.

## Troubleshooting

| Symptom | Cause |
|---|---|
| "needs to run as administrator" | The driver and sharing APIs both require elevation. |
| "capture driver missing" | `WinDivert.dll` or `WinDivert64.sys` is not next to `Crapnet.exe`. Re-extract the zip and check anti-virus has not quarantined the driver. |
| Hotspot will not start | The adapter or its driver does not support Mobile Hotspot. Check Settings → Mobile hotspot. |
| Device connects but has no internet | The uplink has no route, or another connection share is already configured. |
| Rules have no effect | Check the master toggle is armed and Bypass is off, and that the device's address is inside the subnet shown. |

---

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/vlegeron/crapnet
cd crapnet
dotnet build -c Release
```

WinDivert is vendored in [`third_party/windivert`](third_party/windivert) and copied next to the
executable by the build, so the output folder is ready to run (elevated):

```powershell
./src/Crapnet.App/bin/Release/net8.0-windows10.0.19041.0/Crapnet.exe
```

The core logic is platform-neutral and its tests run on any OS:

```bash
dotnet test tests/Crapnet.Domain.Tests
dotnet test tests/Crapnet.Application.Tests
```

## How it works

Windows Mobile Hotspot is built around sharing one specific connection profile, so handing it the
Ethernet profile is what gives the device its internet; no separate Internet Connection Sharing
step is needed. (ICS is implemented as a fallback where Mobile Hotspot is unavailable.)

Traffic for a tethered client is *routed* by Windows rather than addressed to the machine, so
Crapnet captures at WinDivert's forwarding layer, scoped to the hotspot's subnet so the rest of the
machine is left alone. Packets are classified **uplink** or **downlink** by testing them against
that subnet rather than trusting the driver's inbound/outbound flag, which stops meaning anything
useful once Windows is routing on someone else's behalf.

Every captured packet is held until the engine decides what to do with it, so dropping a packet is
simply a matter of not forwarding it.

### Layering

```
Crapnet.App              Avalonia UI, composition root        net8.0-windows
Crapnet.Infrastructure   WinDivert, WinRT tethering, ICS      net8.0-windows
Crapnet.Application      use cases, ports, impairment engine  net8.0
Crapnet.Domain           rules, impairments, value objects    net8.0
```

Dependencies point inwards only. The two inner layers know nothing about Windows, the capture
driver or the UI: everything platform-specific sits behind a port (`IPacketGateway`,
`IHotspotController`, `IClock`, `IRandomSource`, …) and is injected. Probability and time are both
injected too, so "30% drop" is an exact assertion rather than a flaky one, and a 500 ms delay is
verified without waiting 500 ms.

### Design notes

**Bandwidth is not a token bucket.** It models a link that serialises packets behind a virtual
finish time. Queueing delay emerges from offered load on its own, and packets cannot overtake one
another; a token bucket will happily reorder a TCP stream while rate-limiting it, which quietly
contaminates the thing you were trying to measure.

**Destructive impairments run first.** Block and drop are evaluated before anything that costs
queue budget, so nothing is held for a packet that was going to be discarded. The delaying
impairments then compose by accumulating onto a single release time.

**Rule edits cross threads through one volatile handover.** The capture loop owns all per-rule
state and runs single-threaded, so the hot path needs no locks, and toggling an impairment takes
effect on the next packet without interrupting capture.

**Per-rule state is per direction.** A 1 Mbps cap means 1 Mbps each way, because that is how
people read it and how real links behave.

---

## Credits

[WinDivert](https://github.com/basil00/WinDivert) by Basil Fierz does the packet interception.
[clumsy](https://github.com/jagt/clumsy) and [inssidious](https://github.com/shanselman/Inssidious)
are the reference points this is built against.

## Licence

Crapnet is MIT licensed. WinDivert is redistributed unmodified under LGPLv3; its licence is in
[`third_party/windivert/LICENSE`](third_party/windivert/LICENSE) and ships in every release.

# crapnet

Make a Wi-Fi hotspot on Windows, point an Android device at it, and ruin the connection on purpose.

Crapnet is a network conditioner for testing how an app behaves on a bad link. Windows shares its
Ethernet connection over a hotspot, the device under test connects to it, and every packet the
device sends or receives passes through a rule engine that can drop, delay, duplicate, reorder,
corrupt, rate-limit, stall, reset or blackhole it.

It takes the packet-mangling idea from [clumsy](https://github.com/jagt/clumsy) and the
hotspot-for-a-device idea from [inssidious](https://github.com/shanselman/Inssidious), and adds the
part both leave out: **rules**. Impairments are not global. Each one is scoped to whatever slice of
traffic you point it at, and each toggles independently while traffic is flowing.

---

## What you can target

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
destination. One rule therefore reads the same in both directions: "device `.42`, remote port 443"
means that device's HTTPS traffic, whichever way it happens to be flowing.

Rules resolve **first match wins**, like a firewall list. That gives exceptions for free — a rule
with no impairments enabled claims its traffic and passes it through untouched, so putting one
above a broad rule carves a hole in it:

```
1  DNS            remote :53      (nothing enabled)   ← stays clean
2  Everything     any             block               ← everything else disappears
```

## What you can do to it

Nine impairments, each with its own switch:

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

Presets are included as starting points: 2G/EDGE, 3G, weak LTE, lossy Wi-Fi, bufferbloat,
satellite, dead air, no DNS, TLS refused, corruption.

---

## Requirements

- Windows 10 2004 or later (Windows 11 fine), 64-bit
- A Wi-Fi adapter that supports Mobile Hotspot, **and** a wired connection for the internet
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Administrator rights — required both to load the capture driver and to share a connection

## Setup

```powershell
git clone https://github.com/vlegeron/crapnet
cd crapnet
dotnet build -c Release

# Fetch the capture driver (not vendored: it is a signed kernel driver under LGPLv3)
./scripts/fetch-windivert.ps1 -Destination src/Crapnet.App/bin/Release/net8.0-windows10.0.19041.0

# Run elevated
./src/Crapnet.App/bin/Release/net8.0-windows10.0.19041.0/Crapnet.exe
```

Prebuilt binaries are produced by CI on every push; grab the `crapnet-win-x64` artifact.

## Using it

1. Plug in Ethernet and pick it as the **uplink**.
2. Set a network name and password, hit the power toggle. Windows brings up the hotspot and
   shares the uplink's connection with it.
3. Connect the Android device to that network. It appears in the client list once it has a lease.
4. Pick a preset, or add a rule and switch on the impairments you want.
5. Toggle things while traffic flows. Edits reach the engine on the next packet.

**Bypass** passes everything through untouched without detaching from the network, which is the
quickest way to compare impaired against clean behaviour.

---

## How it works

Windows Mobile Hotspot is built around sharing one specific connection profile, so handing it the
Ethernet profile is what gives the phone its internet — no separate Internet Connection Sharing
step is needed on that path. (ICS is implemented as a fallback for machines where Mobile Hotspot
is unavailable.)

Traffic for a tethered client is *routed* by Windows rather than addressed to it, so Crapnet
captures at the forwarding layer, where that traffic actually lives, and scopes the capture to the
hotspot's own subnet so the rest of the machine is left alone.

Packets are classified **uplink** or **downlink** by testing them against that subnet, not by the
driver's raw inbound/outbound flag — once Windows is routing on someone else's behalf, "outbound"
stops meaning anything useful.

Every captured packet is withheld from the network until the engine decides what to do with it,
which is why dropping a packet is simply a matter of not forwarding it.

### Layering

```
Crapnet.App              Avalonia UI, composition root        net8.0-windows
Crapnet.Infrastructure   WinDivert, WinRT tethering, ICS      net8.0-windows
Crapnet.Application      use cases, ports, impairment engine  net8.0
Crapnet.Domain           rules, impairments, value objects    net8.0
```

Dependencies point inwards only. The two inner layers target plain `net8.0` and know nothing about
Windows, the capture driver or the UI: everything platform-specific sits behind a port
(`IPacketGateway`, `IHotspotController`, `IClock`, `IRandomSource`, …) and is injected.

That is not decoration. It means the part most worth getting right — how nine impairments compose,
in what order, and with what timing — is exercised by **153 tests that run anywhere**, with no
driver, no radio and no Windows:

```bash
dotnet test tests/Crapnet.Domain.Tests tests/Crapnet.Application.Tests
```

Probability and time are both injected, so "30% drop" is an exact assertion rather than a flaky
one, and a 500 ms delay is verified without waiting 500 ms.

### Design notes

**Bandwidth is not a token bucket.** It models a link that serialises packets behind a virtual
finish time. Queueing delay then emerges from offered load on its own, and packets cannot overtake
one another — a token bucket will happily reorder a TCP stream while rate-limiting it, which
quietly contaminates the thing you were trying to measure.

**Destructive impairments run before expensive ones.** Block and drop are evaluated before
anything that costs queue budget, so nothing is held for a packet that was going to be discarded.
The delaying impairments then compose by accumulating onto a single release time.

**Rule edits cross threads through one volatile handover.** The capture loop owns all per-rule
state and runs single-threaded, so the hot path needs no locks, and toggling an impairment takes
effect on the next packet without interrupting the capture.

**Per-rule state is per direction.** A 1 Mbps cap means 1 Mbps each way, because that is how
people read it and how real links behave.

---

## Status

The solution builds clean and the domain and application layers are covered by the test suite
above. The Windows-specific layers — the capture driver interop, Mobile Hotspot and ICS — compile
but have **not yet been exercised against real hardware**, so treat the first run on a real machine
as the shakedown.

## Troubleshooting

| Symptom | Cause |
|---|---|
| "needs to run as administrator" | The driver and sharing APIs both require elevation. |
| "capture driver missing" | Run `scripts/fetch-windivert.ps1`. |
| Hotspot will not start | The adapter or driver does not support Mobile Hotspot. Check Settings → Mobile hotspot. |
| Device connects but has no internet | The uplink has no route, or another share is already configured. |
| Rules have no effect | Check the master toggle is armed and Bypass is off; confirm the device's address is inside the subnet shown. |

## Credits

[WinDivert](https://github.com/basil00/WinDivert) by Basil Fierz does the packet interception.
[clumsy](https://github.com/jagt/clumsy) and [inssidious](https://github.com/shanselman/Inssidious)
are the reference points this is built against.

## Licence

MIT. WinDivert is LGPLv3 and is fetched separately rather than redistributed here.

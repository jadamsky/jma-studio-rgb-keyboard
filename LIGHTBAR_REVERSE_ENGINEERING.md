# Reverse-Engineering the Acer Predator Helios 16 (PH16-71) Rear Lightbar

This document is a full account of how the rear lightbar's static/solid
color control was reverse-engineered on an Acer Predator Helios 16
(PH16-71), from "nothing works" to a fully working, reproducible
protocol. It's written for anyone else — on this exact model or a
sibling Acer/Nitro gaming laptop — trying to solve the same problem.

If you just want the punchline (the working bytes) without the story,
skip to [The final, complete protocol](#the-final-complete-protocol).
If you're fighting the exact same wall we hit for months, the [dead
ends](#dead-ends-what-did-not-work) section may save you a lot of time.

**Scope and honesty check up front**: every byte value in this document
was confirmed live, on one specific PH16-71 unit, by watching Acer's own
real software do it. It is *not* guessed, and it is *not* copied from
another laptop's reverse-engineering project — in fact, several of
those turned out to have the wrong bytes for this exact chassis, which
is itself an important finding (see below). If you're on a different
model, don't assume these exact bytes apply — but the *technique* here
(especially the Frida instrumentation approach) should get you there
quickly.

---

## 1. The problem

The PH16-71 has two independent RGB lighting surfaces:

1. A per-key backlit keyboard, addressable over plain USB HID. This is
   well documented (see [Venator](https://github.com/Exyons/Venator) and
   [Order52/ph16-71-rgb](https://github.com/Order52/ph16-71-rgb)) and
   was not the hard part.
2. A separate rear "lightbar" strip (Acer part 58.QJQN7.001) with (on
   this unit) three independently colorable zones. **This has no HID
   interface, no documented protocol, and is only reachable through
   Windows ACPI-WMI** — the same mechanism PredatorSense itself uses.

Getting *some* light out of the lightbar (animated effects: breathing,
neon, wave, ripple, scanner, strobe) turned out to be comparatively
easy — a documented byte layout from Venator's own reverse-engineering
worked for all of those. Getting a **static/solid color** — which is
what PredatorSense's own UI actually offers, and what most users
actually want — did not work, at all, for a very long time, despite
literally hundreds of byte-level variations.

## 2. The transport: ACPI-WMI, not HID

The relevant WMI class is:

- **Class name**: `AcerGamingFunction`
- **Namespace**: `root\wmi`
- **GUID**: `7A4DDFE7-5B5D-40B4-8595-4408E0CC7F56`

You can find this yourself with:

```powershell
Get-CimClass -Namespace root\wmi -ClassName AcerGamingFunction
```

Acer's own driver registers this friendly class name directly — no
GUID-to-class mapping gymnastics needed. Every gaming-related feature
(lighting, fans, power profiles, CPU overclocking) goes through this
one class, dispatched by a numeric method ID (see below).

### Calling convention that actually works

PowerShell's modern WMI cmdlets are unreliable against this specific
class:

- `Invoke-CimMethod` (WinRM/CIM) fails with "Invalid method
  Parameter(s)".
- `Invoke-WmiMethod` (legacy DCOM) fails the same way.
- `Get-WmiObject` + `.GetMethodParameters()` + `.InvokeMethod()` throws
  `Unable to cast object of type 'System.Management.ManagementBaseObject'
  to type 'System.IConvertible'`.

What reliably works is **Python via `pywin32`**, using the older
DCOM-style automation object rather than the modern CIM path:

```python
import win32com.client

wmi = win32com.client.GetObject(r"winmgmts:\\.\root\wmi")
instance = list(wmi.InstancesOf("AcerGamingFunction"))[0]  # NOT wmi.Get()

def call_array(method, arr):
    p = instance.Methods_(method).InParameters.SpawnInstance_()
    p.gmInput = list(arr)          # a plain Python list of ints
    result = instance.ExecMethod_(method, p)
    return result.Properties_("gmOutput").Value

def call_u64(method, value):
    p = instance.Methods_(method).InParameters.SpawnInstance_()
    p.gmInput = value              # a plain Python int
    result = instance.ExecMethod_(method, p)
    return result.Properties_("gmOutput").Value
```

Every method on this class requires an **elevated (Administrator)**
process. Calling from a non-elevated process fails with "Access
denied".

## 3. Building the complete method map

`Get-CimClass`'s default summary view (`Select-Object Name, CimType`)
does **not** show a parameter's array length (`MAX`). You have to drill
into each parameter's `Qualifiers` collection directly:

```powershell
$cls = Get-CimClass -Namespace root\wmi -ClassName AcerGamingFunction
$cls.CimClassMethods | ForEach-Object {
    $m = $_
    $wmiId = ($m.Qualifiers | Where-Object Name -eq 'WmiMethodId').Value
    foreach ($p in $m.Parameters) {
        $max = ($p.Qualifiers | Where-Object Name -eq 'MAX').Value
        "$wmiId  $($m.Name)  $($p.Name):$($p.CimType)" + $(if ($max) {"[MAX=$max]"})
    }
}
```

This produced the full picture (numeric `WmiMethodId` cross-checked
against a real decompiled BIOS MOF from a sibling reverse-engineering
project, and against another open-source Linux driver's own documented
constants — all three independently agree):

| Id | Method | In | Out |
|---:|---|---|---|
| 1 | SetGamingProfile | u64 | u32 |
| **2** | **SetGamingLED** | **u8[MAX=12]** | u32 |
| 3 | GetGamingProfile | u32 | u64 |
| 4 | GetGamingLED | u32 | u8 + u8[MAX=11] |
| 5 | GetGamingSysInfo | u32 | u64 |
| 6 | SetGamingRgbKb | u64 | u32 |
| 7 | GetGamingRgbKb | u32 | u64 |
| 8/9 | Set/GetGamingProfileSetting | u64/u32 | u32/u64 |
| 10/11 | Set/GetGamingLEDBehavior | u64/u32 | u32/u64 |
| 12/13 | Set/GetGamingLEDColor | u64/u32 | u32/u64 |
| 14-19 | fan behavior/speed/table | u64/u32 | u32/u64 |
| 20 | **SetGamingKBBacklight** | **u8[MAX=16]** | u32 |
| 21 | GetGamingKBBacklight | u32 | u8 + u8[MAX=15] |
| 22/23 | Set/GetGamingMiscSetting | u64/u32 | u32/u64 |
| 24/25 | CPU overclocking profile | u8+u8[512] | ... |

Acer's own MOF (yes, we found one, decompiled from a BIOS binary —
see [Sources](#sources-and-prior-art)) never breaks any of these methods
into named sub-fields. Every one is a flat, completely undocumented
`gmInput`/`gmOutput` blob, even in Acer's own source. There is no
shortcut here; every reverse-engineering project working on this
problem (including this one) has had to brute-force the byte layout by
hand.

## 4. Case study: the `SetGamingLED` length bug

For a long time, **every single call to `SetGamingLED`** — dozens of
distinct byte-content variations, at both 9 and 16 bytes, with explicit
COM VARIANT type coercion, even run under a literal `NT
AUTHORITY\SYSTEM` scheduled task — failed identically with:

```
(-2147352567, 'Exception occurred.', (0, 'SWbemObjectEx', 'Invalid parameter ', None, 0, -2147217400), None)
```

The cause had nothing to do with content, privilege, or COM typing. **A
generic manufacturer MOF pulled from a different Acer model's decompiled
BIOS declares `SetGamingLED`'s array as 16 bytes** (same as
`SetGamingKBBacklight`). This exact machine's *live* `Get-CimClass`
output shows the real value is **12 bytes**. Every attempt had simply
been the wrong length.

**Takeaway**: the exact same WMI class GUID, on the exact same method
name, can have a genuinely different array length on different chassis
of the same OEM's product line. No generic reference will ever have
this number for your specific unit. Always verify the real `MAX`
qualifier live against your own machine before assuming any published
byte layout is correct — and if a `SetGamingLED`-shaped call is
throwing "Invalid parameter" for you, check this first.

## 5. Getting ground truth: capturing PredatorSense's real traffic

After confirming (by briefly running the real PredatorSense app) that
static per-zone color absolutely does work on this hardware, the next
step was watching *exactly* what it sends. This did **not** require any
third-party tooling or process injection — Windows ships a built-in ETW
trace channel for WMI activity:

```powershell
wevtutil sl Microsoft-Windows-WMI-Activity/Trace /e:true   # enable (elevated, once)

# ... perform the action in the vendor app you're watching ...

Get-WinEvent -LogName 'Microsoft-Windows-WMI-Activity/Trace' -Oldest |
  Where-Object TimeCreated -gt $since |
  Select-Object TimeCreated, Message
```

Reading already-logged events does **not** require elevation once the
channel exists, even though enabling it does.

**Gotcha**: `wevtutil cl` (clear) can silently stop the channel from
logging *new* events on some machines (the config still reports
`enabled: true`, but the underlying `.evtx` file's last-write time
freezes). If that happens, force a fresh session with
`wevtutil sl ... /e:false` then `/e:true` again.

This revealed the real commit sequence for a single zone-color edit:

```
SetGamingLED -> SetGamingKBBacklight -> SetGamingRgbKb -> SetGamingRgbKb -> SetGamingRgbKb
```

repeated **three times in a row** (~65ms apart) — i.e. even editing one
zone re-asserts all three zones' colors, and the whole five-call group
fires three times for reliability (this chassis has documented
real-world lighting flakiness reported by other owners).

The trace also revealed *who* is actually making these calls, which was
the next important discovery.

## 6. Finding the real actor: it's not the obvious service

Acer ships a service called `AcerLightingService`. It seemed like the
obvious candidate for "the thing that talks WMI." **It is not.** The
ETW trace's `ClientProcessId` for the actual `Set*` calls pointed to a
completely different, unexpected process:

```powershell
Get-CimInstance Win32_Process -Filter "ProcessId=<pid from the trace>"
```

→ **`OpenRGB.exe`**. Specifically, a **private, unpublished fork** of
the open-source [OpenRGB](https://openrgb.org/) project — confirmed by
extracting printable strings from the binary and finding unstripped PDB
debug paths like `D:\project\AcerOpenRGB\OpenRGB\Controllers\...`,
including Acer-specific classes (`AcerLightBarController`,
`AcerGlobalController`, `AcerUSBController`) that don't exist anywhere
in OpenRGB's real public upstream repository. It's bundled inside the
`predatorservice` driver package
(`C:\WINDOWS\System32\DriverStore\FileRepository\predatorservice.inf_amd64_*\OpenRGB.exe`).

`AcerLightingService` itself, in the trace, only ever does read-only
`GetGamingSysInfo` polling in the background — it never issues a single
`Set*` call on its own.

This explained why an earlier idea — "maybe `AcerLightingService` needs
to be running for our own calls to work" — was checked and ruled out
directly: the final working protocol was re-tested with
`AcerLightingService` fully stopped and `OpenRGB.exe` not running at
all, and it still worked. Nothing from Acer's software needs to be
running in the background.

## 7. Getting the real bytes: instrumenting the binary with Frida

The ETW trace gives you method *names* and *timing*, never argument
*bytes*. To get the actual bytes `OpenRGB.exe` sends, we used
[Frida](https://frida.re/), a free dynamic instrumentation toolkit
(`pip install frida frida-tools` — **note**: the current Frida release
requires Python 3.11+; if you're on 3.10, pin
`frida==16.7.19 frida-tools==13.7.1`, since newer releases' own
`__init__.py` uses a `typing` import that doesn't exist before 3.11 and
crashes immediately on import).

### 7.1 `OpenRGB.exe` only connects to WMI once

The target process only calls `CoCreateInstance`/`ConnectServer` **once,
at its own startup** — not per lighting request. Attaching Frida to an
already-running instance sees nothing. The fix: restart the service
that spawns it, and race to attach before it finishes its own COM
initialization:

```python
import subprocess, time, psutil

old_pid = find_pid("OpenRGB.exe")  # may be None
subprocess.run(["powershell", "-NoProfile", "-Command",
                 "Restart-Service -Name AcerLightingService -Force"])

new_pid = None
deadline = time.time() + 15
while time.time() < deadline:
    p = find_pid("OpenRGB.exe")
    if p is not None and p != old_pid:
        new_pid = p
        break
    time.sleep(0.005)  # tight poll -- attach needs to win the race

session = frida.attach(new_pid)
# ... load the hook script here, see below ...
```

In practice this reliably wins the race — process spawn, module
loading, and first COM call take tens to hundreds of milliseconds, more
than enough for a 5ms polling loop plus `frida.attach()` to land first.

### 7.2 Bootstrapping the COM interception

Rather than guess at higher-level WMI scripting wrapper internals, hook
two well-known, **publicly documented, stable** COM vtable layouts from
`wbemcli.h`:

1. Hook `ole32.dll!CoCreateInstance` (an exported, hookable-by-name
   function). Filter for `rclsid == CLSID_WbemLocator`
   (`{4590F811-1D3A-11D0-891F-00AA004B2E24}`), and read the resulting
   `IWbemLocator*` from the out-parameter.
2. Read *that* object's vtable pointer (the first 8 bytes of any COM
   object), and hook vtable slot **3** (`ConnectServer`) directly by
   address with `Interceptor.attach`. This works for *every* instance
   of that COM class from then on, since the vtable array is shared/
   static per implementing class — you only need to discover it once.
3. In `ConnectServer`'s `onLeave`, read the resulting `IWbemServices*`
   out-parameter, read *its* vtable, and hook slot **24**
   (`ExecMethod`) the same way.

```javascript
const CLSID_WBEMLOCATOR = [0x11,0xF8,0x90,0x45,0x3A,0x1D,0xD0,0x11,0x89,0x1F,0x00,0xAA,0x00,0x4B,0x2E,0x24];

Interceptor.attach(Process.getModuleByName('ole32.dll').getExportByName('CoCreateInstance'), {
  onEnter(args) { this.rclsid = args[0]; this.ppv = args[4]; },
  onLeave(retval) {
    if (!guidEquals(this.rclsid, CLSID_WBEMLOCATOR)) return;
    const locatorPtr = this.ppv.readPointer();
    const vtable = locatorPtr.readPointer();
    const connectServerAddr = vtable.add(3 * Process.pointerSize).readPointer();
    hookConnectServer(connectServerAddr); // hooks ExecMethod inside its own onLeave, slot 24
  }
});
```

### 7.3 The BSTR gotcha

`ExecMethod`'s second parameter (`strMethodName`) is declared `BSTR` in
the IDL. Reading it with *proper* BSTR semantics (a 4-byte length
prefix immediately before the pointer) reliably returned an **empty
string**, every single call, for a long time — a real, silent, and
very confusing bug. Dumping the raw pointer value showed it sitting in
the same address range as loaded DLL code (`.rdata`), not the OLE
automation heap. **It's actually just a plain, null-terminated
`LPCWSTR`, not a real length-prefixed BSTR**, despite what the IDL
declares:

```javascript
// WRONG -- silently returns ""
const len = ptr.sub(4).readU32();
const s = ptr.readUtf16String(len / 2);

// RIGHT
const s = ptr.readUtf16String();  // no length arg, reads to the null terminator
```

If a read comes back suspiciously empty, don't trust the IDL-declared
type — dump the raw pointer and try the simpler interpretation first.

### 7.4 Cleanly extracting the argument value

Once `ExecMethod` calls with a method name starting with `"SetGaming"`
are identified, the cleanest way to get the real `gmInput` value is to
call the in-params object's own `IWbemClassObject::Get` method (vtable
slot **4**) rather than trying to parse VARIANT/SAFEARRAY memory layout
by hand:

```javascript
const vtable = pInParams.readPointer();
const getAddr = vtable.add(4 * Process.pointerSize).readPointer();
const getFn = new NativeFunction(getAddr, 'int', ['pointer','pointer','int','pointer','pointer','pointer']);
const namePtr = Memory.allocUtf16String('gmInput');
const variantBuf = Memory.alloc(32);
const hr = getFn(pInParams, namePtr, 0, variantBuf, ptr(0), ptr(0));
const vt = variantBuf.readU16();  // VARIANT type tag
```

For array results (`vt = 8209` = `VT_ARRAY|VT_UI1`), use the *real*
`oleaut32.dll` exports rather than guessing the `SAFEARRAY` struct's
internal layout by hand:

```javascript
const psa = variantBuf.add(8).readPointer();
safeArrayAccessData(psa, pvDataOut);
safeArrayGetUBound(psa, 1, ubOut);
safeArrayGetLBound(psa, 1, lbOut);
const count = ubOut.readS32() - lbOut.readS32() + 1;
const bytes = pvData.readByteArray(count);
safeArrayUnaccessData(psa);
```

**A second surprise**: `SetGamingRgbKb`'s `gmInput` — declared `UInt64`
in the MOF — actually came back with `vt = 8` (`VT_BSTR`), i.e. a
**decimal string** like `"1137832953344"`, not a native 64-bit integer.
Reading `variantBuf.add(8).readPointer()` and then that pointer as a
UTF-16 string gets the real value. Another case of "don't trust the
declared type."

## 8. Decoding the captured bytes

With the interception working, changing a real color in PredatorSense
produced (abbreviated, one full 3x-repeated group):

```
SetGamingLED:          10 00 00 00 00 00 ff ff ff 15 00 00
SetGamingKBBacklight:  00 00 64 00 00 00 00 00 03 02 00 00 00 00 00 00
SetGamingRgbKb (x3):   1137832953344 / 2234338012672 / 4436368955648
```

**`SetGamingLED`** — identical on every single capture, regardless of
what color or zone was actually being set. It's a fixed priming/"arm"
trigger, not something that carries any data at all. (This explains
why guessing "meaningful" content for it never worked — there isn't
any.)

**`SetGamingKBBacklight`** — `[0, 0, 0x64, 0, 0, 0, 0, 0, 3, 2, 0, 0, 0,
0, 0, 0]`. Decoded against the general layout documented by other
Acer-lightbar reverse-engineering projects (mode, speed, brightness,
reserved, direction, R, G, B, tail markers, padding): mode=0,
brightness=0x64=**100**, tail=(3,2), and **all-zero color**. Confirms
the color does *not* live here at all — this call is purely a
mode/brightness "commit," with the actual color coming entirely from
the next step.

**`SetGamingRgbKb`** — this is where the real breakthrough was. Decode
the captured decimal values as little-endian bytes:

```python
>>> (1137832953344).to_bytes(8, 'little').hex(' ')
'00 06 21 ec 08 01 00 00'
>>> (2234338012672).to_bytes(8, 'little').hex(' ')
'00 6e ec 38 08 02 00 00'
>>> (4436368955648).to_bytes(8, 'little').hex(' ')
'00 19 32 ec 08 04 00 00'
```

Byte 0 is always `0x00`. Byte 4 is always `0x08`. **Byte 5 is `0x01`,
`0x02`, `0x04`** for the three zones respectively — the zone bitmask,
exactly matching the "3x `SetGamingRgbKb` calls per commit" from the
trace. Bytes 1-3 vary per call and are the actual **R, G, B** color
bytes. So the real packing formula is:

```
value = (R << 8) | (G << 16) | (B << 24) | (0x08 << 32) | (mask << 40)
```

**This is completely different from the naive guess** every publicly
available reference (and our own many failed attempts) used:
`mask | (R<<8) | (G<<16) | (B<<24)` — which puts the mask in the *low*
byte instead of byte 5, and is missing the constant `0x08` entirely.
That single wrong assumption, copied from a sibling model's project, is
likely why nobody using that formula ever saw real color.

## 9. The final, complete protocol

```python
import time
import win32com.client

wmi = win32com.client.GetObject(r"winmgmts:\\.\root\wmi")
instance = list(wmi.InstancesOf("AcerGamingFunction"))[0]

def call_array(method, arr):
    p = instance.Methods_(method).InParameters.SpawnInstance_()
    p.gmInput = list(arr)
    return instance.ExecMethod_(method, p).Properties_("gmOutput").Value

def call_u64(method, value):
    p = instance.Methods_(method).InParameters.SpawnInstance_()
    p.gmInput = value
    return instance.ExecMethod_(method, p).Properties_("gmOutput").Value

# Fixed priming trigger -- identical every time, do not vary this.
LED_PAYLOAD = [0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x15, 0x00, 0x00]

def kb_commit(brightness=100):
    return call_array("SetGamingKBBacklight",
        [0, 0, brightness, 0, 0, 0, 0, 0, 3, 2, 0, 0, 0, 0, 0, 0])

def rgbkb(mask, r, g, b):
    value = (r << 8) | (g << 16) | (b << 24) | (0x08 << 32) | (mask << 40)
    return call_u64("SetGamingRgbKb", value)

# mask: 1 = zone 1 (left), 2 = zone 2 (center), 4 = zone 3 (right)
# -- viewed from the front of the laptop with the lid up.
for _ in range(3):                    # repeat 3x, matching the real cadence
    call_array("SetGamingLED", LED_PAYLOAD)
    kb_commit(100)
    rgbkb(1, 255, 0, 0)
    rgbkb(2, 255, 0, 0)
    rgbkb(4, 255, 0, 0)
    time.sleep(0.065)
```

This must run in an elevated (Administrator) process, and was verified
to require **nothing** from Acer's own software running in the
background (tested with `AcerLightingService` and `OpenRGB.exe` both
fully stopped — still works).

## 10. Dead ends: what did not work

Documented in detail so nobody re-derives them from scratch:

- **`SetGamingKBBacklight`'s mode byte set to `0xFF` ("STATIC"/"Direct"
  mode)**, with color placed directly in that same 16-byte buffer —
  this is what one sibling PH16-71 reverse-engineering project
  documented as the correct approach, and it never produced visible
  light on this unit, despite hundreds of variations (tail bytes,
  reserved byte, direction, speed, brightness scale 0-100 vs 0-255,
  color channel order, double-sends). Turns out this chassis's real
  software simply doesn't use that mode for static color at all — see
  above.
- **`SetGamingKBBacklight` mode=`0x00` with real color in the same
  buffer** (a different sibling project, for a different Acer model,
  documented this as their working static-color approach) — also never
  worked here. `SetGamingKBBacklight` never carries color on this
  chassis; it's purely a brightness/mode "commit."
- **`SetGamingLEDColor` / `SetGamingLEDBehavior`**, and naive
  `SetGamingRgbKb` packing with the mask in the low byte — never
  worked, and in the `SetGamingLEDColor` case, was independently
  confirmed (by reading back with `GetGamingLEDColor`) to never
  actually change any real state, consistent with another project's
  own much more rigorous investigation of the equivalent problem on a
  *different* Acer model, which reached the same "investigated, not
  figured out" conclusion for that path.
- **Every plausible content guess for `SetGamingLED` at the wrong
  (16-byte) array length** — see the whole of [section 4](#4-case-study-the-setgamingled-length-bug).
- **Assuming a privilege-tier difference** (Administrator vs. literal
  `NT AUTHORITY\SYSTEM`, via a Scheduled Task) explained any of the
  above failures. It didn't — confirmed by testing the exact same
  failing calls as SYSTEM with no change in outcome.

**The general lesson, confirmed repeatedly**: every other Acer/Nitro
model's own reverse-engineering project (several were consulted) had
*some* wrong assumption when applied to this exact chassis — a
different array length, a different packing formula, a mode that isn't
actually used the same way. The WMI class/GUID/method-ID numbering
scheme is shared across the product line, but the actual byte-level
semantics are not. Treat every external reference as a hypothesis to
test, never as ground truth for your specific unit.

## 11. Architectural notes for building a real app around this

- **Elevation**: every `Set*` call requires an Administrator token.
  Rather than prompting for UAC on every single request, if your app
  has a persistent background service/daemon, have it started via a
  Windows Scheduled Task configured with `RunLevel HighestAvailable`
  and an "At log on" trigger — Task Scheduler grants full elevation
  silently, with no interactive UAC prompt, and any child process that
  service spawns inherits the same elevated token for its entire
  lifetime. Verify this empirically (don't just assume it): read a
  process's real `TokenElevationType` via
  `OpenProcessToken`/`GetTokenInformation` — `Default`/`Full` means
  genuinely elevated, `Limited` means it isn't.
- **COM objects are thread-affine.** If your app's WMI-calling code
  runs inside a web server/daemon whose request handlers execute in a
  worker thread pool (e.g. FastAPI/Starlette's sync `def` endpoints),
  caching a single WMI instance object created on one thread and
  calling it from a different thread later fails with
  `CO_E_NOTINITIALIZED` (`0x800401F0`) — a distinct COM exception from
  the "Invalid parameter" errors seen elsewhere in this investigation,
  easy to mistake for a new protocol bug if you don't recognize the
  code. Fix: call `pythoncom.CoInitialize()` once per thread, and
  resolve a fresh WMI instance per-thread (e.g. via `threading.local()`)
  rather than sharing one across threads. Resolving the instance is a
  cheap local lookup, so there's no real cost to doing this per-thread.

## 12. Caveats

- These exact byte values are confirmed for **this specific PH16-71
  unit**. As demonstrated repeatedly above, even sibling reverse-
  engineering efforts on ostensibly similar Acer hardware had
  meaningfully different correct bytes. If you're on a different model
  (or even a different firmware/BIOS revision of the same model), use
  the *technique* here — especially the Frida instrumentation approach
  — to get ground truth for your own unit rather than assuming these
  bytes apply.
- Brightness is on a 0-100 scale here (confirmed via Venator's own
  kernel driver defaulting to `lb_brightness = 100`, and an
  out-of-range value producing a dark result).
- The left/center/right zone-to-mask mapping (1/2/4) was confirmed by
  eye, viewing the laptop from the front with the lid open, on this
  specific unit's three-zone lightbar. A unit with a different number
  of zones would need this re-verified.
- The 3x-repeated commit cadence matches what Acer's own real software
  does; a single (non-repeated) commit was not exhaustively tested for
  reliability on its own, so the repetition is kept in the reference
  implementation as a conservative default.

## Sources and prior art

These projects, all doing similar reverse-engineering work on sibling
Acer/Nitro models, were consulted for orientation (WMI class/GUID,
general method-ID numbering scheme) — none had the correct bytes for
this exact chassis, but the general shape of the problem and several
useful leads came from them:

- [Exyons/Venator](https://github.com/Exyons/Venator) — Linux driver
  for the PH16-71's per-key keyboard and lightbar; the keyboard half is
  fully correct and was used as-is; the lightbar's `STATIC` mode
  documentation is what turned out not to apply here.
- [Order52/ph16-71-rgb](https://github.com/Order52/ph16-71-rgb) —
  independent per-key keyboard implementation, used to cross-check
  Venator's checksums.
- [fredac100/nekro-sense](https://github.com/fredac100/nekro-sense) —
  Linux driver for the Predator Helios Neo 16 (PHN16-72); useful for
  the general "byte 9 of the unified buffer selects keyboard vs. lid
  logo" concept and the dedicated `SetGamingLEDColor` method idea
  (which didn't pan out on this chassis, but was worth ruling out).
- `RedStiff/Acer_Predator_Tool-PHN18-71` — Windows-side research for a
  PHN18 desktop-replacement-style tool; useful for the general "static
  color needs an activation/commit sequence" idea, though its exact
  bytes (mode=0 with color, tail bytes) didn't transfer.
- `jlucaso1/acer-predator-re` — produced a real decompiled BIOS MOF
  (via `bmf2mof`) for a PHN16-73, used to independently cross-check the
  method-ID numbering scheme.
- `cellux-git/nitro-tray` — a notably rigorous investigation of the
  equivalent "secondary LED surface" problem on an Acer Nitro model,
  reaching an honest "investigated, not figured out" conclusion for
  their hardware's lid logo — useful confirmation that the WMI "LED
  family" methods being non-functional-feeling is a real, shared
  experience across this OEM's product line, not a sign of doing
  something wrong.

None of the above is reproduced here beyond citing facts (byte values,
protocol facts are not copyrightable expression); all code in the
reference implementation above is independently written.

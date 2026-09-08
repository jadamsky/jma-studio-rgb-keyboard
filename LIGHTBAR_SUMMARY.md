## Acer Predator Helios 16 (PH16-71) rear lightbar: solved static/solid color

Posting this in case it helps anyone else stuck on this. On my PH16-71
(3-zone rear lightbar), static color turned out to need three separate
WMI method calls together, not the single `SetGamingKBBacklight`
STATIC-mode (`0xFF`) call most write-ups (including my own earlier
attempts) assume. That mode simply never produced light on my unit,
no matter the byte layout.

**Class**: `AcerGamingFunction`, namespace `root\wmi`, GUID
`7A4DDFE7-5B5D-40B4-8595-4408E0CC7F56`. Requires an elevated
(Administrator) process for every `Set*` call.

**The real sequence** (confirmed by instrumenting Acer's own lighting
process with Frida — it's a private, unpublished OpenRGB fork bundled
in the `predatorservice` driver package, not the visible
`AcerLightingService`, which only polls read-only in the background):

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

# Fixed priming call -- identical every time regardless of color/zone.
# NOTE: on my unit SetGamingLED's array length is 12 bytes, not the 16
# you might expect from other write-ups -- check Get-CimClass's real
# Qualifiers (not just the summary view) for YOUR unit before assuming.
LED_PAYLOAD = [0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x15, 0x00, 0x00]

def kb_commit(brightness=100):
    # mode=0, NO color here -- color lives entirely in SetGamingRgbKb below
    return call_array("SetGamingKBBacklight",
        [0, 0, brightness, 0, 0, 0, 0, 0, 3, 2, 0, 0, 0, 0, 0, 0])

def rgbkb(mask, r, g, b):
    # mask 1/2/4 = zone 1/2/3. This packing (mask in byte 5, plus a
    # constant 0x08 in byte 4) is what actually worked -- NOT
    # mask | R<<8 | G<<16 | B<<24, which is what I originally guessed
    # and never got anywhere with.
    value = (r << 8) | (g << 16) | (b << 24) | (0x08 << 32) | (mask << 40)
    return call_u64("SetGamingRgbKb", value)

for _ in range(3):  # repeat 3x, ~65ms apart -- matches Acer's own real cadence
    call_array("SetGamingLED", LED_PAYLOAD)
    kb_commit(100)
    rgbkb(1, 255, 0, 0)
    rgbkb(2, 255, 0, 0)
    rgbkb(4, 255, 0, 0)
    time.sleep(0.065)
```

Verified this needs nothing from Acer's own software running in the
background (works with `AcerLightingService` fully stopped).

**If you're on a different model or a different bios/firmware
revision**: don't assume these exact bytes apply — `SetGamingLED`'s
array length and `SetGamingRgbKb`'s packing formula both turned out to
be genuinely different from what other Acer models' own
reverse-engineering write-ups documented. The Frida-based technique
(watch the vendor's own real software make the calls) is what actually
got me unstuck after months of guessing; happy to share the full
detailed write-up (WMI method-ID mapping, the Frida hooking approach,
every dead end tried) if it'd help.

// Shared flag RenderLoopService checks every tick so a Diagnostics
// self-test (which writes directly to the keyboard/lightbar, bypassing
// the normal effect-render path) never interleaves raw HID/WMI writes
// with the render loop's own -- Keyboard is documented "not thread-safe
// by itself, callers serialize access" (see its own header comment).

namespace JmaStudio.Service;

public sealed class SelfTestGate
{
    public volatile bool InProgress;
}

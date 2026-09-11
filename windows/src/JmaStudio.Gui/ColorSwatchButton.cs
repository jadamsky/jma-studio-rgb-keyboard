// WPF has no equivalent of HTML's <input type="color"> that gui/index.html
// uses throughout (gradient zone colors, bright/base colors, custom-key
// paint colors). Rather than bring in a third-party picker library for one
// widget, this wraps System.Windows.Forms.ColorDialog (a full RGB/hex
// picker, available via <UseWindowsForms> interop) behind a small clickable
// swatch that looks/behaves like the rest of the app's controls.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JmaStudio.Hardware;

namespace JmaStudio.Gui;

public sealed class ColorSwatchButton : Border
{
    public event Action<Color>? ColorPicked;

    private Color _color = Colors.Black;
    private bool _pickerEnabled = true;

    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            Background = new SolidColorBrush(value);
        }
    }

    public bool PickerEnabled
    {
        get => _pickerEnabled;
        set
        {
            _pickerEnabled = value;
            Opacity = value ? 1.0 : 0.4;
            Cursor = value ? Cursors.Hand : Cursors.Arrow;
        }
    }

    public ColorSwatchButton()
    {
        Width = 36;
        Height = 28;
        CornerRadius = new CornerRadius(6);
        BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        BorderThickness = new Thickness(1);
        Cursor = Cursors.Hand;
        Background = new SolidColorBrush(_color);
        MouseLeftButtonUp += (_, _) => OpenPicker();
    }

    private void OpenPicker()
    {
        if (!PickerEnabled) return;
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(_color.R, _color.G, _color.B),
            FullOpen = true,
        };
        // ColorDialog is a Win32 modal dialog, not a WPF Window -- pass this
        // window's handle so it centers over the app instead of the screen.
        var owner = new Win32WindowHandle(Window.GetWindow(this));
        if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
        {
            Color = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            ColorPicked?.Invoke(_color);
        }
    }

    private sealed class Win32WindowHandle : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public Win32WindowHandle(Window window) => Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
    }
}

public static class ColorConversions
{
    public static RgbColor ToRgbColor(this Color c) => new(c.R, c.G, c.B);
    public static Color ToMediaColor(this RgbColor c) => Color.FromRgb(c.R, c.G, c.B);
}

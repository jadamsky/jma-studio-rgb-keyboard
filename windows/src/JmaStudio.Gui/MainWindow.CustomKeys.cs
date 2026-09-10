// Custom Key Colors painter -- ported from gui/index.html's "Custom Key
// Colors" panel and gui/app.js's custom-key-colors-editor section
// (buildCustomKeyboard, toggleKeySelection, applyColorToSelection, etc).
// Reuses MainWindow.xaml.cs's BuildKeyGrid (the same per-key layout as the
// live preview) with a click-to-select decorator instead of just color
// polling.

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JmaStudio.Effects;
using JmaStudio.Hardware;

namespace JmaStudio.Gui;

public partial class MainWindow
{
    private readonly Dictionary<int, Border> _ckCellBorders = new();
    private readonly HashSet<int> _ckSelected = new();
    private readonly Dictionary<int, RgbColor> _customKeyColors = new();
    private RgbColor _customKeyDefault = new(0, 0, 0);
    private readonly List<string> _recentColors = new(); // hex strings, most-recent first
    private Debouncer? _ckDebounce;

    private static readonly SolidColorBrush CkSelectedBorderBrush = new(Color.FromRgb(0xB5, 0x44, 0xE8)); // matches gui/style.css's --accent-b

    // localStorage isn't available outside a browser -- a small JSON file
    // under the current user's LocalAppData is the closest WPF equivalent,
    // same "best-effort, never fatal" spirit as app.js's own try/catch
    // around localStorage (a private/blocked profile there just means
    // recent colors don't persist; same result here if this fails).
    private static readonly string RecentColorsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JmaStudio", "gui-recent-colors.json");

    private void InitializeCustomKeysPanel()
    {
        _ckDebounce = new Debouncer(120, async () => await OnCkLiveApplyAsync());
        CkDefaultSwatch.Color = Colors.Black;
        CkPaintSwatch.Color = Colors.White;
        CkPaintSwatch.PickerEnabled = false;
        CkDefaultSwatch.ColorPicked += c =>
        {
            _customKeyDefault = c.ToRgbColor();
            RenderCustomKeyColors();
            FireCkLive();
        };
        CkPaintSwatch.ColorPicked += ApplyColorToSelection;
        LoadRecentColors();
        RenderRecentColors();
        UpdateSelectionVisual();
    }

    private void BuildCustomKeysCanvas()
    {
        BuildKeyGrid(CkKeyboardCanvas, CkPreviewBoardHost, _ckCellBorders, (border, cell) =>
        {
            border.Cursor = Cursors.Hand;
            border.MouseLeftButtonUp += (_, _) =>
            {
                ModifierKeys mods = System.Windows.Input.Keyboard.Modifiers;
                bool additive = mods.HasFlag(ModifierKeys.Shift) || mods.HasFlag(ModifierKeys.Control);
                ToggleKeySelection(cell.Index, additive);
            };
        });
        RenderCustomKeyColors();
        UpdateSelectionVisual();
    }

    private void CkPreviewBoardHost_SizeChanged(object sender, SizeChangedEventArgs e) => BuildCustomKeysCanvas();

    private RgbColor CurrentColorForIndex(int index) =>
        _customKeyColors.TryGetValue(index, out RgbColor c) ? c : _customKeyDefault;

    private void RenderCustomKeyColors()
    {
        foreach ((int index, Border border) in _ckCellBorders)
        {
            border.Background = new SolidColorBrush(CurrentColorForIndex(index).ToMediaColor());
        }
    }

    private void UpdateSelectionVisual()
    {
        foreach ((int index, Border border) in _ckCellBorders)
        {
            bool selected = _ckSelected.Contains(index);
            border.BorderBrush = selected ? CkSelectedBorderBrush : KeyBorderBrush;
            border.BorderThickness = new Thickness(selected ? 2 : 1);
        }

        int count = _ckSelected.Count;
        if (count == 0)
        {
            CkSelectionLabel.Text = "No keys selected -- click a key to select it (shift-click to add more)";
            CkPaintSwatch.PickerEnabled = false;
            return;
        }

        var names = _ckSelected
            .Select(idx => _layout?.Cells.FirstOrDefault(c => c.Index == idx))
            .Where(c => c is not null)
            .Select(c => KeyboardKeyStyle.FriendlyLabel(c!.Name))
            .ToList();
        CkSelectionLabel.Text = count == 1
            ? $"Selected: {names.FirstOrDefault()}"
            : $"Selected {count} keys: {string.Join(", ", names.Take(6))}{(count > 6 ? ", ..." : "")}";
        CkPaintSwatch.PickerEnabled = true;
        // An already-painted key shows its own color when selected, rather
        // than the picker holding onto whatever was last applied elsewhere.
        CkPaintSwatch.Color = CurrentColorForIndex(_ckSelected.First()).ToMediaColor();
    }

    private void ToggleKeySelection(int index, bool additive)
    {
        if (!additive)
        {
            bool wasOnlySelected = _ckSelected.Count == 1 && _ckSelected.Contains(index);
            _ckSelected.Clear();
            if (!wasOnlySelected) _ckSelected.Add(index);
        }
        else if (_ckSelected.Contains(index))
        {
            _ckSelected.Remove(index);
        }
        else
        {
            _ckSelected.Add(index);
        }
        UpdateSelectionVisual();
    }

    private void ApplyColorToSelection(Color color)
    {
        if (_ckSelected.Count == 0) return;
        RgbColor rgb = color.ToRgbColor();
        foreach (int idx in _ckSelected) _customKeyColors[idx] = rgb;
        CkPaintSwatch.Color = color;
        AddRecentColor(ColorToHex(color));
        RenderCustomKeyColors();
        FireCkLive();
    }

    private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private CustomKeysParams BuildCustomKeysParams() => new()
    {
        Colors = _customKeyColors.ToDictionary(kv => kv.Key, kv => kv.Value),
        DefaultColor = _customKeyDefault,
    };

    // Unlike the Gradient/Reactive Typing panels, this panel's live-apply
    // always posts a bare "custom_keys" effect directly -- it does NOT
    // funnel through ApplyCurrentLiveAsync even if typing_reactive is
    // currently using it as a background. That's a faithful port of
    // app.js's applyCustomKeysLive (which never calls applyCurrentLive()
    // the way applyGradientLive/applyTypingReactiveLive do), not an
    // oversight -- editing this grid always previews as a static board.
    private void FireCkLive()
    {
        if (_suppressLiveApply) return;
        _ckDebounce?.Fire();
    }

    private async Task OnCkLiveApplyAsync()
    {
        if (CkLiveCheck.IsChecked != true) return;
        await _api.SetEffectAsync("custom_keys", BuildCustomKeysParams());
        _activePresetName = null;
        await RefreshPresetsAsync();
    }

    private void CkSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_layout is null) return;
        _ckSelected.Clear();
        foreach (LayoutCell c in _layout.Cells) _ckSelected.Add(c.Index);
        UpdateSelectionVisual();
    }

    private void CkSelectNone_Click(object sender, RoutedEventArgs e)
    {
        _ckSelected.Clear();
        UpdateSelectionVisual();
    }

    private void CkResetSelected_Click(object sender, RoutedEventArgs e)
    {
        foreach (int idx in _ckSelected) _customKeyColors.Remove(idx);
        RenderCustomKeyColors();
        FireCkLive();
    }

    private void CkClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Clear all custom key colors?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        _customKeyColors.Clear();
        RenderCustomKeyColors();
        FireCkLive();
    }

    // Snapshots whatever's actually lit right now (any effect -- a
    // gradient, a preset, even mid-chase) into per-key overrides, so "set
    // up a gradient, then pull it into custom and edit it" works as a
    // starting point instead of starting from a blank board.
    private async void CkPullCurrent_Click(object sender, RoutedEventArgs e)
    {
        FrameResponse? frame = await _api.GetFrameAsync();
        if (frame is null || _layout is null) return;
        _customKeyColors.Clear();
        foreach (LayoutCell cell in _layout.Cells)
        {
            if (cell.Index < frame.Colors.Length) _customKeyColors[cell.Index] = frame.Colors[cell.Index];
        }
        RenderCustomKeyColors();
        FireCkLive();
        ShowToast("Pulled current keyboard colors into the custom editor");
    }

    private void LoadRecentColors()
    {
        try
        {
            if (!File.Exists(RecentColorsPath)) return;
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RecentColorsPath));
            if (list is not null)
            {
                _recentColors.Clear();
                _recentColors.AddRange(list);
            }
        }
        catch
        {
            // Corrupt/inaccessible file -- recent colors just won't persist.
        }
    }

    private void SaveRecentColors()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecentColorsPath)!);
            File.WriteAllText(RecentColorsPath, JsonSerializer.Serialize(_recentColors));
        }
        catch
        {
            // Same best-effort spirit as app.js's saveRecentColors.
        }
    }

    private void AddRecentColor(string hex)
    {
        _recentColors.RemoveAll(c => c == hex);
        _recentColors.Insert(0, hex);
        const int maxRecent = 16;
        if (_recentColors.Count > maxRecent) _recentColors.RemoveRange(maxRecent, _recentColors.Count - maxRecent);
        SaveRecentColors();
        RenderRecentColors();
    }

    private void RenderRecentColors()
    {
        CkRecentColorsPanel.Children.Clear();
        var subText = (Brush)FindResource("SubTextBrush");
        if (_recentColors.Count == 0)
        {
            CkRecentColorsPanel.Children.Add(new TextBlock
            {
                Text = "Recently used: none yet", Foreground = subText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            });
            return;
        }
        CkRecentColorsPanel.Children.Add(new TextBlock
        {
            Text = "Recently used:", Foreground = subText, FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        });
        foreach (string hex in _recentColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand, ToolTip = hex,
            };
            swatch.MouseLeftButtonUp += (_, _) => ApplyColorToSelection(color);
            CkRecentColorsPanel.Children.Add(swatch);
        }
    }

    // Loads a custom_keys preset's params into this panel -- ported from
    // app.js's syncTuningPanelsFromPreset's custom_keys handling. Also
    // called when typing_reactive's base_effect is "custom_keys".
    private void LoadCustomKeysParams(CustomKeysParams p)
    {
        _customKeyColors.Clear();
        foreach ((int index, RgbColor color) in p.Colors) _customKeyColors[index] = color;
        _customKeyDefault = p.DefaultColor;
        CkDefaultSwatch.Color = _customKeyDefault.ToMediaColor();
        _ckSelected.Clear();
        RenderCustomKeyColors();
        UpdateSelectionVisual();
    }
}

using System.Windows;
using System.Windows.Input;

namespace JmaStudio.Gui;

public partial class NamePromptWindow : Window
{
    public string EnteredName { get; private set; } = "";

    public NamePromptWindow(string prompt = "Preset name")
    {
        InitializeComponent();
        PromptLabel.Text = prompt;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => TryAccept();

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryAccept();
        else if (e.Key == Key.Escape) DialogResult = false;
    }

    private void TryAccept()
    {
        string name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        EnteredName = name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

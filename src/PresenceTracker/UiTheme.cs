using PresenceTracker.Domain;

namespace PresenceTracker;

internal static class UiTheme
{
    private static readonly Color Background = Color.FromArgb(27, 34, 44);
    private static readonly Color Surface = Color.FromArgb(37, 46, 59);
    private static readonly Color Text = Color.FromArgb(232, 237, 244);
    private static readonly Color Muted = Color.FromArgb(179, 191, 205);

    public static void Apply(Control root, ThemeMode mode)
    {
        if (mode != ThemeMode.Dark) return;
        root.BackColor = Background;
        root.ForeColor = Text;
        foreach (Control control in root.Controls)
            ApplyControl(control);
    }

    private static void ApplyControl(Control control)
    {
        switch (control)
        {
            case Button button:
                if (button.Tag is not DateOnly)
                {
                    button.BackColor = Surface;
                    button.ForeColor = Text;
                }
                break;
            case TextBoxBase or ComboBox or NumericUpDown or ListView:
                control.BackColor = Surface;
                control.ForeColor = Text;
                break;
            case Label label:
                label.ForeColor = label.Font.Bold ? Text : Muted;
                break;
            case GroupBox:
                control.ForeColor = Text;
                control.BackColor = Background;
                break;
            case Panel or TableLayoutPanel or FlowLayoutPanel or TabPage or TabControl:
                control.BackColor = Background;
                control.ForeColor = Text;
                break;
        }
        foreach (Control child in control.Controls)
            ApplyControl(child);
    }
}



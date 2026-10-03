using PresenceTracker.Domain;

namespace PresenceTracker;

internal static class UiTheme
{
    public static readonly Color Navy = Color.FromArgb(8, 29, 47);
    public static readonly Color NavyRaised = Color.FromArgb(14, 43, 67);
    public static readonly Color Blue = Color.FromArgb(20, 111, 242);
    public static readonly Color BlueSoft = Color.FromArgb(231, 241, 255);
    public static readonly Color Background = Color.FromArgb(243, 247, 251);
    public static readonly Color Surface = Color.White;
    public static readonly Color Text = Color.FromArgb(18, 36, 58);
    public static readonly Color Muted = Color.FromArgb(93, 112, 133);
    public static readonly Color Border = Color.FromArgb(221, 230, 239);
    public static readonly Color Green = Color.FromArgb(27, 158, 91);

    public static void Apply(Control root, ThemeMode mode)
    {
        var dark = mode == ThemeMode.Dark;
        var background = dark ? Color.FromArgb(17, 28, 40) : Background;
        var surface = dark ? Color.FromArgb(29, 43, 58) : Surface;
        var text = dark ? Color.FromArgb(230, 237, 245) : Text;
        var muted = dark ? Color.FromArgb(165, 180, 197) : Muted;
        var border = dark ? Color.FromArgb(55, 72, 91) : Border;

        if (root is Form form)
        {
            form.BackColor = background;
            form.ForeColor = text;
            if (form.Font.Name != "Segoe UI" || Math.Abs(form.Font.SizeInPoints - 9F) > 0.01F)
                form.Font = new Font("Segoe UI", 9F);
        }

        ApplyControl(root, dark, background, surface, text, muted, border);
    }

    private static void ApplyControl(Control control, bool dark, Color background, Color surface,
        Color text, Color muted, Color border)
    {
        if (control.Tag is string marker && marker.StartsWith("brand:", StringComparison.Ordinal))
        {
            foreach (Control child in control.Controls)
                ApplyControl(child, dark, background, surface, text, muted, border);
            return;
        }

        switch (control)
        {
            case Button button when button.Tag is DateOnly:
                break;
            case Button button when button.Tag is string tone && tone.StartsWith("action:", StringComparison.Ordinal):
                var palette = tone["action:".Length..];
                if (dark)
                {
                    (button.BackColor, button.ForeColor) = palette switch
                    {
                        "presence" => (Color.FromArgb(28, 66, 48), Color.FromArgb(145, 226, 178)),
                        "plan" => (Color.FromArgb(34, 55, 88), Color.FromArgb(150, 190, 255)),
                        "absence" => (Color.FromArgb(69, 54, 31), Color.FromArgb(255, 210, 130)),
                        _ => (surface, text)
                    };
                }
                else
                {
                    (button.BackColor, button.ForeColor) = palette switch
                    {
                        "presence" => (Color.FromArgb(226, 247, 234), Color.FromArgb(27, 130, 76)),
                        "plan" => (Color.FromArgb(231, 241, 255), Color.FromArgb(20, 100, 210)),
                        "absence" => (Color.FromArgb(255, 242, 223), Color.FromArgb(177, 111, 0)),
                        _ => (surface, text)
                    };
                }
                button.FlatAppearance.BorderColor = border;
                break;
            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = border;
                button.BackColor = button.Tag as string == "primary" ? Blue : surface;
                button.ForeColor = button.Tag as string == "primary" ? Color.White : text;
                break;
            case TextBoxBase or ComboBox or NumericUpDown or DateTimePicker:
                control.BackColor = surface;
                control.ForeColor = text;
                break;
            case ListView list:
                list.BackColor = surface;
                list.ForeColor = text;
                break;
            case Label label:
                label.ForeColor = label.Font.Bold ? text : muted;
                break;
            case GroupBox:
                control.ForeColor = text;
                control.BackColor = surface;
                break;
            case Panel or TableLayoutPanel or FlowLayoutPanel or TabPage or TabControl:
                control.BackColor = control.Tag as string == "card" ? surface : background;
                control.ForeColor = text;
                break;
        }

        foreach (Control child in control.Controls)
            ApplyControl(child, dark, background, surface, text, muted, border);
    }
}

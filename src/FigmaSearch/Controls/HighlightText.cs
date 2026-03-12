using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FigmaSearch.Controls;

/// <summary>
/// TextBlock extension that highlights a search keyword in blue.
/// Usage: <ctrl:HighlightText Text="{Binding Name}" Keyword="{Binding SearchQuery}" />
/// </summary>
public class HighlightText : TextBlock
{
    public static readonly DependencyProperty TextValueProperty =
        DependencyProperty.Register(nameof(TextValue), typeof(string), typeof(HighlightText),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty KeywordProperty =
        DependencyProperty.Register(nameof(Keyword), typeof(string), typeof(HighlightText),
            new PropertyMetadata("", OnChanged));

    public string TextValue
    {
        get => (string)GetValue(TextValueProperty);
        set => SetValue(TextValueProperty, value);
    }

    public string Keyword
    {
        get => (string)GetValue(KeywordProperty);
        set => SetValue(KeywordProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HighlightText)d).Rebuild();

    private void Rebuild()
    {
        Inlines.Clear();
        var text    = TextValue ?? "";
        var keyword = Keyword ?? "";
        if (string.IsNullOrEmpty(text)) return;
        if (string.IsNullOrEmpty(keyword)) { Inlines.Add(new Run(text)); return; }

        int start = 0;
        while (start <= text.Length)
        {
            int idx = text.IndexOf(keyword, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                if (start < text.Length) Inlines.Add(new Run(text[start..]));
                break;
            }
            if (idx > start) Inlines.Add(new Run(text[start..idx]));
            Inlines.Add(new Run(text[idx..(idx + keyword.Length)])
            {
                Foreground = Brushes.DodgerBlue,
                FontWeight = FontWeights.SemiBold
            });
            start = idx + keyword.Length;
        }
    }
}

using System.Text.Json;
using System.Xml;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace v2rayN.Desktop.Views;

public partial class JsonEditor : UserControl
{
    private static readonly JsonSerializerOptions SIndentedOptions = new() { WriteIndented = true };

    private static readonly Lazy<IHighlightingDefinition> SHighlightingDark =
        new(() => BuildHighlighting(dark: true), isThreadSafe: true);

    private static readonly Lazy<IHighlightingDefinition> SHighlightingLight =
        new(() => BuildHighlighting(dark: false), isThreadSafe: true);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<JsonEditor, string>(nameof(Text), defaultValue: string.Empty);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    // opening char -> closing char
    private static readonly Dictionary<char, char> SPairs = new()
    {
        ['{'] = '}',
        ['['] = ']',
        ['('] = ')',
        ['"'] = '"',
    };

    public JsonEditor()
    {
        InitializeComponent();
        var isDark = Application.Current?.ActualThemeVariant != ThemeVariant.Light;
        Editor.SyntaxHighlighting = isDark ? SHighlightingDark.Value : SHighlightingLight.Value;
        Editor.TextArea.TextView.Options.EnableHyperlinks = false;

        Editor.TextChanged += (_, _) =>
        {
            if (Text != Editor.Text)
            {
                SetCurrentValue(TextProperty, Editor.Text);
            }
        };

        this.GetObservable(TextProperty).Subscribe(text =>
        {
            if (Editor.Text != text)
            {
                Editor.Text = text ?? string.Empty;
            }
        });

        Editor.TextArea.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Editor.TextArea.TextEntering += OnTextEntering;
        Editor.TextArea.TextEntered  += OnTextEntered;
    }

    private static void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Back) return;

        var area = (TextArea)sender!;
        if (!area.Selection.IsEmpty) return;

        var doc = area.Document;
        var caret = area.Caret.Offset;
        if (caret <= 0 || caret >= doc.TextLength) return;

        var left = doc.GetCharAt(caret - 1);
        if (!SPairs.TryGetValue(left, out var right)) return;
        if (doc.GetCharAt(caret) != right) return;

        doc.Remove(caret - 1, 2);
        area.Caret.Offset = caret - 1;
        e.Handled = true;
    }

    // Before the character is inserted: if user types a closing char that already sits at
    // the caret (auto-inserted), just skip over it instead of duplicating it.
    private static void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: 1 }) return;
        var ch = e.Text[0];
        if (!SPairs.ContainsValue(ch)) return;

        var area = (TextArea)sender!;
        var doc  = area.Document;
        var caret = area.Caret.Offset;
        if (caret < doc.TextLength && doc.GetCharAt(caret) == ch)
        {
            // skip over the already-present closing char
            area.Caret.Offset = caret + 1;
            e.Handled = true;
        }
    }

    // After the character is inserted: auto-insert the matching closing char.
    private static void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: 1 }) return;
        var ch = e.Text[0];
        if (!SPairs.TryGetValue(ch, out var closing)) return;

        var area  = (TextArea)sender!;
        var caret = area.Caret.Offset;

        if (ch == '"' && !ShouldAutoCompleteQuote(area, caret))
        {
            return;
        }

        area.Document.Insert(caret, closing.ToString());
        // keep caret between the pair
        area.Caret.Offset = caret;
    }

    private static bool ShouldAutoCompleteQuote(TextArea area, int caret)
    {
        var doc = area.Document;
        var quoteOffset = caret - 1;
        if (quoteOffset < 0) return false;

        // Escaped quote (\") should not trigger auto-complete.
        if (IsEscapedByBackslashes(doc, quoteOffset))
        {
            return false;
        }

        var line = doc.GetLineByOffset(quoteOffset);
        var start = line.Offset;
        var end = quoteOffset;
        var quoteCount = 0;
        for (var i = start; i <= end; i++)
        {
            if (doc.GetCharAt(i) == '"' && !IsEscapedByBackslashes(doc, i))
            {
                quoteCount++;
            }
        }

        // Odd count means this quote is opening a string in current line context.
        return (quoteCount & 1) == 1;
    }

    private static bool IsEscapedByBackslashes(TextDocument doc, int offset)
    {
        var slashCount = 0;
        for (var i = offset - 1; i >= 0 && doc.GetCharAt(i) == '\\'; i--)
        {
            slashCount++;
        }

        return (slashCount & 1) == 1;
    }

    private static IHighlightingDefinition BuildHighlighting(bool dark)
    {
        var keyColor = dark ? "#9CDCFE" : "#0451A5";
        var strColor = dark ? "#CE9178" : "#A31515";
        var numColor = dark ? "#B5CEA8" : "#098658";
        var kwColor  = dark ? "#569CD6" : "#0000FF";
        var xshd = $"""
            <?xml version="1.0"?>
            <SyntaxDefinition name="JSON" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Key" foreground="{keyColor}" />
              <Color name="String" foreground="{strColor}" />
              <Color name="Number" foreground="{numColor}" />
              <Color name="Keyword" foreground="{kwColor}" fontWeight="bold" />
              <RuleSet>
                <Rule color="Key">"([^"\\]|\\.)*"(?=\s*:)</Rule>
                <Rule color="String">"([^"\\]|\\.)*"</Rule>
                <Rule color="Number">-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?</Rule>
                <Keywords color="Keyword">
                  <Word>true</Word>
                  <Word>false</Word>
                  <Word>null</Word>
                </Keywords>
              </RuleSet>
            </SyntaxDefinition>
            """;
        using var reader = XmlReader.Create(new StringReader(xshd));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private void FormatJson_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var obj = JsonUtils.ParseJson(Editor.Text);
            Editor.Text = JsonUtils.Serialize(obj, SIndentedOptions);
        }
        catch
        {
            // ignored
        }
    }

    private void Copy_Click(object? sender, RoutedEventArgs e) => Editor.Copy();
    private void Paste_Click(object? sender, RoutedEventArgs e) => Editor.Paste();
    private void SelectAll_Click(object? sender, RoutedEventArgs e) => Editor.SelectAll();
}

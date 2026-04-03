using System;
using System.Text;
using System.Windows;
using Clipboard = System.Windows.Clipboard;
using FrozenOCR.Ocr;

namespace FrozenOCR.UI;

internal partial class ResultWindow : Window
{
    public ResultWindow(OcrResult result)
    {
        InitializeComponent();
        TextBox.Text = result.Text ?? string.Empty;
        ProviderText.Text = $"Provider: {result.ProviderName} | Language: {result.LanguageTag}";
        TextBox.SelectAll();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(TextBox.Text);
    }

    private void CopyOneLine_Click(object sender, RoutedEventArgs e)
    {
        var oneLine = string.Join(" ", TextBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        CopyToClipboard(oneLine);
    }

    private void CopyCodeBlock_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("```");
        sb.AppendLine(TextBox.Text);
        sb.AppendLine("```");
        CopyToClipboard(sb.ToString());
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static void CopyToClipboard(string? text)
    {
        try
        {
            Clipboard.SetText(text ?? string.Empty);
        }
        catch
        {
            // Clipboard can fail if locked by another process; ignore.
        }
    }
}

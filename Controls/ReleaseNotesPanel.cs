using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UpdateCenter.Models;
using UpdateCenter.Services;

namespace UpdateCenter.Controls;

public sealed class ReleaseNotesPanel : Border
{
    private readonly TextBox _text = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Button _link = new() { Margin = new(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _close = new() { Content = "×", Width = 36, Height = 32, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? _request;
    private Uri? _uri;
    private FrameworkElement? _anchor;
    private UpdateItem? _item;
    internal ReleaseNotesService Reader { get; set; } = new();
    public ReleaseNotesPanel()
    {
        Padding = new(16); BorderThickness = new(1); CornerRadius = new(12);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Contained);
        _title.Margin = new(0, 0, 44, 0); _title.TextWrapping = TextWrapping.Wrap;
        _text.Padding = new(0); _text.VerticalContentAlignment = VerticalAlignment.Top;
        _text.SetResourceReference(Control.FontSizeProperty, "FontSize12");
        _link.SetResourceReference(FrameworkElement.StyleProperty, "SettingsButton");
        _close.SetResourceReference(FrameworkElement.StyleProperty, "SettingsButton");
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(BorderBrushProperty, "BorderBrush");
        _text.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush"); _text.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid(); header.Children.Add(_title); header.Children.Add(_close); grid.Children.Add(header);
        Grid.SetRow(_text, 1); _text.Margin = new(0, 12, 0, 0); grid.Children.Add(_text);
        Grid.SetRow(_link, 2); grid.Children.Add(_link); Child = grid;
        _close.Click += (_, _) => Dismiss();
        _link.Click += (_, _) => { if (_uri is not null) try { Process.Start(new ProcessStartInfo(_uri.AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { _link.IsEnabled = false; } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Dismiss(); } };
        Unloaded += (_, _) => { Cancel(); Visibility = Visibility.Collapsed; };
        IsVisibleChanged += (_, _) => { if (!IsVisible) { Cancel(); Visibility = Visibility.Collapsed; } };
        Visibility = Visibility.Collapsed;
    }
    public async Task OpenAsync(UpdateItem item, FrameworkElement anchor)
    {
        Cancel(); var request = _request = new CancellationTokenSource(); _anchor = anchor; _item = item;
        _title.Text = LocalizationService.Text("Novità della versione", "Release notes");
        _close.ToolTip = LocalizationService.Text("Chiudi", "Close");
        System.Windows.Automation.AutomationProperties.SetName(_close, (string)_close.ToolTip);
        System.Windows.Automation.AutomationProperties.SetName(_text, _title.Text);
        _link.Content = LocalizationService.Text("Apri note ufficiali", "Open official notes");
        _link.Visibility = Visibility.Collapsed; _uri = null;
        var heading = $"{item.Name}\n{LocalizationService.Text("Installata", "Installed")}: {item.InstalledVersion}\n{LocalizationService.Text("Disponibile", "Available")}: {item.AvailableVersion}\n{LocalizationService.Text("Origine", "Source")}: {item.Source}\n\n";
        _text.Text = heading + LocalizationService.Text("Caricamento…", "Loading…");
        Visibility = Visibility.Visible; UiMotion.Reveal(this); _close.Focus();
        try
        {
            var notes = await Reader.GetAsync(item, LocalizationService.CurrentLanguage, request.Token);
            if (!ReferenceEquals(_request, request) || request.IsCancellationRequested) return;
            _text.Text = heading + (notes.Text.Length > 0 ? notes.Text : LocalizationService.Text("Note di rilascio non disponibili per questa versione.", "Release notes are not available for this version."));
            if (notes.Locale.Length > 0) _text.Text += "\n\n" + LocalizationService.Text("Lingua delle note: ", "Notes language: ") + notes.Locale;
            if (notes.Description.Length > 0) _text.Text += "\n\n" + LocalizationService.Text("Descrizione del provider", "Provider description") + "\n" + notes.Description;
            _text.ScrollToHome(); _uri = ReleaseNotesService.SafeLink(notes.Url);
            _link.Visibility = _uri is null ? Visibility.Collapsed : Visibility.Visible; _link.IsEnabled = true;
        }
        catch (OperationCanceledException) { }
    }
    public void Dismiss() { Cancel(); UiMotion.Hide(this); if (_anchor is { IsVisible: true, IsEnabled: true } && ReferenceEquals(_anchor.DataContext, _item)) _anchor.Focus(); }
    private void Cancel() { _request?.Cancel(); _request?.Dispose(); _request = null; }
}

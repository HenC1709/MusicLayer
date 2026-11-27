namespace MusicLayer.Presentation.Maui;

public partial class TonicHeader : ContentView
{
    public TonicHeader() => InitializeComponent();

    public event EventHandler? SearchRequested;
    public event EventHandler? ImportRequested;

    public bool ShowLibraryActions
    {
        get => ImportAction.IsVisible;
        set => SearchAction.IsVisible = ImportAction.IsVisible = value;
    }

    public void SetLibraryActionsEnabled(bool enabled) =>
        SearchAction.IsEnabled = ImportAction.IsEnabled = enabled;

    private void OpenMenu(object? sender, EventArgs e)
    {
        if (Shell.Current is { } shell) shell.FlyoutIsPresented = true;
    }

    private void OnSearch(object? sender, EventArgs e) => SearchRequested?.Invoke(this, e);
    private void OnImport(object? sender, EventArgs e) => ImportRequested?.Invoke(this, e);
}

namespace MusicLayer.Presentation.Maui;

public partial class AppShell : Shell
{
    private static readonly Color Primary = Color.FromArgb("#F2F8F7");
    private static readonly Color Secondary = Color.FromArgb("#9AB9B5");
    private static readonly Color Accent = Color.FromArgb("#25E8A4");
    private readonly FlyoutItem main = new() { Route = "principal", Title = "Tonic" };
    private readonly Dictionary<Tab, Grid> menuRows = [];

    public AppShell(LibraryPage library, PlaybackPage playbackPage, Platforms.Android.AndroidPlayback playback)
    {
        InitializeComponent();

        var menu = new VerticalStackLayout { Padding = new Thickness(16), Spacing = 6 };
        menu.Children.Add(CreateMenuHeader());
        var libraryTab = AddTab(menu, "biblioteca", "Biblioteca", "sixteenth_notes.png",
            "Explorá tu música, artistas y álbumes", () => library);
        var playbackTab = AddTab(menu, "reproducir", "Reproducir", "play.png",
            "Controlá la pista activa y la cola", () => playbackPage);
        library.PlaybackRequested += async (_, _) => await SelectTabAsync(playbackTab);
        playbackPage.LibraryRequested += async (_, _) => await SelectTabAsync(libraryTab);
        AddPendingTab("letras", "Letras", "audio_lines.png", "Seguí la letra de la pista activa");
        AddPendingTab("playlists", "Playlists", "list_music.png", "Organizá tu música en listas");
        menu.Children.Add(CreateMenuRow("Importar música", "Elegí archivos para tu biblioteca", "folder_open.png", async () =>
        {
            await SelectTabAsync(libraryTab);
            await library.ImportSongsAsync();
        }));
        AddPendingTab("ajustes", "Ajustes", "settings.png", "Configurá tu biblioteca y el audio");

        var drawer = new ScrollView { Content = menu };
        drawer.SafeAreaEdges = SafeAreaEdges.All;
        FlyoutContent = drawer;
        Items.Add(main);
        Navigated += (_, _) => UpdateMenuSelection();
        UpdateMenuSelection();

        void AddPendingTab(string route, string title, string icon, string description) =>
            AddTab(menu, route, title, icon, description, () => CreatePendingPage(title, playback, () => SelectTabAsync(playbackTab)));
    }

    private Tab AddTab(VerticalStackLayout menu, string route, string title, string icon, string description,
        Func<ContentPage> createPage)
    {
        var tab = new Tab { Title = title, Icon = icon, Route = route };
        tab.Items.Add(new ShellContent { Route = $"{route}-inicio", ContentTemplate = new DataTemplate(() => createPage()) });
        main.Items.Add(tab);
        var row = CreateMenuRow(title, description, icon, () => SelectTabAsync(tab));
        menuRows.Add(tab, row);
        menu.Children.Add(row);
        return tab;
    }

    private async Task SelectTabAsync(Tab tab)
    {
        FlyoutIsPresented = false;
        if (main.CurrentItem != tab)
            await GoToAsync($"//{main.Route}/{tab.Route}/{tab.Items[0].Route}", animate: false);
    }

    private void UpdateMenuSelection()
    {
        foreach (var (tab, row) in menuRows)
            row.BackgroundColor = main.CurrentItem == tab ? Color.FromArgb("#103832") : Colors.Transparent;
    }

    private Grid CreateMenuHeader()
    {
        var header = new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 12)
        };
        header.Add(new Image { Source = "tonic_desktop.png", WidthRequest = 42, HeightRequest = 42 });
        var wordmark = new Label { Text = "Tonic" };
        wordmark.SetDynamicResource(VisualElement.StyleProperty, "TonicWordmark");
        header.Add(wordmark, 1);
        var close = new Button
        {
            Text = "×", FontSize = 22, WidthRequest = 44, HeightRequest = 44, Padding = 0,
            CornerRadius = 12, BackgroundColor = Color.FromArgb("#0B1D22"), TextColor = Primary
        };
        SemanticProperties.SetDescription(close, "Cerrar menú");
        close.Clicked += (_, _) => FlyoutIsPresented = false;
        header.Add(close, 2);
        return header;
    }

    private static Grid CreateMenuRow(string title, string description, string icon, Func<Task> open)
    {
        var row = new Grid { MinimumHeightRequest = 56 };
        var content = new Grid
        {
            Padding = new Thickness(10, 6), ColumnSpacing = 10, InputTransparent = true,
            ColumnDefinitions = [new(new GridLength(26)), new(GridLength.Star)]
        };
        content.Add(new Image { Source = icon, WidthRequest = 22, HeightRequest = 22,
            VerticalOptions = LayoutOptions.Center });
        var labels = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        labels.Children.Add(new Label { Text = title, TextColor = Primary, FontSize = 14, FontAttributes = FontAttributes.Bold });
        labels.Children.Add(new Label { Text = description, TextColor = Secondary, FontSize = 12 });
        content.Add(labels, 1);
        var action = new Button { BackgroundColor = Colors.Transparent, MinimumHeightRequest = 44, Padding = 0 };
        SemanticProperties.SetDescription(action, $"{title}. {description}");
        action.Clicked += async (_, _) => await open();
        row.Children.Add(content);
        row.Children.Add(action);
        return row;
    }

    private static ContentPage CreatePendingPage(string heading, Platforms.Android.AndroidPlayback playback, Func<Task> openPlayback)
    {
        var title = new Label { Text = heading, TextColor = Primary, FontSize = 26, FontAttributes = FontAttributes.Bold };
        SemanticProperties.SetHeadingLevel(title, SemanticHeadingLevel.Level1);
        var mini = new MiniPlayer();
        mini.Connect(playback);
        mini.OpenRequested += async (_, _) => await openPlayback();
        var layout = new Grid
        {
            Padding = new Thickness(16, 8), RowSpacing = 14,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)]
        };
        layout.Add(new TonicHeader());
        layout.Add(title, 0, 1);
        layout.Add(new Label { Text = "Disponible próximamente.", TextColor = Accent, FontSize = 15 }, 0, 2);
        layout.Add(mini, 0, 3);
        return new ContentPage
        {
            Title = heading,
            BackgroundColor = Color.FromArgb("#050D10"),
            SafeAreaEdges = SafeAreaEdges.All,
            Content = layout
        };
    }
}

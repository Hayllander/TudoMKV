using Avalonia.Controls;
using Avalonia.Interactivity;
using TudoMKV.App.Configuration;

namespace TudoMKV.App.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
        LoadSettings(AppSettingsStore.Load());
    }

    private void LoadSettings(AppSettings settings)
    {
        ProviderCombo.SelectedIndex = settings.Integration.Provider switch
        {
            MetadataProvider.Tmdb => 1,
            _ => 0,
        };
        TmdbKeyBox.Text = settings.Integration.TmdbApiKey;
        ApiDataBox.Text = settings.Integration.ApiData;
        Mpeg2CompatBox.IsChecked = settings.Muxing.MarkMpeg2ForVlc;
        ReleaseGroupBox.Text = settings.Muxing.ReleaseGroup;
        ScreenshotsBox.IsChecked = settings.Muxing.GenerateScreenshots;
    }

    private AppSettings CurrentSettings() => new()
    {
        Integration = new IntegrationSettings
        {
            Provider = ProviderCombo.SelectedIndex == 1 ? MetadataProvider.Tmdb : MetadataProvider.None,
            TmdbApiKey = TmdbKeyBox.Text?.Trim() ?? string.Empty,
            ApiData = ApiDataBox.Text?.Trim() ?? string.Empty,
        },
        Muxing = new MuxingSettings
        {
            MarkMpeg2ForVlc = Mpeg2CompatBox.IsChecked ?? true,
            ReleaseGroup = ReleaseGroupBox.Text?.Trim() ?? string.Empty,
            GenerateScreenshots = ScreenshotsBox.IsChecked ?? false,
        },
    };

    private void SaveSettings() => AppSettingsStore.Save(CurrentSettings());

    private void OnApplyClick(object? sender, RoutedEventArgs e) => SaveSettings();

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}

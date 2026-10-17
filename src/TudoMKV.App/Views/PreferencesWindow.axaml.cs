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
            MetadataProvider.Omdb => 1,
            _ => 0,
        };
        ApiDataBox.Text = settings.Integration.ApiData;
    }

    private AppSettings CurrentSettings() => new()
    {
        Integration = new IntegrationSettings
        {
            Provider = ProviderCombo.SelectedIndex == 1 ? MetadataProvider.Omdb : MetadataProvider.None,
            ApiData = ApiDataBox.Text?.Trim() ?? string.Empty,
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

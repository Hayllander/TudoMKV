using System.Text.Json;

namespace TudoMKV.App.Configuration;

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TudoMKV",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return Migrate(JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings());
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Antes do TMDb o padrão era "Nenhum", gravado por qualquer salvamento das Preferências; esse valor de um
    /// arquivo antigo não foi uma escolha, então passa a ser o TMDb, como numa instalação nova.
    /// </summary>
    public static AppSettings Migrate(AppSettings settings)
    {
        if (settings.SchemaVersion < 2 && settings.Integration.Provider == MetadataProvider.None)
        {
            settings.Integration.Provider = MetadataProvider.Tmdb;
        }

        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        return settings;
    }

    public static void Save(AppSettings settings)
    {
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}

using TudoMKV.App.Configuration;

namespace TudoMKV.Tests;

public class AppSettingsMigrationTests
{
    [Fact]
    public void AnOldFileWithNoProviderChosenMovesToTmdb()
    {
        var old = new AppSettings { Integration = new IntegrationSettings { Provider = MetadataProvider.None } };

        var migrated = AppSettingsStore.Migrate(old);

        Assert.Equal(MetadataProvider.Tmdb, migrated.Integration.Provider);
        Assert.Equal(AppSettings.CurrentSchemaVersion, migrated.SchemaVersion);
    }

    [Fact]
    public void KeepsNoneWhenItWasChosenInTheCurrentFormat()
    {
        var current = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            Integration = new IntegrationSettings { Provider = MetadataProvider.None },
        };

        Assert.Equal(MetadataProvider.None, AppSettingsStore.Migrate(current).Integration.Provider);
    }
}

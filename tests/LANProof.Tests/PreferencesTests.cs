using LANProof.Runtime;

namespace LANProof.Tests;

public sealed class PreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LANProof-prefs-" + Guid.NewGuid().ToString("N"));
    public PreferencesTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Fact] public void MissingAppearanceDefaultsToDark() => Assert.True(ThemePreferences.Load(Path.Combine(_directory,"appearance.json")).DarkMode);
    [Fact] public async Task LightAndDarkSelectionSurviveRestart()
    {
        var path = Path.Combine(_directory,"appearance.json");
        await new ThemePreferences { DarkMode=false }.SaveAsync(path);
        Assert.False(ThemePreferences.Load(path).DarkMode);
        await new ThemePreferences { DarkMode=true }.SaveAsync(path);
        Assert.True(ThemePreferences.Load(path).DarkMode);
    }
    [Fact] public void InvalidAppearanceFallsBackToDark()
    {
        var path = Path.Combine(_directory,"appearance.json"); File.WriteAllText(path,"broken");
        Assert.True(ThemePreferences.Load(path).DarkMode);
    }
    [Fact] public void DataStaysAtProjectRootForBuildAndPublishedExecutables()
    {
        File.WriteAllText(Path.Combine(_directory,"LANProof.sln"), "test marker");
        var nested = Path.Combine(_directory,"publish","Windows"); Directory.CreateDirectory(nested);
        Assert.Equal(Path.Combine(_directory,"data"), ApplicationPaths.ResolveDataDirectory(nested));
    }
    [Fact] public void ExtractedDistributionUsesDataBesideExecutable()
    {
        Assert.Equal(Path.Combine(_directory,"data"),ApplicationPaths.ResolveDataDirectory(_directory));
    }
}

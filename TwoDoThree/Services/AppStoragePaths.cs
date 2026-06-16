using System.IO;

namespace TwoDoThree.Services;

public static class AppStoragePaths
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "2do3");

    public static string SettingsFilePath => Path.Combine(RootDirectory, "settings.json");

    public static string EmailCacheFilePath => Path.Combine(RootDirectory, "email-cache.json");

    public static string MsalCacheFileName => "msal.cache";

    public static void EnsureRootDirectory()
    {
        Directory.CreateDirectory(RootDirectory);
    }
}

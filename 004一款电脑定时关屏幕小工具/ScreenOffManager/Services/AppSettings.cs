using System.IO;
using System.Text.Json;

namespace ScreenOffManager.Services;

/// <summary>
/// 用户配置持久化：%AppData%\ScreenOffManager\settings.json
/// </summary>
public class AppSettings
{
    /// <summary>上次使用的自定义分钟数</summary>
    public string LastCustomMinutes { get; set; } = "10";

    private static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenOffManager");

    private static string FilePath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception ex)
        {
            Log.Error("读取配置失败，使用默认配置", ex);
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error("保存配置失败", ex);
        }
    }
}

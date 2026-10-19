using System.Text.Json;

namespace Modeler;

/// <summary>
/// Локальные настройки окна (пока только число сглаживания). Лежат в профиле
/// пользователя, а не в репозитории: на каждой машине свои.
/// </summary>
public sealed class Settings
{
    public int Msaa { get; set; } = 4;
    public int Ssaa { get; set; } = 3;

    /// <summary>Верхний предел частоты кадров при взаимодействии. 0 — без ограничения.</summary>
    public int Fps { get; set; } = 60;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Modeler", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Settings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
        }
        catch
        {
            // Битый или недоступный файл не должен мешать открыть программу.
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Нет прав или диск занят — работаем дальше без сохранения.
        }
    }
}

using System;
using System.IO;
using UnityEngine;

// Instellingen van de arcadekast. Staan in arcade-config.json naast ArcadeFirda.exe
// (in de editor: in de hoofdmap van het Unity-project). Dit bestand staat niet in Git,
// zodat het wachtwoord niet op GitHub komt. Zie arcade-config.example.json.
[Serializable]
public class ArcadeConfig
{
    public const string FileName = "arcade-config.json";

    // Map met de games, elke game in een eigen submap.
    public string gamesRoot = @"C:\Nonsteamgames\Imported";

    // Wachtwoord voor de knop Desktop. Leeg = de knop werkt niet.
    public string desktopPassword = "";

    // Aantal seconden zonder input voordat de slideshow start.
    public float idleSeconds = 120f;

    // Tijdstip (HH:mm) waarop de pc automatisch uitgaat, bijvoorbeeld "18:00". Leeg = nooit.
    public string shutdownTime = "";

    public static string ConfigPath
    {
        // Application.dataPath is <map van de exe>/ArcadeFirda_Data, of <project>/Assets in de editor.
        get { return Path.Combine(Path.GetDirectoryName(Application.dataPath), FileName); }
    }

    public static ArcadeConfig Load()
    {
        string path = ConfigPath;
        var config = new ArcadeConfig();

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[ArcadeConfig] {path} bestaat niet, er wordt een standaardbestand aangemaakt.");
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(config, true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ArcadeConfig] Kon {path} niet aanmaken: {ex.Message}");
            }
            return config;
        }

        try
        {
            JsonUtility.FromJsonOverwrite(File.ReadAllText(path), config);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ArcadeConfig] Fout in {path}, standaardinstellingen worden gebruikt: {ex.Message}");
        }
        return config;
    }

    // Geeft het tijdstip van vandaag terug waarop de pc uit moet, of null als dat niet is ingesteld.
    public DateTime? GetShutdownTimeToday()
    {
        if (string.IsNullOrWhiteSpace(shutdownTime))
            return null;

        if (TimeSpan.TryParse(shutdownTime.Trim(), out TimeSpan time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1))
            return DateTime.Today + time;

        Debug.LogWarning($"[ArcadeConfig] shutdownTime '{shutdownTime}' is geen geldig tijdstip (gebruik HH:mm).");
        return null;
    }
}

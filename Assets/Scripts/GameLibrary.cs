using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

// Inhoud van arcade/game.json in de map van een game. Alle velden zijn optioneel.
[Serializable]
public class GameMetadata
{
    public int formatVersion = 1;
    public string id;
    public string title;
    public string exe;
    public string[] makers;
    public int year;
    public string group;
    public int playersMin;
    public int playersMax;
    public string genre;
    public string description;
    public string controls;
    public string preview;
    public string[] screenshots;
}

// Een gevonden game, met de gegevens die het menu nodig heeft.
public class GameEntry
{
    public string FolderPath;
    public string ExePath;
    public GameMetadata Metadata;
    public Texture2D Preview;
    public bool HasPreview;

    public string Id
    {
        get { return string.IsNullOrWhiteSpace(Metadata.id) ? Path.GetFileName(FolderPath) : Metadata.id.Trim(); }
    }

    public string Title
    {
        get { return string.IsNullOrWhiteSpace(Metadata.title) ? Path.GetFileName(FolderPath) : Metadata.title.Trim(); }
    }

    // "Jan de Vries, Sara Bakker · 2025"
    public string MakersAndYear
    {
        get
        {
            var parts = new List<string>();
            if (Metadata.makers != null && Metadata.makers.Length > 0)
                parts.Add(string.Join(", ", Metadata.makers));
            if (Metadata.year > 0)
                parts.Add(Metadata.year.ToString());
            return string.Join(" · ", parts);
        }
    }

    // "1-2 spelers · Shooter"
    public string PlayersAndGenre
    {
        get
        {
            var parts = new List<string>();
            int min = Metadata.playersMin;
            int max = Mathf.Max(Metadata.playersMax, min);
            if (min > 0 && max > min)
                parts.Add($"{min}-{max} spelers");
            else if (max > 0)
                parts.Add(max == 1 ? "1 speler" : $"{max} spelers");
            if (!string.IsNullOrWhiteSpace(Metadata.genre))
                parts.Add(Metadata.genre.Trim());
            return string.Join(" · ", parts);
        }
    }

    public string ArcadeFolder
    {
        get { return Path.Combine(FolderPath, GameLibrary.ArcadeFolderName); }
    }
}

// Zoekt games in de gamemap en leest hun arcade-map in.
public static class GameLibrary
{
    public const string ArcadeFolderName = "arcade";
    public const string MetadataFileName = "game.json";

    private static readonly string[] DefaultPreviewNames = { "preview.png", "preview.jpg", "preview.jpeg" };

    private static readonly Regex UnityHelperExePattern = new Regex(
        @"^(unitycrashhandler(32|64)?|unityplayer)\.exe$",
        RegexOptions.IgnoreCase
    );

    public static List<GameEntry> Scan(string root)
    {
        var games = new List<GameEntry>();
        if (!Directory.Exists(root))
            return games;

        foreach (string folderPath in Directory.GetDirectories(root))
        {
            try
            {
                GameEntry game = LoadGame(folderPath);
                if (game != null)
                    games.Add(game);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameLibrary] Kon {folderPath} niet inlezen: {ex.Message}");
            }
        }

        games.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
        return games;
    }

    private static GameEntry LoadGame(string folderPath)
    {
        var game = new GameEntry
        {
            FolderPath = folderPath,
            Metadata = LoadMetadata(folderPath)
        };

        string exeName = GetExeName(folderPath, game.Metadata.exe);
        if (exeName == null)
            return null;
        game.ExePath = Path.Combine(folderPath, exeName);

        game.Preview = LoadPreview(game);
        game.HasPreview = game.Preview != null;
        if (!game.HasPreview)
            game.Preview = CreatePlaceholder(game.Title);

        return game;
    }

    private static GameMetadata LoadMetadata(string folderPath)
    {
        var metadata = new GameMetadata();
        string path = Path.Combine(folderPath, ArcadeFolderName, MetadataFileName);
        if (!File.Exists(path))
            return metadata;

        try
        {
            JsonUtility.FromJsonOverwrite(File.ReadAllText(path), metadata);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GameLibrary] Fout in {path}: {ex.Message}");
        }
        return metadata;
    }

    // Kiest de exe van de game: eerst die uit game.json, dan een exe met een bijbehorende
    // <naam>_Data-map (zo bouwt Unity), en anders de eerste exe die geen Unity-hulpprogramma is.
    private static string GetExeName(string folderPath, string preferredExe)
    {
        if (!string.IsNullOrWhiteSpace(preferredExe) && File.Exists(Path.Combine(folderPath, preferredExe.Trim())))
            return preferredExe.Trim();

        var candidates = new List<string>();
        foreach (string fullExe in Directory.GetFiles(folderPath, "*.exe", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(fullExe);
            if (!UnityHelperExePattern.IsMatch(fileName))
                candidates.Add(fileName);
        }

        if (candidates.Count == 0)
            return null;

        candidates.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in candidates)
        {
            string dataFolder = Path.Combine(folderPath, Path.GetFileNameWithoutExtension(candidate) + "_Data");
            if (Directory.Exists(dataFolder))
                return candidate;
        }
        return candidates[0];
    }

    private static Texture2D LoadPreview(GameEntry game)
    {
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(game.Metadata.preview))
            names.Add(game.Metadata.preview.Trim());
        names.AddRange(DefaultPreviewNames);

        foreach (string name in names)
        {
            Texture2D texture = LoadImage(Path.Combine(game.ArcadeFolder, name));
            if (texture != null)
                return texture;
        }
        return null;
    }

    public static Texture2D LoadImage(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (texture.LoadImage(File.ReadAllBytes(path)))
            {
                texture.name = Path.GetFileName(path);
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }
            UnityEngine.Object.Destroy(texture);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GameLibrary] Kon afbeelding {path} niet laden: {ex.Message}");
        }
        return null;
    }

    // Effen afbeelding met een kleur die uit de titel volgt, zodat elke game zonder preview
    // toch een eigen, herkenbare kleur heeft. De titel wordt er door het menu overheen gezet.
    private static Texture2D CreatePlaceholder(string title)
    {
        int hash = 17;
        foreach (char c in title)
            hash = hash * 31 + c;
        float hue = (hash & 0xFFFF) / 65535f;

        Color top = Color.HSVToRGB(hue, 0.65f, 0.85f);
        Color bottom = Color.HSVToRGB(hue, 0.8f, 0.45f);

        const int width = 16, height = 9;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Placeholder " + title,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        for (int y = 0; y < height; y++)
        {
            Color color = Color.Lerp(bottom, top, y / (float)(height - 1));
            for (int x = 0; x < width; x++)
                texture.SetPixel(x, y, color);
        }
        texture.Apply();
        return texture;
    }

    // Laat een afbeelding een vlak van de gegeven verhouding vullen door het midden uit te snijden.
    public static Rect GetCropUv(Texture texture, float targetAspect)
    {
        if (texture == null || texture.height == 0 || targetAspect <= 0f)
            return new Rect(0, 0, 1, 1);

        float textureAspect = texture.width / (float)texture.height;
        if (textureAspect > targetAspect)
        {
            float width = targetAspect / textureAspect;
            return new Rect((1f - width) / 2f, 0, width, 1);
        }
        float height = textureAspect / targetAspect;
        return new Rect(0, (1f - height) / 2f, 1, height);
    }

    public static void DestroyTextures(List<GameEntry> games)
    {
        foreach (GameEntry game in games)
        {
            if (game.Preview != null)
                UnityEngine.Object.Destroy(game.Preview);
        }
    }
}

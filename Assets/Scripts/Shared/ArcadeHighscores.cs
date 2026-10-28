// ArcadeHighscores.cs - highscores voor de arcadekast van Firda Drachten.
//
// Kopieer dit bestand naar je eigen Unity-project (bijvoorbeeld Assets/Scripts/) en roep
// ArcadeHighscores.Submit aan als een speler klaar is:
//
//     ArcadeHighscores.Submit("AAA", score);                  // naar het eerste board
//     ArcadeHighscores.Submit("AAA", tijd, "time-attack");    // naar een board per gamemode
//
// Wil je een board met een eigen titel, label of sorteerrichting (laagste wint, bijvoorbeeld
// bij een racetijd)? Roep dan bij het opstarten van je game DefineBoard aan:
//
//     ArcadeHighscores.DefineBoard("time-attack", "Time Attack", "Seconden", ArcadeHighscores.SortOrder.LowestFirst);
//
// De scores komen in arcade/highscores.json naast de .exe van je game. Het menu van de kast
// leest dat bestand en toont de scores. In de Unity-editor wordt een testbestand in
// Application.persistentDataPath gebruikt, zodat je het kunt proberen zonder build.
//
// Dit bestand gebruikt alleen UnityEngine en System, er zijn geen extra packages nodig.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

[Serializable]
public class ArcadeHighscoreEntry
{
    public string name;
    public double score;
    public string date;
}

[Serializable]
public class ArcadeHighscoreBoard
{
    public string id;
    public string title;
    public string scoreLabel = "Punten";
    // "desc" = hoogste score wint, "asc" = laagste score wint (bijvoorbeeld een tijd)
    public string sortOrder = "desc";
    public List<ArcadeHighscoreEntry> entries = new List<ArcadeHighscoreEntry>();

    public bool LowestFirst
    {
        get { return string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase); }
    }

    public void Sort()
    {
        if (entries == null)
            entries = new List<ArcadeHighscoreEntry>();
        entries.RemoveAll(e => e == null);
        bool lowestFirst = LowestFirst;
        entries.Sort((a, b) => lowestFirst ? a.score.CompareTo(b.score) : b.score.CompareTo(a.score));
    }
}

[Serializable]
public class ArcadeHighscoreFile
{
    public int formatVersion = 1;
    public List<ArcadeHighscoreBoard> boards = new List<ArcadeHighscoreBoard>();

    public bool HasScores
    {
        get
        {
            if (boards == null)
                return false;
            foreach (ArcadeHighscoreBoard board in boards)
            {
                if (board != null && board.entries != null && board.entries.Count > 0)
                    return true;
            }
            return false;
        }
    }
}

public static class ArcadeHighscores
{
    public enum SortOrder { HighestFirst, LowestFirst }

    public const string FileName = "highscores.json";

    // Hoeveel scores er per board bewaard worden.
    public static int MaxEntriesPerBoard = 10;

    // Langere namen worden afgekapt.
    public const int MaxNameLength = 12;

    public static string FilePath
    {
        get
        {
#if UNITY_EDITOR
            return Path.Combine(Application.persistentDataPath, "arcade-test", FileName);
#else
            // Application.dataPath is <map van de exe>/<Naam>_Data
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), "arcade", FileName);
#endif
        }
    }

    // Maakt een board aan of past titel, label en sorteerrichting aan. Bestaande scores blijven staan.
    public static void DefineBoard(string boardId, string title, string scoreLabel = "Punten", SortOrder sortOrder = SortOrder.HighestFirst)
    {
        ArcadeHighscoreFile file = Load();
        ArcadeHighscoreBoard board = GetOrCreateBoard(file, boardId);
        board.title = title;
        board.scoreLabel = scoreLabel;
        board.sortOrder = sortOrder == SortOrder.LowestFirst ? "asc" : "desc";
        board.Sort();
        Save(file);
    }

    // Voegt een score toe. Geeft de plek in de lijst terug (1 = beste), of 0 als de score de lijst niet haalt.
    public static int Submit(string playerName, double score, string boardId = null)
    {
        ArcadeHighscoreFile file = Load();
        ArcadeHighscoreBoard board = GetOrCreateBoard(file, boardId);

        var entry = new ArcadeHighscoreEntry
        {
            name = CleanName(playerName),
            score = score,
            date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
        board.entries.Add(entry);
        board.Sort();

        int rank = board.entries.IndexOf(entry) + 1;
        if (board.entries.Count > MaxEntriesPerBoard)
            board.entries.RemoveRange(MaxEntriesPerBoard, board.entries.Count - MaxEntriesPerBoard);
        if (rank > MaxEntriesPerBoard)
            rank = 0;

        Save(file);
        return rank;
    }

    // Haalt de lijst met scores van een board op (beste eerst). Zonder boardId: het eerste board.
    public static List<ArcadeHighscoreEntry> GetScores(string boardId = null)
    {
        ArcadeHighscoreBoard board = FindBoard(Load(), boardId);
        return board != null ? new List<ArcadeHighscoreEntry>(board.entries) : new List<ArcadeHighscoreEntry>();
    }

    // Kijkt of een score de lijst zou halen, bijvoorbeeld om te bepalen of je om een naam vraagt.
    public static bool IsHighscore(double score, string boardId = null)
    {
        ArcadeHighscoreBoard board = FindBoard(Load(), boardId);
        if (board == null || board.entries.Count < MaxEntriesPerBoard)
            return true;
        double worst = board.entries[board.entries.Count - 1].score;
        return board.LowestFirst ? score < worst : score > worst;
    }

    public static ArcadeHighscoreFile Load()
    {
        return LoadFrom(FilePath) ?? new ArcadeHighscoreFile();
    }

    // Leest een highscorebestand. Geeft null terug als het bestand niet bestaat of niet te lezen is.
    public static ArcadeHighscoreFile LoadFrom(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            ArcadeHighscoreFile file = JsonUtility.FromJson<ArcadeHighscoreFile>(File.ReadAllText(path));
            if (file == null)
                return null;
            if (file.boards == null)
                file.boards = new List<ArcadeHighscoreBoard>();
            file.boards.RemoveAll(b => b == null);
            foreach (ArcadeHighscoreBoard board in file.boards)
                board.Sort();
            return file;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArcadeHighscores] Kon {path} niet lezen: {ex.Message}");
            return null;
        }
    }

    public static void Save(ArcadeHighscoreFile file)
    {
        SaveTo(file, FilePath);
    }

    // Schrijft eerst naar een tijdelijk bestand, zodat er nooit een half geschreven bestand ontstaat.
    public static void SaveTo(ArcadeHighscoreFile file, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonUtility.ToJson(file, true));
            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ArcadeHighscores] Kon {path} niet opslaan: {ex.Message}");
        }
    }

    private static ArcadeHighscoreBoard FindBoard(ArcadeHighscoreFile file, string boardId)
    {
        if (file.boards.Count == 0)
            return null;
        if (string.IsNullOrEmpty(boardId))
            return file.boards[0];
        return file.boards.Find(b => b.id == boardId);
    }

    private static ArcadeHighscoreBoard GetOrCreateBoard(ArcadeHighscoreFile file, string boardId)
    {
        ArcadeHighscoreBoard board = FindBoard(file, boardId);
        if (board != null)
            return board;

        string id = string.IsNullOrEmpty(boardId) ? "default" : boardId;
        board = new ArcadeHighscoreBoard { id = id, title = string.IsNullOrEmpty(boardId) ? "" : boardId };
        file.boards.Add(board);
        return board;
    }

    private static string CleanName(string playerName)
    {
        string name = string.IsNullOrWhiteSpace(playerName) ? "???" : playerName.Trim();
        return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;
    }
}

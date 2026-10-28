using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Toont de highscores van de geselecteerde game over het grote plaatje, nadat de speler een paar
// seconden op dezelfde game blijft staan. Een lange lijst scrolt langzaam; meerdere boards wisselen elkaar af.
public class LeaderboardOverlay : MonoBehaviour
{
    public float showDelay = 4f;
    public float boardDuration = 8f;
    public float scrollSpeed = 40f;   // pixels per seconde
    public float scrollPause = 2f;    // seconden stilstaan bovenaan en onderaan
    public float fadeDuration = 0.4f;

    private static readonly Color Gold = new Color(1f, 0.84f, 0.29f);
    private static readonly Color Silver = new Color(0.82f, 0.82f, 0.86f);
    private static readonly Color Bronze = new Color(0.88f, 0.58f, 0.35f);

    private CanvasGroup overlayGroup;
    private CanvasGroup boardGroup;
    private RectTransform viewport;
    private TextMeshProUGUI headerText;
    private TextMeshProUGUI rowsText;

    private List<ArcadeHighscoreBoard> boards = new List<ArcadeHighscoreBoard>();
    private float selectedAt;
    private bool started;
    private int boardIndex;
    private float boardStartedAt;

    // Bouwt de overlay als kind van het grote plaatje, in de stijl van de bestaande teksten.
    public static LeaderboardOverlay Create(RectTransform parent, TextMeshProUGUI style)
    {
        GameObject root = CreateChild("Leaderboard", parent);
        Stretch((RectTransform)root.transform, Vector2.zero, Vector2.zero);

        var background = root.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.8f);
        background.raycastTarget = false;
        var parentCorners = parent.GetComponent<ImageWithRoundedCorners>();
        if (parentCorners != null)
        {
            var corners = root.AddComponent<ImageWithRoundedCorners>();
            corners.radius = parentCorners.radius;
            corners.Validate();
            corners.Refresh();
        }

        var overlay = root.AddComponent<LeaderboardOverlay>();
        overlay.overlayGroup = root.AddComponent<CanvasGroup>();
        overlay.overlayGroup.alpha = 0f;
        overlay.overlayGroup.interactable = false;
        overlay.overlayGroup.blocksRaycasts = false;

        GameObject board = CreateChild("Board", root.transform);
        Stretch((RectTransform)board.transform, Vector2.zero, Vector2.zero);
        overlay.boardGroup = board.AddComponent<CanvasGroup>();

        GameObject header = CreateChild("Header", board.transform);
        var headerRect = (RectTransform)header.transform;
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1);
        headerRect.offsetMin = new Vector2(60, -110);
        headerRect.offsetMax = new Vector2(-60, -25);
        overlay.headerText = CreateText(header, style, 54, TextAlignmentOptions.Center);
        overlay.headerText.fontStyle = FontStyles.Bold;
        overlay.headerText.enableAutoSizing = true;
        overlay.headerText.fontSizeMin = 24;
        overlay.headerText.fontSizeMax = 54;
        overlay.headerText.color = Gold;

        GameObject viewportObject = CreateChild("Viewport", board.transform);
        overlay.viewport = (RectTransform)viewportObject.transform;
        Stretch(overlay.viewport, new Vector2(80, 35), new Vector2(-80, -125));
        viewportObject.AddComponent<RectMask2D>();

        GameObject rows = CreateChild("Rows", viewportObject.transform);
        var rowsRect = (RectTransform)rows.transform;
        rowsRect.anchorMin = new Vector2(0, 1);
        rowsRect.anchorMax = new Vector2(1, 1);
        rowsRect.pivot = new Vector2(0.5f, 1);
        rowsRect.anchoredPosition = Vector2.zero;
        overlay.rowsText = CreateText(rows, style, 40, TextAlignmentOptions.TopLeft);
        overlay.rowsText.textWrappingMode = TextWrappingModes.NoWrap;
        overlay.rowsText.overflowMode = TextOverflowModes.Overflow;

        return overlay;
    }

    public void Show(GameEntry game)
    {
        boards.Clear();
        if (game != null && game.Highscores != null && game.Highscores.boards != null)
        {
            foreach (ArcadeHighscoreBoard board in game.Highscores.boards)
            {
                if (board.entries != null && board.entries.Count > 0)
                    boards.Add(board);
            }
        }
        selectedAt = Time.unscaledTime;
        started = false;
        overlayGroup.alpha = 0f;
    }

    public void Hide()
    {
        boards.Clear();
        started = false;
        overlayGroup.alpha = 0f;
    }

    private void Update()
    {
        if (boards.Count == 0 || Time.unscaledTime - selectedAt < showDelay)
        {
            overlayGroup.alpha = 0f;
            return;
        }

        if (!started)
        {
            started = true;
            ShowBoard(0);
        }

        overlayGroup.alpha = Mathf.MoveTowards(overlayGroup.alpha, 1f, Time.unscaledDeltaTime / fadeDuration);

        float boardTime = Time.unscaledTime - boardStartedAt;
        float overflow = Mathf.Max(0f, rowsText.rectTransform.rect.height - viewport.rect.height);
        float scrollTime = overflow / scrollSpeed;

        // Bovenaan even stilstaan, dan naar beneden scrollen, en onderaan weer even stilstaan.
        float scroll = Mathf.Clamp((boardTime - scrollPause) * scrollSpeed, 0f, overflow);
        rowsText.rectTransform.anchoredPosition = new Vector2(0f, scroll);

        bool cycles = boards.Count > 1 || overflow > 0f;
        float duration = Mathf.Max(boardDuration, scrollPause * 2f + scrollTime);
        if (!cycles)
        {
            boardGroup.alpha = 1f;
            return;
        }

        // Met meerdere boards (of een lange lijst die opnieuw begint) zacht in- en uitfaden.
        boardGroup.alpha = Mathf.Clamp01(Mathf.Min(boardTime, duration - boardTime) / fadeDuration);
        if (boardTime >= duration)
            ShowBoard((boardIndex + 1) % boards.Count);
    }

    private void ShowBoard(int index)
    {
        boardIndex = index;
        boardStartedAt = Time.unscaledTime;
        ArcadeHighscoreBoard board = boards[index];

        string title = string.IsNullOrWhiteSpace(board.title) ? "HIGHSCORES" : "HIGHSCORES · " + board.title.Trim();
        if (boards.Count > 1)
            title += $"  <size=60%><color=#BBBBBB>{index + 1}/{boards.Count}</color></size>";
        headerText.text = title;

        string label = string.IsNullOrWhiteSpace(board.scoreLabel) ? "Score" : board.scoreLabel.Trim();
        var text = new StringBuilder();
        text.Append($"<size=65%><color=#BBBBBB><pos=12%>Naam<pos=62%>{label}</color></size>\n");
        for (int i = 0; i < board.entries.Count; i++)
        {
            ArcadeHighscoreEntry entry = board.entries[i];
            string color = ColorUtility.ToHtmlStringRGB(RankColor(i));
            string name = Escape(string.IsNullOrWhiteSpace(entry.name) ? "???" : entry.name.Trim());
            text.Append($"<color=#{color}>{i + 1}.<pos=12%>{name}<pos=62%>{FormatScore(entry.score)}</color>\n");
        }
        rowsText.text = text.ToString();

        rowsText.ForceMeshUpdate();
        RectTransform rowsRect = rowsText.rectTransform;
        rowsRect.sizeDelta = new Vector2(0f, rowsText.preferredHeight);
        rowsRect.anchoredPosition = Vector2.zero;
    }

    private static Color RankColor(int rank)
    {
        switch (rank)
        {
            case 0: return Gold;
            case 1: return Silver;
            case 2: return Bronze;
            default: return Color.white;
        }
    }

    // 12000 -> "12.000", 48.25 -> "48,25"
    public static string FormatScore(double score)
    {
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo("nl-NL");
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }
        return score.ToString(score == System.Math.Floor(score) ? "#,0" : "#,0.##", culture);
    }

    // Namen zijn vrije tekst uit een bestand; zorg dat ze geen opmaak-tags van TextMeshPro kunnen bevatten.
    private static string Escape(string text)
    {
        return text.Replace("<", "<noparse><</noparse>");
    }

    private static GameObject CreateChild(string name, Transform parent)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        return child;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static TextMeshProUGUI CreateText(GameObject target, TextMeshProUGUI style, float size, TextAlignmentOptions alignment)
    {
        var text = target.AddComponent<TextMeshProUGUI>();
        if (style != null)
            text.font = style.font;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.text = "";
        return text;
    }
}

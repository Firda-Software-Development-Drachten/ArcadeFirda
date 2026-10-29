using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Attract mode: na idleSeconds zonder input een slideshow met previews, screenshots en highscores.
public class IdleSlideshow : MonoBehaviour
{
    [SerializeField] private UIHandler uiHandler;
    [SerializeField] private EventSystem eventSystem;
    [SerializeField] private CanvasGroup idleScreen;
    [SerializeField] private CanvasGroup slideGroup;
    [SerializeField] private RawImage slideImage;
    [SerializeField] private TextMeshProUGUI slideTitle;
    [SerializeField] private TextMeshProUGUI slideSubtitle;
    [SerializeField] private TextMeshProUGUI callToAction;

    [SerializeField] private float slideSeconds = 6f;
    [SerializeField] private float fadeSeconds = 0.5f;
    [SerializeField] private int gamesPerHighscoreSlide = 3;

    [Header("Credits voor de makers van de arcadekast")]
    [SerializeField] private string creditsYear = "2025";
    [SerializeField] private string[] creditsNames = { "Daniël", "Quinten", "Sjoerd", "Stef", "Thijs", "Yorick" };
    // Kans per ronde van de slideshow dat de creditdia erin zit, zodat hij zelden voorbijkomt.
    [SerializeField, Range(0f, 1f)] private float creditsChance = 0.25f;

    private static readonly Color Leisteen = new Color32(0x3A, 0x57, 0x57, 0xFF);

    private enum SlideKind { Preview, Screenshot, Highscores, Credits }

    private class Slide
    {
        public SlideKind Kind;
        public GameEntry Game;
        public string ImagePath;
    }

    private readonly List<Slide> slides = new List<Slide>();
    private LeaderboardOverlay leaderboard;
    private Texture2D loadedScreenshot;
    private TextMeshProUGUI creditsText;
    private int slideIndex;
    private float slideStartedAt;
    private float lastInputTime;
    private bool active;

    private void Start()
    {
        leaderboard = LeaderboardOverlay.Create(slideImage.rectTransform, slideTitle);
        leaderboard.showDelay = 0f;
        leaderboard.boardDuration = slideSeconds;
        creditsText = CreateCreditsText();

        idleScreen.alpha = 0f;
        lastInputTime = Time.unscaledTime;
    }

    private void Update()
    {
        if (HasInput())
        {
            lastInputTime = Time.unscaledTime;
            if (active)
                Exit();
            return;
        }

        if (!active)
        {
            bool canStart = !uiHandler.IsGameRunning && !uiHandler.IsMenuBusy && uiHandler.Games.Count > 0;
            if (canStart && Time.unscaledTime - lastInputTime >= uiHandler.Config.idleSeconds)
                Enter();
            return;
        }

        idleScreen.alpha = Mathf.MoveTowards(idleScreen.alpha, 1f, Time.unscaledDeltaTime / fadeSeconds);
        callToAction.alpha = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);

        // Elke dia zacht in- en uitfaden, daarna de volgende.
        float time = Time.unscaledTime - slideStartedAt;
        float duration = SlideDuration(slides[slideIndex]);
        slideGroup.alpha = Mathf.Clamp01(Mathf.Min(time, duration - time) / fadeSeconds);
        if (time >= duration)
            ShowSlide(slideIndex + 1);
    }

    // Elke knop van de gamepad of het toetsenbord, of de stick bewegen.
    private static bool HasInput()
    {
        return Input.anyKeyDown
            || Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.5f
            || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.5f;
    }

    private void Enter()
    {
        BuildSlides();
        if (slides.Count == 0)
        {
            lastInputTime = Time.unscaledTime;
            return;
        }

        active = true;
        // De knop die de slideshow stopt mag geen game starten.
        eventSystem.enabled = false;
        idleScreen.alpha = 0f;
        ShowSlide(0);
    }

    private void Exit()
    {
        active = false;
        GameEntry game = slides[slideIndex].Game;

        leaderboard.Hide();
        idleScreen.alpha = 0f;
        UnloadScreenshot();
        StartCoroutine(ReturnToMenu(game));
    }

    private IEnumerator ReturnToMenu(GameEntry game)
    {
        // Wachten tot de knop is losgelaten, anders start dezelfde druk meteen een game.
        while (Input.anyKey)
            yield return null;
        yield return null;

        eventSystem.enabled = true;
        uiHandler.SelectGame(game);
    }

    private void BuildSlides()
    {
        slides.Clear();

        var games = new List<GameEntry>(uiHandler.Games);
        Shuffle(games);
        List<GameEntry> withScores = games.FindAll(g => g.Highscores != null && g.Highscores.HasScores);

        int nextScoreGame = 0;
        int gamesSinceScores = 0;
        foreach (GameEntry game in games)
        {
            slides.Add(new Slide { Kind = SlideKind.Preview, Game = game });

            if (game.Metadata.screenshots != null)
            {
                foreach (string screenshot in game.Metadata.screenshots)
                    slides.Add(new Slide { Kind = SlideKind.Screenshot, Game = game, ImagePath = Path.Combine(game.ArcadeFolder, screenshot) });
            }

            gamesSinceScores++;
            if (withScores.Count > 0 && gamesSinceScores >= gamesPerHighscoreSlide)
            {
                gamesSinceScores = 0;
                slides.Add(new Slide { Kind = SlideKind.Highscores, Game = withScores[nextScoreGame++ % withScores.Count] });
            }
        }

        // Met weinig games toch minstens één highscoredia.
        if (withScores.Count > 0 && !slides.Exists(s => s.Kind == SlideKind.Highscores))
            slides.Add(new Slide { Kind = SlideKind.Highscores, Game = withScores[0] });

        // Af en toe, op een willekeurige plek (niet als eerste), de credits voor de makers van de kast.
        if (slides.Count > 0 && creditsNames != null && creditsNames.Length > 0 && Random.value < creditsChance)
            slides.Insert(Random.Range(1, slides.Count + 1), new Slide { Kind = SlideKind.Credits });
    }

    private void ShowSlide(int index)
    {
        // Aan het eind een nieuwe willekeurige volgorde.
        if (index >= slides.Count)
        {
            BuildSlides();
            index = 0;
        }

        slideIndex = index;
        slideStartedAt = Time.unscaledTime;
        Slide slide = slides[index];

        UnloadScreenshot();
        creditsText.text = "";
        slideImage.color = Color.white;

        if (slide.Kind == SlideKind.Credits)
        {
            ShowCredits();
            return;
        }

        // Bijgesneden kopieen in plaats van een uvRect, anders vallen de ronde hoeken buiten beeld.
        Rect rect = slideImage.rectTransform.rect;
        float aspect = rect.height > 0 ? rect.width / rect.height : 16f / 9f;
        int maxWidth = Mathf.Max(256, Mathf.CeilToInt(rect.width));

        Texture texture = null;
        if (slide.Kind == SlideKind.Screenshot)
        {
            Texture2D screenshot = GameLibrary.LoadImage(slide.ImagePath);
            if (screenshot != null)
            {
                loadedScreenshot = GameLibrary.CreateCroppedCopy(screenshot, aspect, maxWidth);
                if (loadedScreenshot != screenshot)
                    Destroy(screenshot);
                texture = loadedScreenshot;
            }
        }
        if (texture == null)
            texture = slide.Game.GetCroppedPreview(aspect, maxWidth);

        slideImage.texture = texture;
        slideImage.uvRect = new Rect(0, 0, 1, 1);

        slideTitle.text = slide.Game.Title;
        slideSubtitle.text = slide.Game.MakersAndYear.Replace("\n", " · ");

        if (slide.Kind == SlideKind.Highscores)
            leaderboard.Show(slide.Game);
        else
            leaderboard.Hide();
    }

    private void ShowCredits()
    {
        // Een vlak in Leisteen groen met de namen, in plaats van een preview.
        slideImage.texture = null;
        slideImage.uvRect = new Rect(0, 0, 1, 1);
        slideImage.color = Leisteen;
        leaderboard.Hide();

        var names = new List<string>(creditsNames);
        names.RemoveAll(string.IsNullOrWhiteSpace);
        names.Sort(System.StringComparer.CurrentCultureIgnoreCase);

        creditsText.text = "<size=50%><color=#FFCD00>/</color> Deze arcadekast is gemaakt door</size>\n\n" + string.Join("\n", names);
        slideTitle.text = "Arcadekast Firda";
        slideSubtitle.text = $"Studenten Software Development · {creditsYear}";
    }

    private TextMeshProUGUI CreateCreditsText()
    {
        var textObject = new GameObject("CreditsText", typeof(RectTransform));
        textObject.layer = slideImage.gameObject.layer;
        var rect = (RectTransform)textObject.transform;
        rect.SetParent(slideImage.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(60, 40);
        rect.offsetMax = new Vector2(-60, -40);

        var text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = slideTitle.font;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = 24;
        text.fontSizeMax = 72;
        text.raycastTarget = false;
        text.text = "";
        return text;
    }

    private float SlideDuration(Slide slide)
    {
        if (slide.Kind == SlideKind.Credits)
            return slideSeconds * 1.5f;
        if (slide.Kind != SlideKind.Highscores)
            return slideSeconds;

        int boards = 0;
        foreach (ArcadeHighscoreBoard board in slide.Game.Highscores.boards)
        {
            if (board.entries != null && board.entries.Count > 0)
                boards++;
        }
        return slideSeconds * Mathf.Max(1, boards);
    }

    private void UnloadScreenshot()
    {
        if (loadedScreenshot != null)
        {
            Destroy(loadedScreenshot);
            loadedScreenshot = null;
        }
    }

    private static void Shuffle(List<GameEntry> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
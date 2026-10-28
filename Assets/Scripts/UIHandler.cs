using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.IO;
using TMPro;
using System.Diagnostics; // <-- Add this
using System.Runtime.InteropServices;
using Nobi.UiRoundedCorners;
using UnityEngine.Video;

public class UIHandler : MonoBehaviour
{
    [SerializeField] private GameObject scrollView;
    [SerializeField] private GameObject gameButtonPrefab;
    [SerializeField] private EventSystem eventSystem;
    [SerializeField] private GameObject settingsObject;
    [SerializeField] private Button settingsButton;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private TextMeshProUGUI exeNameText;

    [Header("Game info (wordt automatisch gevonden als het leeg is)")]
    [SerializeField] private RawImage gameImage;
    [SerializeField] private TextMeshProUGUI playersGenreText;
    [SerializeField] private TextMeshProUGUI extraInfoText;
    [SerializeField] private TMP_FontAsset bodyFont;

    [SerializeField] private GameObject passwordPanel;
    [SerializeField] private TMP_InputField passwordInputField;
    [SerializeField] private TextMeshProUGUI passwordFeedbackText;
    [SerializeField] private Button passwordSubmitButton;
    [SerializeField] private Button passwordCancelButton;

    private ArcadeConfig config;

    private ScrollRect scrollRect;
    private RectTransform contentRect;
    private RectTransform viewportRect;
    private List<GameObject> gameButtons = new List<GameObject>();

    private float targetScrollPosition;
    private bool isScrolling = false;
    private float scrollSpeed = 5f; // Adjust for smoothness

    // Changed to Windows-style path. Wordt overschreven door gamesRoot in arcade-config.json.
    public string destinationRoot = @"C:\Nonsteamgames\Imported";
    private const string doneFileName = "Done";

    private List<GameEntry> games = new List<GameEntry>();
    private TextMeshProUGUI gameImageTitle;

    private FileSystemWatcher watcher;
    [Header("Detected .exe file paths")]
    [Tooltip("Gevuld bij elke scan van de gamemap")]
    public List<string> exePaths = new List<string>();

    // --- Added for FileSystemWatcher thread-safety ---
    private volatile bool pendingRescan = false;
    // --------------------------------------------------

    // Studenten kopiëren games met de hand; wacht tot het kopiëren klaar lijkt voordat er gescand wordt.
    private const float rescanDelay = 5f;
    private float rescanAt = -1f;

    // Voorkomt dat een game meerdere keren start als de knop snel achter elkaar wordt ingedrukt.
    private const float launchCooldown = 5f;
    private float lastLaunchTime = -100f;

    // De game die nu draait, zodat het menu weet wanneer hij weer afgesloten is.
    private Process runningGame;
    private string runningGameFolder;
    private const float runningGameCheckInterval = 1f;
    private float nextRunningGameCheck;
    private int savedVSyncCount;
    private VideoPlayer backgroundVideo;
    private System.IntPtr menuWindow;

    private LeaderboardOverlay leaderboardOverlay;

    private const float shutdownCheckInterval = 5f;
    private float nextShutdownCheck = 0f;
    private System.DateTime lastShutdownCheck;
    private bool shutdownStarted = false;

    private const float previewInset = 8f;

    private void Awake()
    {
        passwordInputField = passwordPanel.GetComponentInChildren<TMP_InputField>();
        passwordFeedbackText = passwordPanel.transform.Find("FeedbackText").GetComponent<TextMeshProUGUI>();
        passwordSubmitButton = passwordPanel.transform.Find("SubmitButton").GetComponent<Button>();
        passwordCancelButton = passwordPanel.transform.Find("CancelButton").GetComponent<Button>();

        scrollRect = scrollView.GetComponent<ScrollRect>();
        viewportRect = scrollRect.viewport;
        contentRect = scrollRect.content;

        config = ArcadeConfig.Load();
        if (!string.IsNullOrWhiteSpace(config.gamesRoot))
            destinationRoot = config.gamesRoot;

        // Normalize backslashes for Windows
        destinationRoot = destinationRoot.Replace("/", "\\").TrimEnd('\\');

        lastShutdownCheck = System.DateTime.Now;
        backgroundVideo = FindFirstObjectByType<VideoPlayer>();

        SetupGameInfoPanel();
        ScanAndCreateGameButtons();

        // Setup FileSystemWatcher to monitor for the "Done" file and new game folders
        SetupDoneFileWatcher();
    }

    private void SetupDoneFileWatcher()
    {
        if (!Directory.Exists(destinationRoot))
        {
            UnityEngine.Debug.LogError($"[UIHandler] Kan {destinationRoot} niet bewaken: de map bestaat niet.");
            return;
        }

        try
        {
            watcher = new FileSystemWatcher(destinationRoot)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            watcher.Created += OnDoneFileDetected;
            watcher.Changed += OnDoneFileDetected;
            watcher.Renamed += OnDoneFileDetected;
            watcher.Deleted += OnDoneFileDetected;
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"[UIHandler] FileSystemWatcher kon niet starten: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        if (watcher != null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        GameLibrary.DestroyTextures(games);
    }

    private void OnDoneFileDetected(object sender, FileSystemEventArgs e)
    {
        // Set flag to rescan on main thread
        pendingRescan = true;
    }

    private void Update()
    {
        if (isScrolling)
        {
            scrollRect.verticalNormalizedPosition = Mathf.Lerp(scrollRect.verticalNormalizedPosition, targetScrollPosition, Time.deltaTime * scrollSpeed);

            if (Mathf.Abs(scrollRect.verticalNormalizedPosition - targetScrollPosition) < 0.001f)
            {
                scrollRect.verticalNormalizedPosition = targetScrollPosition;
                isScrolling = false;
            }
        }

        if (settingsSpinning)
        {
            settingsObject.transform.Rotate(0, 0, 100f * Time.deltaTime); // Rotate around the Z-axis
        }

        // --- Handle rescan if the games folder changed ---
        if (pendingRescan)
        {
            pendingRescan = false;
            rescanAt = Time.unscaledTime + rescanDelay;
        }
        if (rescanAt >= 0f && Time.unscaledTime >= rescanAt)
        {
            rescanAt = -1f;
            RescanAndAddGameButtons();
        }
        // -------------------------------------------

        if (Time.unscaledTime >= nextShutdownCheck)
        {
            nextShutdownCheck = Time.unscaledTime + shutdownCheckInterval;
            CheckAutoShutdown();
        }

        if (IsGameRunning && Time.unscaledTime >= nextRunningGameCheck)
        {
            nextRunningGameCheck = Time.unscaledTime + runningGameCheckInterval;
            CheckRunningGame();
        }
    }

    // Zoekt de vakjes onder het grote plaatje op en zet er tekstvelden in.
    private void SetupGameInfoPanel()
    {
        if (exeNameText == null)
            return;

        exeNameText.enableAutoSizing = true;
        exeNameText.fontSizeMin = 14;
        exeNameText.fontSizeMax = exeNameText.fontSize;
        exeNameText.margin = new Vector4(10, 5, 10, 5);

        // Name-tekst -> vakje "Image" -> GameInfoPanel
        Transform infoPanel = exeNameText.transform.parent.parent;

        if (gameImage == null)
        {
            Transform gameImageTransform = infoPanel.Find("GameImage");
            if (gameImageTransform != null)
                gameImage = gameImageTransform.GetComponent<RawImage>();
        }
        if (gameImage != null)
        {
            gameImageTitle = CreateText("Title", gameImage.transform, exeNameText, 96, Color.white, 40f);
            gameImageTitle.fontStyle = FontStyles.Bold;
            leaderboardOverlay = LeaderboardOverlay.Create(gameImage.rectTransform, exeNameText);
        }

        if (playersGenreText == null)
            playersGenreText = CreateBoxText(infoPanel, "Image (1)");
        if (extraInfoText == null)
            extraInfoText = CreateBoxText(infoPanel, "Image (2)");
    }

    private TextMeshProUGUI CreateBoxText(Transform infoPanel, string boxName)
    {
        Transform box = infoPanel.Find(boxName);
        if (box == null)
        {
            UnityEngine.Debug.LogWarning($"[UIHandler] Vakje {boxName} niet gevonden in {infoPanel.name}.");
            return null;
        }
        TextMeshProUGUI text = CreateText("Text (TMP)", box, exeNameText, exeNameText.fontSizeMax, exeNameText.color, 10f);
        if (bodyFont != null)
            text.font = bodyFont;
        return text;
    }

    // Maakt een tekstveld dat zijn ouder vult, in dezelfde stijl als de bestaande Name-tekst.
    private static TextMeshProUGUI CreateText(string name, Transform parent, TextMeshProUGUI style, float maxSize, Color color, float margin)
    {
        var textObject = new GameObject(name, typeof(RectTransform));
        textObject.layer = parent.gameObject.layer;
        var rect = (RectTransform)textObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var text = textObject.AddComponent<TextMeshProUGUI>();
        if (style != null)
            text.font = style.font;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = 14;
        text.fontSizeMax = maxSize;
        text.margin = new Vector4(margin, margin / 2f, margin, margin / 2f);
        text.raycastTarget = false;
        text.text = "";
        return text;
    }

    private bool ScanAndCreateGameButtons()
    {
        foreach (var btn in gameButtons)
        {
            Destroy(btn);
        }
        gameButtons.Clear();
        GameLibrary.DestroyTextures(games);
        games.Clear();
        exePaths.Clear();

        if (!Directory.Exists(destinationRoot))
        {
            UnityEngine.Debug.LogError($"Destination root does not exist: {destinationRoot}");
            ShowMessage("Gamemap niet gevonden", destinationRoot);
            return false;
        }

        UnityEngine.Debug.Log($"Destination root exists: {destinationRoot}");
        games = GameLibrary.Scan(destinationRoot);

        if (games.Count == 0)
        {
            UnityEngine.Debug.LogWarning("[GameListUI] No valid game executables found in any subfolder.");
            ShowMessage("Nog geen games", destinationRoot);
            return false;
        }

        for (int i = 0; i < games.Count; i++)
        {
            exePaths.Add(games[i].ExePath);

            GameObject gameButtonClone = Instantiate(gameButtonPrefab, contentRect);
            gameButtonClone.name = "Game " + games[i].Title;
            gameButtonClone.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 200);
            AddPreviewToButton(gameButtonClone, games[i]);

            EventTrigger trigger = gameButtonClone.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = gameButtonClone.AddComponent<EventTrigger>();

            int index = i;

            // Na een rescan kan een oude knop nog een Deselect sturen; die hoort dan niet meer bij deze index.
            EventTrigger.Entry selectEntry = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            selectEntry.callback.AddListener((data) => { if (IsCurrentButton(gameButtonClone, index)) GameSelectedOn(index); });
            trigger.triggers.Add(selectEntry);

            EventTrigger.Entry deselectEntry = new EventTrigger.Entry { eventID = EventTriggerType.Deselect };
            deselectEntry.callback.AddListener((data) => { if (IsCurrentButton(gameButtonClone, index)) GameSelectedOff(index); });
            trigger.triggers.Add(deselectEntry);

            // --- Add Button and onClick for launching exe ---
            Button button = gameButtonClone.GetComponent<Button>();
            if (button == null)
                button = gameButtonClone.AddComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => StartExe(index));
            // -----------------------------------------------

            gameButtons.Add(gameButtonClone);
        }
        return true;
    }

    private bool IsCurrentButton(GameObject button, int index)
    {
        return index < gameButtons.Count && gameButtons[index] == button;
    }

    // De witte knop blijft als rand zichtbaar (en kleurt groen bij selectie); de preview ligt erbinnen.
    private void AddPreviewToButton(GameObject button, GameEntry game)
    {
        var previewObject = new GameObject("Preview", typeof(RectTransform));
        previewObject.layer = button.layer;
        var rect = (RectTransform)previewObject.transform;
        rect.SetParent(button.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(previewInset, previewInset);
        rect.offsetMax = new Vector2(-previewInset, -previewInset);

        var preview = previewObject.AddComponent<RawImage>();
        preview.texture = game.Preview;
        preview.uvRect = GameLibrary.GetCropUv(game.Preview, 1f);
        preview.raycastTarget = false;

        var buttonCorners = button.GetComponent<ImageWithRoundedCorners>();
        var previewCorners = previewObject.AddComponent<ImageWithRoundedCorners>();
        previewCorners.radius = Mathf.Max(0f, (buttonCorners != null ? buttonCorners.radius : 40f) - previewInset);
        previewCorners.Validate();
        previewCorners.Refresh();

        // Zonder preview staat de titel op het gekleurde vlak, zodat de game herkenbaar blijft.
        if (!game.HasPreview)
        {
            TextMeshProUGUI title = CreateText("Title", previewObject.transform, exeNameText, 40, Color.white, 12f);
            title.fontStyle = FontStyles.Bold;
            title.text = game.Title;
        }
    }

    private void RescanAndAddGameButtons()
    {
        UnityEngine.Debug.Log("[UIHandler] Gamemap is gewijzigd. Rescanning for new games...");

        GameObject selected = eventSystem.currentSelectedGameObject;
        int selectedIndex = gameButtons.IndexOf(selected);
        string selectedFolder = selectedIndex >= 0 && selectedIndex < games.Count ? games[selectedIndex].FolderPath : null;
        bool gameWasSelected = selectedIndex >= 0 || selected == null;

        if (ScanAndCreateGameButtons())
        {
            int newIndex = games.FindIndex(g => g.FolderPath == selectedFolder);
            if (newIndex < 0)
                newIndex = 0;

            eventSystem.firstSelectedGameObject = gameButtons[0];
            if (gameWasSelected)
            {
                // Eén frame wachten zodat de layout van de nieuwe knoppen klaar is voordat er gescrold wordt.
                StartCoroutine(SelectNextFrame(gameButtons[newIndex]));
            }
        }
        else
        {
            eventSystem.firstSelectedGameObject = settingsButton.gameObject;
            if (gameWasSelected)
                eventSystem.SetSelectedGameObject(settingsButton.gameObject);
        }
    }

    private IEnumerator SelectNextFrame(GameObject target)
    {
        eventSystem.SetSelectedGameObject(null);
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (target != null)
            eventSystem.SetSelectedGameObject(target);
    }

    private void Start()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        menuWindow = GetActiveWindow();
#endif
        Canvas.ForceUpdateCanvases();

        if (gameButtons.Count > 0)
            eventSystem.firstSelectedGameObject = gameButtons[0];
        else
            eventSystem.firstSelectedGameObject = settingsButton.gameObject;
        scrollRect.verticalNormalizedPosition = 1.5f;
    }

    private bool settingsSpinning = false;

    public void SettingsSelectedOn()
    {
        settingsSpinning = true;
    }
    public void SettingsSelectedOff()
    {
        settingsSpinning = false;
        settingsObject.transform.rotation = Quaternion.Euler(-90, 90, 0);
    }

    public void SettingsClicked()
    {
        if (settingsPanel.activeSelf)
        {
            settingsPanel.SetActive(false);
            settingsButton.Select();

        }
        else
        {
            settingsPanel.SetActive(true);
            Button[] buttons = settingsPanel.GetComponentsInChildren<Button>();
            foreach (var button in buttons)
            {
                if (button.name == "BackButton")
                {
                    print("back");
                    button.Select();
                }
            }
        }
    }

    public void GameSelectedOn(int gameNumber)
    {
        gameButtons[gameNumber].GetComponent<RectTransform>().sizeDelta = new Vector2(300, 300);
        ScrollToButton(gameButtons[gameNumber].GetComponent<RectTransform>());

        if (gameNumber < games.Count)
            ShowGameInfo(games[gameNumber]);
    }

    public void GameSelectedOff(int gameNumber)
    {
        gameButtons[gameNumber].GetComponent<RectTransform>().sizeDelta = new Vector2(200, 200);
        if (leaderboardOverlay != null)
            leaderboardOverlay.Hide();
    }

    private void ShowGameInfo(GameEntry game)
    {
        if (gameImage != null)
        {
            gameImage.texture = game.Preview;
            Rect rect = gameImage.rectTransform.rect;
            gameImage.uvRect = GameLibrary.GetCropUv(game.Preview, rect.height > 0 ? rect.width / rect.height : 16f / 9f);
        }
        if (gameImageTitle != null)
            gameImageTitle.text = game.HasPreview ? "" : game.Title;

        // Vakje 1: titel
        if (exeNameText != null)
            exeNameText.text = game.Title;

        // Vakje 2: spelers en genre
        if (playersGenreText != null)
            playersGenreText.text = game.PlayersAndGenre;

        // Vakje 3: makers en jaar
        if (extraInfoText != null)
            extraInfoText.text = game.MakersAndYear;

        // Highscores verschijnen na een paar seconden over het grote plaatje
        if (leaderboardOverlay != null)
            leaderboardOverlay.Show(game);
    }

    // Toont een melding in het infopaneel, bijvoorbeeld als er geen games zijn.
    private void ShowMessage(string title, string detail)
    {
        if (gameImage != null)
        {
            gameImage.texture = null;
            gameImage.uvRect = new Rect(0, 0, 1, 1);
        }
        if (gameImageTitle != null)
            gameImageTitle.text = "";
        if (leaderboardOverlay != null)
            leaderboardOverlay.Hide();
        if (exeNameText != null)
            exeNameText.text = $"{title}\n<size=50%>{detail}</size>";
        if (playersGenreText != null)
            playersGenreText.text = "";
        if (extraInfoText != null)
            extraInfoText.text = "";
    }

    private void ScrollToButton(RectTransform button)
    {
        Vector3 worldPosition = button.position;
        Vector3 viewportPosition = viewportRect.InverseTransformPoint(worldPosition);

        float buttonTop = viewportPosition.y + (button.rect.height / 2);
        float buttonBottom = viewportPosition.y - (button.rect.height / 2);
        float viewportTop = viewportRect.rect.height / 2;
        float viewportBottom = -viewportRect.rect.height / 2;

        float newScrollPosition = scrollRect.verticalNormalizedPosition;

        if (buttonTop > viewportTop)
        {
            float diff = buttonTop - viewportTop;
            newScrollPosition += diff / contentRect.rect.height;
            newScrollPosition += 0.03f;
        }
        else if (buttonBottom < viewportBottom)
        {
            float diff = viewportBottom - buttonBottom;
            newScrollPosition -= diff / contentRect.rect.height;
            newScrollPosition -= 0.03f;
        }

        targetScrollPosition = Mathf.Clamp(newScrollPosition, 0f, 1f);
        isScrolling = true;
    }

    // --- Launch the exe directly on Windows ---
    private void StartExe(int gameNumber)
    {
        if (gameNumber >= games.Count || IsGameRunning)
            return;

        if (Time.unscaledTime - lastLaunchTime < launchCooldown)
        {
            UnityEngine.Debug.Log("[UIHandler] Game wordt al gestart, extra druk op de knop genegeerd.");
            return;
        }
        lastLaunchTime = Time.unscaledTime;

        string exePath = games[gameNumber].ExePath;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = games[gameNumber].FolderPath,
                UseShellExecute = true, // UseShellExecute true for Windows GUI apps
                CreateNoWindow = false
            };
            Process process = Process.Start(startInfo);
            if (process != null)
                OnGameStarted(process, games[gameNumber]);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"Failed to start exe: {exePath}\n{ex.Message}");
        }
    }

    public bool IsGameRunning
    {
        get { return runningGame != null; }
    }

    public IReadOnlyList<GameEntry> Games
    {
        get { return games; }
    }

    public ArcadeConfig Config
    {
        get { return config; }
    }

    // Instellingen of wachtwoord open: dan geen slideshow starten.
    public bool IsMenuBusy
    {
        get { return settingsPanel.activeSelf || passwordPanel.activeSelf; }
    }

    // Selecteert een game in de lijst, bijvoorbeeld na de slideshow.
    public void SelectGame(GameEntry game)
    {
        int index = game != null ? games.IndexOf(game) : -1;
        if (index < 0 && gameButtons.Count > 0)
            index = 0;
        GameObject target = index >= 0 ? gameButtons[index] : settingsButton.gameObject;
        StartCoroutine(SelectNextFrame(target));
    }

    // Terwijl een game draait: geen input in het menu (de gamepad wordt ook op de achtergrond gelezen),
    // video op pauze en een lage framerate, zodat de game alle rekenkracht krijgt.
    private void OnGameStarted(Process process, GameEntry game)
    {
        runningGame = process;
        runningGameFolder = game.FolderPath;
        nextRunningGameCheck = Time.unscaledTime + runningGameCheckInterval;

        if (leaderboardOverlay != null)
            leaderboardOverlay.Hide();
        eventSystem.enabled = false;
        if (backgroundVideo != null)
            backgroundVideo.Pause();

        savedVSyncCount = QualitySettings.vSyncCount;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 10;
    }

    private void CheckRunningGame()
    {
        bool exited;
        try
        {
            exited = runningGame.HasExited;
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[UIHandler] Kan de status van de game niet lezen: {ex.Message}");
            exited = true;
        }

        if (exited)
            OnGameExited();
    }

    private void OnGameExited()
    {
        runningGame.Dispose();
        runningGame = null;

        QualitySettings.vSyncCount = savedVSyncCount;
        Application.targetFrameRate = -1;
        if (backgroundVideo != null)
            backgroundVideo.Play();
        eventSystem.enabled = true;

        BringMenuToFront();

        // Scores kunnen veranderd zijn; daarna de game weer selecteren waar de speler vandaan kwam.
        int index = games.FindIndex(g => g.FolderPath == runningGameFolder);
        runningGameFolder = null;
        if (index < 0)
            return;

        GameLibrary.ReloadHighscores(games[index]);
        if (eventSystem.currentSelectedGameObject == gameButtons[index])
            ShowGameInfo(games[index]);
        else
            eventSystem.SetSelectedGameObject(gameButtons[index]);
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(System.IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern System.IntPtr GetActiveWindow();
#endif

    // Na het afsluiten van een game krijgt het menu niet altijd vanzelf de focus terug.
    private void BringMenuToFront()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        try
        {
            System.IntPtr window = menuWindow != System.IntPtr.Zero ? menuWindow : Process.GetCurrentProcess().MainWindowHandle;
            if (window != System.IntPtr.Zero)
            {
                const int SW_RESTORE = 9;
                ShowWindow(window, SW_RESTORE);
                SetForegroundWindow(window);
            }
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[UIHandler] Kon het menu niet naar voren halen: {ex.Message}");
        }
#endif
    }

    // Zet de pc uit op het tijdstip uit arcade-config.json. Alleen als dat tijdstip voorbijkomt terwijl
    // het menu draait, zodat een kast die na dat tijdstip wordt aangezet niet meteen weer uitgaat.
    private void CheckAutoShutdown()
    {
        System.DateTime now = System.DateTime.Now;
        System.DateTime? shutdownAt = config.GetShutdownTimeToday();

        if (!shutdownStarted && shutdownAt.HasValue && lastShutdownCheck < shutdownAt.Value && now >= shutdownAt.Value)
        {
            shutdownStarted = true;
            ShutDownPc();
        }
        lastShutdownCheck = now;
    }

    private void ShutDownPc()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/s /t 60 /c \"De arcadekast gaat over 1 minuut uit.\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            print("Shutting down PC...");
            Process.Start(startInfo);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"Failed to shut down PC: {ex.Message}");
        }
#else
        UnityEngine.Debug.Log("[UIHandler] Automatisch uitzetten: in de editor wordt de pc niet uitgezet.");
#endif
    }

    // De Power Off-knop staat uit in de scene; de pc gaat automatisch uit via shutdownTime in arcade-config.json.
    public void PowerOffMode()
    {
        // Shut down the PC (Windows only)
#if UNITY_STANDALONE_WIN
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/s /t 0",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            print("Shutting down PC...");
            //Process.Start(startInfo);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"Failed to shut down PC: {ex.Message}");
        }
#endif
    }
    public void StartDesktopMode()
    {
        print("showing password pannel");
        // Show the password panel
        passwordPanel.SetActive(true);
        passwordFeedbackText.text = "";
        passwordInputField.text = "";
        passwordInputField.ActivateInputField();

        // Remove previous listeners to avoid stacking
        passwordSubmitButton.onClick.RemoveAllListeners();
        passwordCancelButton.onClick.RemoveAllListeners();

        // Confirm button logic
        passwordSubmitButton.onClick.AddListener(() =>
        {
            if (string.IsNullOrEmpty(config.desktopPassword))
            {
                passwordFeedbackText.text = $"<color=red>Geen wachtwoord ingesteld in {ArcadeConfig.FileName}.</color>";
                passwordInputField.text = "";
                passwordInputField.ActivateInputField();
            }
            else if (passwordInputField.text == config.desktopPassword)
            {
                passwordFeedbackText.text = "";
                Application.Quit();
            }
            else
            {
                passwordFeedbackText.text = "<color=red>Incorrect password. Please try again.</color>";
                passwordInputField.text = "";
                passwordInputField.ActivateInputField();
            }
        });

        // Cancel button logic
        passwordCancelButton.onClick.AddListener(() =>
        {
            passwordPanel.SetActive(false);
            // Select the settings back button if it exists
            Button[] buttons = settingsPanel.GetComponentsInChildren<Button>();
            foreach (var button in buttons)
            {
                if (button.name == "BackButton")
                {
                    button.Select();
                    break;
                }
            }
        });
    }
    // ------------------------------------------
}

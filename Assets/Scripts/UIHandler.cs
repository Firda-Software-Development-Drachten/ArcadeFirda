using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using System.Diagnostics; // <-- Add this

public class UIHandler : MonoBehaviour
{
    [SerializeField] private GameObject scrollView;
    [SerializeField] private GameObject gameButtonPrefab;
    [SerializeField] private EventSystem eventSystem;
    [SerializeField] private GameObject settingsObject;
    [SerializeField] private Button settingsButton;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private TextMeshProUGUI exeNameText;

    [SerializeField] private GameObject passwordPanel;
    [SerializeField] private TMP_InputField passwordInputField;
    [SerializeField] private TextMeshProUGUI passwordFeedbackText;
    [SerializeField] private Button passwordSubmitButton;
    [SerializeField] private Button passwordCancelButton;
    private string correctPassword = "ArcadeFirda12345!";

    private ScrollRect scrollRect;
    private RectTransform contentRect;
    private RectTransform viewportRect;
    private List<GameObject> gameButtons = new List<GameObject>();

    private float targetScrollPosition;
    private bool isScrolling = false;
    private float scrollSpeed = 5f; // Adjust for smoothness

    // Changed to Windows-style path
    public string destinationRoot = @"C:\Nonsteamgames\Imported";
    private const string doneFileName = "Done";
    private string doneFilePath;

    private List<string> gameDisplayNames = new List<string>();
    private List<string> gameExePaths = new List<string>();

    private FileSystemWatcher watcher;
    private bool doneDetected = false;
    [Header("Detected .exe file paths")]
    [Tooltip("Populated once the 'Done' file is detected")]
    public List<string> exePaths = new List<string>();

    private static readonly Regex UnityHelperExePattern = new Regex(
        @"^(unitycrashhandler(32|64)?|unityplayer)\.exe$",
        RegexOptions.IgnoreCase
    );

    // --- Added for FileSystemWatcher thread-safety ---
    private volatile bool pendingRescan = false;
    // --------------------------------------------------

    private void Awake()
    {
        passwordInputField = passwordPanel.GetComponentInChildren<TMP_InputField>();
        passwordFeedbackText = passwordPanel.transform.Find("FeedbackText").GetComponent<TextMeshProUGUI>();
        passwordSubmitButton = passwordPanel.transform.Find("SubmitButton").GetComponent<Button>();
        passwordCancelButton = passwordPanel.transform.Find("CancelButton").GetComponent<Button>();

        scrollRect = scrollView.GetComponent<ScrollRect>();
        viewportRect = scrollRect.viewport;
        contentRect = scrollRect.content;

        // Normalize backslashes for Windows
        destinationRoot = destinationRoot.Replace("/", "\\").TrimEnd('\\');

        ScanAndCreateGameButtons();

        // Setup FileSystemWatcher to monitor for the "Done" file
        SetupDoneFileWatcher();
    }

    private void SetupDoneFileWatcher()
    {
        watcher = new FileSystemWatcher(destinationRoot)
        {
            Filter = doneFileName,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        watcher.Created += OnDoneFileDetected;
        watcher.Changed += OnDoneFileDetected;
        watcher.Renamed += OnDoneFileDetected;
    }

    private void OnDestroy()
    {
        if (watcher != null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
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

        // --- Handle rescan if Done file detected ---
        if (pendingRescan)
        {
            pendingRescan = false;
            RescanAndAddGameButtons();
        }
        // -------------------------------------------
    }

    private bool ScanAndCreateGameButtons()
    {
        gameDisplayNames.Clear();
        gameExePaths.Clear();
        foreach (var btn in gameButtons)
        {
            Destroy(btn);
        }
        gameButtons.Clear();

        if (Directory.Exists(destinationRoot))
        {
            UnityEngine.Debug.Log($"Destination root exists: {destinationRoot}");

            string[] subfolders = Directory.GetDirectories(destinationRoot);
            foreach (string folderPath in subfolders)
            {
                string exeName = GetExeName(folderPath);
                if (string.IsNullOrEmpty(exeName))
                {
                    continue;
                }

                string displayName = Path.GetFileNameWithoutExtension(exeName);
                gameDisplayNames.Add(displayName);

                string exeFullPath = Path.Combine(folderPath, exeName);
                gameExePaths.Add(exeFullPath);
            }

            int exeCounts = gameDisplayNames.Count;
            if (exeCounts == 0)
            {
                UnityEngine.Debug.LogWarning("[GameListUI] No valid game executables found in any subfolder.");
                //make it select another button and make it show the error
                return false;
            }

            for (int i = 0; i < exeCounts; i++)
            {
                GameObject gameButtonClone = Instantiate(gameButtonPrefab, contentRect);
                gameButtonClone.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 200);

                EventTrigger trigger = gameButtonClone.GetComponent<EventTrigger>();
                if (trigger == null)
                    trigger = gameButtonClone.AddComponent<EventTrigger>();

                int index = i;

                EventTrigger.Entry selectEntry = new EventTrigger.Entry { eventID = EventTriggerType.Select };
                selectEntry.callback.AddListener((data) => { GameSelectedOn(index); });
                trigger.triggers.Add(selectEntry);

                EventTrigger.Entry deselectEntry = new EventTrigger.Entry { eventID = EventTriggerType.Deselect };
                deselectEntry.callback.AddListener((data) => { GameSelectedOff(index); });
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
        else
        {
            UnityEngine.Debug.LogError($"Destination root does not exist: {destinationRoot}");
            //make it select another button and make it show the error
            return false;
        }
    }

    private void RescanAndAddGameButtons()
    {
        UnityEngine.Debug.Log("[UIHandler] Detected 'Done' file. Rescanning for new games...");
        if (ScanAndCreateGameButtons())
        {
            // Optionally, reset selection and scroll
            if (gameButtons.Count > 0)
            {
                eventSystem.firstSelectedGameObject = gameButtons[0];
                scrollRect.verticalNormalizedPosition = 1.5f;
            }
        }
        else
        {
            eventSystem.firstSelectedGameObject = settingsButton.gameObject;
        }
    }

    private string GetExeName(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return null;

        string[] allExeFiles = Directory.GetFiles(folderPath, "*.exe", SearchOption.TopDirectoryOnly);

        List<string> candidates = new List<string>();
        foreach (var fullExe in allExeFiles)
        {
            string fileName = Path.GetFileName(fullExe);
            if (UnityHelperExePattern.IsMatch(fileName))
                continue;
            candidates.Add(fileName);
        }

        if (candidates.Count == 0)
            return null;

        return candidates[0];
    }

    private void Start()
    {
        Canvas.ForceUpdateCanvases();

        if (gameButtons.Count > 0)
            eventSystem.firstSelectedGameObject = gameButtons[0];
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

        // Show the exe name in the TextMeshPro field
        if (exeNameText != null && gameExePaths.Count > gameNumber)
        {
            exeNameText.text = Path.GetFileName(gameExePaths[gameNumber]);
        }
    }

    public void GameSelectedOff(int gameNumber)
    {
        gameButtons[gameNumber].GetComponent<RectTransform>().sizeDelta = new Vector2(200, 200);
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
        if (gameExePaths.Count > gameNumber)
        {
            string exePath = gameExePaths[gameNumber];

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true, // UseShellExecute true for Windows GUI apps
                    CreateNoWindow = false
                };
                Process.Start(startInfo);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Failed to start exe: {exePath}\n{ex.Message}");
            }
        }
    }

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
            if (passwordInputField.text == correctPassword)
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

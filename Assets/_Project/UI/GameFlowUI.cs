using System.Text;
using Dormitory7.Core;
using Dormitory7.Features.Quests;
using Dormitory7.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dormitory7.UI
{
    public class GameFlowUI : MonoBehaviour
    {
        public static GameFlowUI Instance { get; private set; }

        [Header("Core References")]
        [SerializeField] private GameStateManager gameStateManager;
        [SerializeField] private DormStatsManager statsManager;
        [SerializeField] private DayTimeController dayTimeController;
        [SerializeField] private QuestManager questManager;
        [SerializeField] private FirstPersonController playerController;

        [Header("Main Menu Panel")]
        [SerializeField] private GameObject mainMenuRoot;
        [SerializeField] private Button mainStartButton;
        [SerializeField] private Button mainSettingsButton;
        [SerializeField] private Button mainQuitButton;
        [SerializeField] private GameObject mainSettingsContainer;
        [SerializeField] private Slider mainSensSlider;
        [SerializeField] private TextMeshProUGUI mainSensValueText;
        [SerializeField] private Slider mainVolSlider;
        [SerializeField] private TextMeshProUGUI mainVolValueText;

        [Header("Week Intro Panel")]
        [SerializeField] private GameObject weekIntroRoot;
        [SerializeField] private Button weekIntroStartDayButton;

        [Header("Pause Panel")]
        [SerializeField] private GameObject pauseRoot;
        [SerializeField] private Button pauseResumeButton;
        [SerializeField] private Button pauseRestartButton;
        [SerializeField] private Button pauseMainMenuButton;
        [SerializeField] private Slider pauseSensSlider;
        [SerializeField] private TextMeshProUGUI pauseSensValueText;
        [SerializeField] private Slider pauseVolSlider;
        [SerializeField] private TextMeshProUGUI pauseVolValueText;

        [Header("Day Summary Panel")]
        [SerializeField] private GameObject daySummaryRoot;
        [SerializeField] private TextMeshProUGUI daySummaryTitleText;
        [SerializeField] private TextMeshProUGUI daySummaryBodyText;
        [SerializeField] private Button daySummaryNextDayButton;
        [SerializeField] private TextMeshProUGUI daySummaryNextBtnLabel;

        [Header("End Screen Panel (Victory / Game Over)")]
        [SerializeField] private GameObject endScreenRoot;
        [SerializeField] private TextMeshProUGUI endScreenTitleText;
        [SerializeField] private TextMeshProUGUI endScreenSubtitleText;
        [SerializeField] private TextMeshProUGUI endScreenBodyText;
        [SerializeField] private Button endScreenRestartButton;
        [SerializeField] private Button endScreenMainMenuButton;

        private bool _syncingSliders;

        public void BindCore(
            GameStateManager stateMgr,
            DormStatsManager stats,
            DayTimeController dayTime,
            QuestManager quests,
            FirstPersonController fpc)
        {
            gameStateManager = stateMgr;
            statsManager = stats;
            dayTimeController = dayTime;
            questManager = quests;
            playerController = fpc;
        }

        public void BindMainMenu(
            GameObject root, Button startBtn, Button settingsBtn, Button quitBtn,
            GameObject settingsBox, Slider sensSlider, TextMeshProUGUI sensLabel,
            Slider volSlider, TextMeshProUGUI volLabel)
        {
            mainMenuRoot = root;
            mainStartButton = startBtn;
            mainSettingsButton = settingsBtn;
            mainQuitButton = quitBtn;
            mainSettingsContainer = settingsBox;
            mainSensSlider = sensSlider;
            mainSensValueText = sensLabel;
            mainVolSlider = volSlider;
            mainVolValueText = volLabel;
        }

        public void BindWeekIntro(GameObject root, Button startDayBtn)
        {
            weekIntroRoot = root;
            weekIntroStartDayButton = startDayBtn;
        }

        public void BindPause(
            GameObject root, Button resumeBtn, Button restartBtn, Button menuBtn,
            Slider sensSlider, TextMeshProUGUI sensLabel,
            Slider volSlider, TextMeshProUGUI volLabel)
        {
            pauseRoot = root;
            pauseResumeButton = resumeBtn;
            pauseRestartButton = restartBtn;
            pauseMainMenuButton = menuBtn;
            pauseSensSlider = sensSlider;
            pauseSensValueText = sensLabel;
            pauseVolSlider = volSlider;
            pauseVolValueText = volLabel;
        }

        public void BindDaySummary(
            GameObject root, TextMeshProUGUI title, TextMeshProUGUI body,
            Button nextBtn, TextMeshProUGUI nextBtnLabel)
        {
            daySummaryRoot = root;
            daySummaryTitleText = title;
            daySummaryBodyText = body;
            daySummaryNextDayButton = nextBtn;
            daySummaryNextBtnLabel = nextBtnLabel;
        }

        public void BindEndScreen(
            GameObject root, TextMeshProUGUI title, TextMeshProUGUI subtitle, TextMeshProUGUI body,
            Button restartBtn, Button mainMenuBtn)
        {
            endScreenRoot = root;
            endScreenTitleText = title;
            endScreenSubtitleText = subtitle;
            endScreenBodyText = body;
            endScreenRestartButton = restartBtn;
            endScreenMainMenuButton = mainMenuBtn;
        }

        private void Awake()
        {
            Instance = this;
            if (gameStateManager == null) gameStateManager = FindAnyObjectByType<GameStateManager>();
            if (statsManager == null) statsManager = FindAnyObjectByType<DormStatsManager>();
            if (dayTimeController == null) dayTimeController = FindAnyObjectByType<DayTimeController>();
            if (questManager == null) questManager = FindAnyObjectByType<QuestManager>();
            if (playerController == null) playerController = FindAnyObjectByType<FirstPersonController>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            if (gameStateManager != null)
            {
                gameStateManager.OnStateChanged += HandleStateChanged;
                gameStateManager.OnDaySummaryShown += PopulateDaySummary;
            }

            if (mainStartButton != null) mainStartButton.onClick.AddListener(OnStartClicked);
            if (mainSettingsButton != null) mainSettingsButton.onClick.AddListener(OnToggleMainSettings);
            if (mainQuitButton != null) mainQuitButton.onClick.AddListener(OnQuitClicked);
            if (weekIntroStartDayButton != null) weekIntroStartDayButton.onClick.AddListener(OnWeekIntroStartClicked);

            if (pauseResumeButton != null) pauseResumeButton.onClick.AddListener(OnResumeClicked);
            if (pauseRestartButton != null) pauseRestartButton.onClick.AddListener(OnRestartClicked);
            if (pauseMainMenuButton != null) pauseMainMenuButton.onClick.AddListener(OnMainMenuClicked);

            if (daySummaryNextDayButton != null) daySummaryNextDayButton.onClick.AddListener(OnContinueFromSummaryClicked);

            if (endScreenRestartButton != null) endScreenRestartButton.onClick.AddListener(OnRestartClicked);
            if (endScreenMainMenuButton != null) endScreenMainMenuButton.onClick.AddListener(OnMainMenuClicked);

            if (mainSensSlider != null) mainSensSlider.onValueChanged.AddListener(OnSensitivityChanged);
            if (pauseSensSlider != null) pauseSensSlider.onValueChanged.AddListener(OnSensitivityChanged);
            if (mainVolSlider != null) mainVolSlider.onValueChanged.AddListener(OnVolumeChanged);
            if (pauseVolSlider != null) pauseVolSlider.onValueChanged.AddListener(OnVolumeChanged);
        }

        private void OnDisable()
        {
            if (gameStateManager != null)
            {
                gameStateManager.OnStateChanged -= HandleStateChanged;
                gameStateManager.OnDaySummaryShown -= PopulateDaySummary;
            }

            if (mainStartButton != null) mainStartButton.onClick.RemoveListener(OnStartClicked);
            if (mainSettingsButton != null) mainSettingsButton.onClick.RemoveListener(OnToggleMainSettings);
            if (mainQuitButton != null) mainQuitButton.onClick.RemoveListener(OnQuitClicked);
            if (weekIntroStartDayButton != null) weekIntroStartDayButton.onClick.RemoveListener(OnWeekIntroStartClicked);

            if (pauseResumeButton != null) pauseResumeButton.onClick.RemoveListener(OnResumeClicked);
            if (pauseRestartButton != null) pauseRestartButton.onClick.RemoveListener(OnRestartClicked);
            if (pauseMainMenuButton != null) pauseMainMenuButton.onClick.RemoveListener(OnMainMenuClicked);

            if (daySummaryNextDayButton != null) daySummaryNextDayButton.onClick.RemoveListener(OnContinueFromSummaryClicked);

            if (endScreenRestartButton != null) endScreenRestartButton.onClick.RemoveListener(OnRestartClicked);
            if (endScreenMainMenuButton != null) endScreenMainMenuButton.onClick.RemoveListener(OnMainMenuClicked);

            if (mainSensSlider != null) mainSensSlider.onValueChanged.RemoveListener(OnSensitivityChanged);
            if (pauseSensSlider != null) pauseSensSlider.onValueChanged.RemoveListener(OnSensitivityChanged);
            if (mainVolSlider != null) mainVolSlider.onValueChanged.RemoveListener(OnVolumeChanged);
            if (pauseVolSlider != null) pauseVolSlider.onValueChanged.RemoveListener(OnVolumeChanged);
        }

        private void Start()
        {
            SyncSlidersFromSavedValues();
            if (mainSettingsContainer != null)
                mainSettingsContainer.SetActive(false);

            if (gameStateManager != null)
                HandleStateChanged(gameStateManager.CurrentState, gameStateManager.EndStateMessage);
        }

        private void HandleStateChanged(GameState state, string message)
        {
            if (mainMenuRoot != null) mainMenuRoot.SetActive(state == GameState.MainMenu);
            if (weekIntroRoot != null) weekIntroRoot.SetActive(state == GameState.WeekIntro);
            if (pauseRoot != null) pauseRoot.SetActive(state == GameState.Paused);
            if (daySummaryRoot != null) daySummaryRoot.SetActive(state == GameState.DaySummary);
            if (endScreenRoot != null) endScreenRoot.SetActive(state == GameState.Victory || state == GameState.GameOver);

            if (state == GameState.MainMenu || state == GameState.Paused)
                SyncSlidersFromSavedValues();

            if (state == GameState.Victory)
                PopulateVictoryScreen(message);
            else if (state == GameState.GameOver)
                PopulateGameOverScreen(message);
        }

        private void PopulateDaySummary(DaySummaryData data)
        {
            if (daySummaryTitleText != null)
                daySummaryTitleText.text = $"ПІДСУМКИ ДНЯ {data.CompletedDay} ({data.CompletedDayName})";

            if (daySummaryBodyText != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine(data.EndNote);
                sb.AppendLine();

                string respSign = data.RespectDelta >= 0f ? "+" : "";
                string trashSign = data.TrashDelta >= 0f ? "+" : "";
                sb.AppendLine($"<b>Виконано задач:</b> {data.CompletedQuestsCount} / {data.TotalQuestsCount}");
                sb.AppendLine($"<b>Респект за день:</b> {respSign}{Mathf.RoundToInt(data.RespectDelta)}  (Загалом: {Mathf.RoundToInt(data.CurrentRespect)})");
                sb.AppendLine($"<b>Треш за день:</b> {trashSign}{Mathf.RoundToInt(data.TrashDelta)}%  (Поточний: {Mathf.RoundToInt(data.CurrentTrash)}%)");
                sb.AppendLine($"<b>Догани:</b> {data.CurrentReprimands}/3   •   <b>Сила:</b> {data.Strength}/5   •   <b>Витривалість:</b> {data.Stamina}/5");
                sb.AppendLine();

                if (data.CompletedQuestTitles != null && data.CompletedQuestTitles.Count > 0)
                {
                    sb.AppendLine("<color=#62E882><b>Виконані задачі:</b></color>");
                    for (int i = 0; i < data.CompletedQuestTitles.Count; i++)
                        sb.AppendLine($"  • {data.CompletedQuestTitles[i]}");
                }

                if (data.MissedQuestTitles != null && data.MissedQuestTitles.Count > 0)
                {
                    sb.AppendLine("<color=#F2A365><b>Не завершено:</b></color>");
                    for (int i = 0; i < data.MissedQuestTitles.Count; i++)
                        sb.AppendLine($"  • {data.MissedQuestTitles[i]}");
                }

                if (data.NextDay >= 5)
                {
                    sb.AppendLine();
                    sb.AppendLine("<color=#FF6B57><b>УВАГА! Попереду П'ЯТНИЦЯ — кульмінація тижня!</b></color>");
                }

                daySummaryBodyText.text = sb.ToString().TrimEnd();
            }

            if (daySummaryNextBtnLabel != null)
                daySummaryNextBtnLabel.text = $"Перейти до Дня {data.NextDay} ({data.NextDayName})";
        }

        private void PopulateVictoryScreen(string message)
        {
            if (endScreenTitleText != null)
            {
                endScreenTitleText.text = "ТИЖДЕНЬ ЗАКРИТО!";
                endScreenTitleText.color = new Color(0.42f, 0.92f, 0.56f, 1f);
            }

            if (endScreenSubtitleText != null)
                endScreenSubtitleText.text = string.IsNullOrEmpty(message) ? "Ви вижили у Гуртожитку №7!" : message;

            if (endScreenBodyText != null && statsManager != null)
            {
                int questsDone = questManager != null ? questManager.TotalCompletedQuestsWeek : 0;
                bool bossWon = questManager != null && questManager.FridayBossChallengeCompleted;
                string rank = (statsManager.Respect >= 50f && bossWon)
                    ? "ЛЕГЕНДА 1-ГО ПОВЕРХУ"
                    : (statsManager.Respect >= 30f ? "СВІЙ ЧОЛОВІК У ГУРТОЖИТКУ" : "ВЦІЛІЛИЙ ПЕРШОКУРСНИК");

                var sb = new StringBuilder();
                sb.AppendLine($"<b>Статус тижня:</b> {rank}");
                sb.AppendLine($"<b>Виконано задач за тиждень:</b> {questsDone}");
                sb.AppendLine($"<b>Фінальний Респект:</b> {Mathf.RoundToInt(statsManager.Respect)}");
                sb.AppendLine($"<b>Сила / Витривалість:</b> {statsManager.Strength}/5  •  {statsManager.Stamina}/5");
                sb.AppendLine($"<b>Треш на фініші:</b> {Mathf.RoundToInt(statsManager.Trash)}%   •   <b>Догани:</b> {statsManager.Reprimands}/3");
                sb.AppendLine($"<b>П'ятничний виклик Боса качалки:</b> {(bossWon ? "ВИГРАНО!" : "Пропущено")}");
                endScreenBodyText.text = sb.ToString().TrimEnd();
            }
        }

        private void PopulateGameOverScreen(string reasonMessage)
        {
            if (endScreenTitleText != null)
            {
                endScreenTitleText.text = "НАКАЗ ПРО ВИСЕЛЕННЯ";
                endScreenTitleText.color = new Color(0.95f, 0.28f, 0.24f, 1f);
            }

            if (endScreenSubtitleText != null)
                endScreenSubtitleText.text = string.IsNullOrEmpty(reasonMessage) ? "Вас відраховано з гуртожитку." : reasonMessage;

            if (endScreenBodyText != null && statsManager != null)
            {
                int dayReached = dayTimeController != null ? dayTimeController.CurrentDay : 1;
                string dayName = dayTimeController != null ? dayTimeController.CurrentDayFullName : "Понеділок";
                int questsDone = questManager != null ? questManager.TotalCompletedQuestsWeek : 0;

                var sb = new StringBuilder();
                sb.AppendLine($"<b>Протримались до:</b> День {dayReached} ({dayName})");
                sb.AppendLine($"<b>Виконано задач:</b> {questsDone}");
                sb.AppendLine($"<b>Респект:</b> {Mathf.RoundToInt(statsManager.Respect)}   •   <b>Треш:</b> {Mathf.RoundToInt(statsManager.Trash)}%");
                sb.AppendLine($"<b>Офіційні догани:</b> {statsManager.Reprimands}/3");
                sb.AppendLine();
                sb.AppendLine("<color=#C8C8C8>Порада: стежте за задачами у смартфоні [Tab], тримайте Треш нижче 50% до 22:00 і не забувайте про Боса качалки.</color>");
                endScreenBodyText.text = sb.ToString().TrimEnd();
            }
        }

        private void SyncSlidersFromSavedValues()
        {
            _syncingSliders = true;
            float sens = playerController != null
                ? playerController.MouseSensitivityMultiplier
                : PlayerPrefs.GetFloat(FirstPersonController.MouseSensPrefKey, 1f);
            float vol = gameStateManager != null
                ? gameStateManager.GetMasterVolume()
                : PlayerPrefs.GetFloat(GameStateManager.MasterVolumePrefKey, 0.85f);

            if (mainSensSlider != null) mainSensSlider.value = sens;
            if (pauseSensSlider != null) pauseSensSlider.value = sens;
            if (mainVolSlider != null) mainVolSlider.value = vol;
            if (pauseVolSlider != null) pauseVolSlider.value = vol;

            UpdateSettingsLabels(sens, vol);
            _syncingSliders = false;
        }

        private void OnSensitivityChanged(float value)
        {
            if (_syncingSliders) return;
            playerController?.SetMouseSensitivityMultiplier(value);
            _syncingSliders = true;
            if (mainSensSlider != null) mainSensSlider.value = value;
            if (pauseSensSlider != null) pauseSensSlider.value = value;
            _syncingSliders = false;
            UpdateSettingsLabels(value, gameStateManager != null ? gameStateManager.GetMasterVolume() : AudioListener.volume);
        }

        private void OnVolumeChanged(float value)
        {
            if (_syncingSliders) return;
            gameStateManager?.SetMasterVolume(value);
            _syncingSliders = true;
            if (mainVolSlider != null) mainVolSlider.value = value;
            if (pauseVolSlider != null) pauseVolSlider.value = value;
            _syncingSliders = false;
            float sens = playerController != null ? playerController.MouseSensitivityMultiplier : 1f;
            UpdateSettingsLabels(sens, value);
        }

        private void UpdateSettingsLabels(float sens, float vol)
        {
            string sensText = $"Чутливість миші: {sens:F2}x";
            string volText = $"Гучність звуку: {Mathf.RoundToInt(vol * 100f)}%";
            if (mainSensValueText != null) mainSensValueText.text = sensText;
            if (pauseSensValueText != null) pauseSensValueText.text = sensText;
            if (mainVolValueText != null) mainVolValueText.text = volText;
            if (pauseVolValueText != null) pauseVolValueText.text = volText;
        }

        private void OnStartClicked() => gameStateManager?.StartWeekIntro();
        private void OnToggleMainSettings()
        {
            if (mainSettingsContainer != null)
                mainSettingsContainer.SetActive(!mainSettingsContainer.activeSelf);
        }
        private void OnQuitClicked() => gameStateManager?.QuitGame();
        private void OnWeekIntroStartClicked() => gameStateManager?.BeginGameplay();
        private void OnResumeClicked() => gameStateManager?.ResumeGame();
        private void OnRestartClicked() => gameStateManager?.RestartScene();
        private void OnMainMenuClicked() => gameStateManager?.ReturnToMainMenu();
        private void OnContinueFromSummaryClicked() => gameStateManager?.ContinueFromDaySummary();
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Dormitory7.Core
{
    public enum GameState
    {
        MainMenu = 0,
        WeekIntro = 1,
        Playing = 2,
        Smartphone = 3,
        Paused = 4,
        DaySummary = 5,
        GameOver = 6,
        Victory = 7
    }

    public struct DaySummaryData
    {
        public int CompletedDay;
        public int NextDay;
        public string CompletedDayName;
        public string NextDayName;
        public string EndNote;
        public int CompletedQuestsCount;
        public int TotalQuestsCount;
        public List<string> CompletedQuestTitles;
        public List<string> MissedQuestTitles;
        public float RespectDelta;
        public float TrashDelta;
        public float CurrentRespect;
        public float CurrentTrash;
        public int CurrentReprimands;
        public int Strength;
        public int Stamina;
    }

    public class GameStateManager : MonoBehaviour
    {
        public static GameStateManager Instance { get; private set; }
        public const string MasterVolumePrefKey = "Dorm7_MasterVolume";

        [SerializeField] private DormStatsManager statsManager;
        [SerializeField] private DayTimeController dayTimeController;
        [SerializeField] private bool startInMainMenu = true;

        public GameState CurrentState { get; private set; } = GameState.MainMenu;
        public string EndStateMessage { get; private set; } = "";
        public DaySummaryData LastDaySummary { get; private set; }
        public bool IsAnyQteActive => _activeQteCancelHandler != null;

        public event Action<GameState, string> OnStateChanged;
        public event Action<DaySummaryData> OnDaySummaryShown;

        private InputAction _pauseAction;
        private InputAction _smartphoneAction;
        private Action _activeQteCancelHandler;

        public void RegisterActiveQte(Action onCancel)
        {
            _activeQteCancelHandler = onCancel;
        }

        public void UnregisterActiveQte(Action onCancel)
        {
            if (_activeQteCancelHandler == onCancel)
                _activeQteCancelHandler = null;
        }

        public void SetReferences(DormStatsManager stats, DayTimeController dayTime)
        {
            statsManager = stats;
            dayTimeController = dayTime;
        }

        public void SetMasterVolume(float volume)
        {
            float clamped = Mathf.Clamp01(volume);
            AudioListener.volume = clamped;
            PlayerPrefs.SetFloat(MasterVolumePrefKey, clamped);
        }

        public float GetMasterVolume()
        {
            return Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePrefKey, 0.85f));
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (statsManager == null)
                statsManager = FindAnyObjectByType<DormStatsManager>();
            if (dayTimeController == null)
                dayTimeController = FindAnyObjectByType<DayTimeController>();

            AudioListener.volume = GetMasterVolume();

            _pauseAction = new InputAction("Pause", InputActionType.Button);
            _pauseAction.AddBinding("<Keyboard>/escape");
            _pauseAction.AddBinding("<Gamepad>/start");
            _pauseAction.performed += OnPausePerformed;

            _smartphoneAction = new InputAction("Smartphone", InputActionType.Button);
            _smartphoneAction.AddBinding("<Keyboard>/tab");
            _smartphoneAction.AddBinding("<Gamepad>/select");
            _smartphoneAction.performed += OnSmartphonePerformed;
        }

        private void OnEnable()
        {
            _pauseAction?.Enable();
            _smartphoneAction?.Enable();
            if (statsManager != null)
                statsManager.OnGameOver += HandleGameOver;
            if (dayTimeController != null)
                dayTimeController.OnWeekCompleted += HandleWeekCompleted;
        }

        private void OnDisable()
        {
            _pauseAction?.Disable();
            _smartphoneAction?.Disable();
            if (statsManager != null)
                statsManager.OnGameOver -= HandleGameOver;
            if (dayTimeController != null)
                dayTimeController.OnWeekCompleted -= HandleWeekCompleted;
        }

        private void OnDestroy()
        {
            if (_pauseAction != null)
            {
                _pauseAction.performed -= OnPausePerformed;
                _pauseAction.Dispose();
            }
            if (_smartphoneAction != null)
            {
                _smartphoneAction.performed -= OnSmartphonePerformed;
                _smartphoneAction.Dispose();
            }
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            SetState(startInMainMenu ? GameState.MainMenu : GameState.Playing, "");
        }

        private void OnPausePerformed(InputAction.CallbackContext ctx)
        {
            // 1. If a Repair or Gym QTE is active, Esc cancels the QTE cleanly instead of pausing!
            if (_activeQteCancelHandler != null)
            {
                Action cancel = _activeQteCancelHandler;
                _activeQteCancelHandler = null;
                cancel.Invoke();
                return;
            }

            // 2. If Smartphone is open, Esc closes Smartphone back to Playing!
            if (CurrentState == GameState.Smartphone)
            {
                SetState(GameState.Playing, "");
                return;
            }

            // 3. Toggle Pause <-> Playing
            if (CurrentState == GameState.Playing)
                SetState(GameState.Paused, "Пауза");
            else if (CurrentState == GameState.Paused)
                SetState(GameState.Playing, "");
        }

        private void OnSmartphonePerformed(InputAction.CallbackContext ctx)
        {
            ToggleSmartphone();
        }

        public void ToggleSmartphone()
        {
            if (IsAnyQteActive) return;

            if (CurrentState == GameState.Playing)
                SetState(GameState.Smartphone, "");
            else if (CurrentState == GameState.Smartphone)
                SetState(GameState.Playing, "");
        }

        public void StartWeekIntro()
        {
            SetState(GameState.WeekIntro, "");
        }

        public void BeginGameplay()
        {
            SetState(GameState.Playing, "");
        }

        public void ShowDaySummary(DaySummaryData data)
        {
            if (CurrentState == GameState.GameOver || CurrentState == GameState.Victory)
                return;
            LastDaySummary = data;
            SetState(GameState.DaySummary, data.EndNote ?? "");
            OnDaySummaryShown?.Invoke(data);
        }

        public void ContinueFromDaySummary()
        {
            if (CurrentState == GameState.DaySummary)
                SetState(GameState.Playing, "");
        }

        public void ReturnToMainMenu()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void QuitGame()
        {
            PlayerPrefs.Save();
            Application.Quit();
        }

        private void HandleGameOver(GameOverReason reason, string message)
        {
            SetState(GameState.GameOver, message);
        }

        private void HandleWeekCompleted()
        {
            SetState(GameState.Victory, "Тиждень закрито! Ви вижили у Гуртожитку №7.");
        }

        public void SetState(GameState newState, string message)
        {
            CurrentState = newState;
            EndStateMessage = message;

            bool isPlaying = newState == GameState.Playing;
            Time.timeScale = isPlaying ? 1f : 0f;
            Cursor.lockState = isPlaying ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !isPlaying;

            OnStateChanged?.Invoke(CurrentState, EndStateMessage);
        }

        public void ResumeGame()
        {
            if (CurrentState == GameState.Paused || CurrentState == GameState.Smartphone)
                SetState(GameState.Playing, "");
        }

        public void RestartScene()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}

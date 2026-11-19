using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Dormitory7.Core
{
    public enum GameState
    {
        Playing = 0,
        Paused = 1,
        GameOver = 2,
        Victory = 3
    }

    public class GameStateManager : MonoBehaviour
    {
        public static GameStateManager Instance { get; private set; }

        [SerializeField] private DormStatsManager statsManager;
        [SerializeField] private DayTimeController dayTimeController;

        public GameState CurrentState { get; private set; } = GameState.Playing;
        public string EndStateMessage { get; private set; } = "";

        public event Action<GameState, string> OnStateChanged;

        private InputAction _pauseAction;

        public void SetReferences(DormStatsManager stats, DayTimeController dayTime)
        {
            statsManager = stats;
            dayTimeController = dayTime;
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

            _pauseAction = new InputAction("Pause", InputActionType.Button);
            _pauseAction.AddBinding("<Keyboard>/escape");
            _pauseAction.AddBinding("<Gamepad>/start");
            _pauseAction.performed += OnPausePerformed;
        }

        private void OnEnable()
        {
            _pauseAction?.Enable();
            if (statsManager != null)
                statsManager.OnGameOver += HandleGameOver;
            if (dayTimeController != null)
                dayTimeController.OnWeekCompleted += HandleWeekCompleted;
        }

        private void OnDisable()
        {
            _pauseAction?.Disable();
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
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            SetState(GameState.Playing, "");
        }

        private void OnPausePerformed(InputAction.CallbackContext ctx)
        {
            if (CurrentState == GameState.Playing)
                SetState(GameState.Paused, "Пауза");
            else if (CurrentState == GameState.Paused)
                SetState(GameState.Playing, "");
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
            if (CurrentState == GameState.Paused)
                SetState(GameState.Playing, "");
        }

        public void RestartScene()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}

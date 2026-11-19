using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Interaction;
using Dormitory7.UI;
using UnityEngine;

namespace Dormitory7.Features.Cockroaches
{
    public enum MessType
    {
        Puddle = 0,
        TrashPile = 1
    }

    public class MessSpot : MonoBehaviour, IHoldInteractable
    {
        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private MessType messType = MessType.Puddle;

        public bool IsCleaned { get; private set; }
        public float CleanProgress { get; private set; }
        public MessType Type => messType;

        private DormStatsManager _stats;
        private Vector3 _initialScale;

        public float HoldDuration => balanceConfig != null ? Mathf.Max(0.5f, balanceConfig.cleanHoldSeconds) : 1.8f;

        public void Configure(BalanceConfig config, MessType type)
        {
            balanceConfig = config;
            messType = type;
        }

        private void Awake()
        {
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();
            _initialScale = transform.localScale;
        }

        private void Start()
        {
            _stats = FindAnyObjectByType<DormStatsManager>();
        }

        private void Update()
        {
            if (IsCleaned || _stats == null || balanceConfig == null) return;
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            _stats.ModifyTrash((balanceConfig.messTrashPerMinute / 60f) * Time.deltaTime);
        }

        public bool ApplyMopCleaning(float deltaTime)
        {
            if (IsCleaned) return false;
            float duration = HoldDuration;
            CleanProgress = Mathf.Clamp01(CleanProgress + deltaTime / duration);

            float scaleFactor = Mathf.Lerp(1f, 0.25f, CleanProgress);
            transform.localScale = new Vector3(_initialScale.x * scaleFactor, _initialScale.y, _initialScale.z * scaleFactor);

            if (CleanProgress >= 1f)
            {
                CompleteCleaning();
                return true;
            }
            return false;
        }

        private void CompleteCleaning()
        {
            if (IsCleaned) return;
            IsCleaned = true;

            float reduction = balanceConfig != null ? balanceConfig.cleanTrashReduction : 5f;
            if (_stats != null)
                _stats.ModifyTrash(-reduction);

            string label = messType == MessType.Puddle ? "Калюжу витерто" : "Сміття прибрано";
            HudController.Instance?.ShowToast($"{label}! (-{Mathf.RoundToInt(reduction)}% Трешу)");
            gameObject.SetActive(false);
        }

        public string GetPrompt(PlayerContext context)
        {
            if (IsCleaned) return "";
            string noun = messType == MessType.Puddle ? "калюжу" : "сміття";
            bool hasMop = context.Inventory != null && context.Inventory.HasActiveItemWithId("mop");
            return hasMop
                ? $"[Утрим. ЛКМ або E] Прибрати {noun} шваброю"
                : $"Бруд ({noun}) — візьміть Швабру, щоб прибрати";
        }

        public bool CanInteract(PlayerContext context)
        {
            if (IsCleaned || context.Inventory == null) return false;
            return context.Inventory.HasActiveItemWithId("mop");
        }

        public void OnHoldStart(PlayerContext context) { }

        public void OnHoldProgress(PlayerContext context, float normalizedProgress)
        {
            CleanProgress = Mathf.Max(CleanProgress, normalizedProgress);
            float scaleFactor = Mathf.Lerp(1f, 0.25f, CleanProgress);
            transform.localScale = new Vector3(_initialScale.x * scaleFactor, _initialScale.y, _initialScale.z * scaleFactor);
        }

        public void OnHoldCancel(PlayerContext context) { }

        public void Interact(PlayerContext context)
        {
            CompleteCleaning();
        }
    }
}

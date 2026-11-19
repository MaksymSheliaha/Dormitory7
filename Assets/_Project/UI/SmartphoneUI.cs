using System.Text;
using Dormitory7.Core;
using Dormitory7.Features.Quests;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dormitory7.UI
{
    public class SmartphoneUI : MonoBehaviour
    {
        [Header("Core References")]
        [SerializeField] private GameStateManager gameStateManager;
        [SerializeField] private DayTimeController dayTimeController;
        [SerializeField] private DormStatsManager statsManager;
        [SerializeField] private QuestManager questManager;

        [Header("Phone Root & Header")]
        [SerializeField] private GameObject phoneOverlayRoot;
        [SerializeField] private TextMeshProUGUI statusBarText;
        [SerializeField] private TextMeshProUGUI statsSummaryText;
        [SerializeField] private TextMeshProUGUI questsBodyText;
        [SerializeField] private TextMeshProUGUI floorChatBodyText;
        [SerializeField] private Button closeButton;

        public bool IsOpen => phoneOverlayRoot != null && phoneOverlayRoot.activeSelf;

        public void Bind(
            GameStateManager stateMgr,
            DayTimeController dayTime,
            DormStatsManager stats,
            QuestManager quests,
            GameObject root,
            TextMeshProUGUI statusBar,
            TextMeshProUGUI statsText,
            TextMeshProUGUI questsText,
            TextMeshProUGUI chatText,
            Button closeBtn)
        {
            gameStateManager = stateMgr;
            dayTimeController = dayTime;
            statsManager = stats;
            questManager = quests;
            phoneOverlayRoot = root;
            statusBarText = statusBar;
            statsSummaryText = statsText;
            questsBodyText = questsText;
            floorChatBodyText = chatText;
            closeButton = closeBtn;
        }

        private void Awake()
        {
            if (gameStateManager == null) gameStateManager = FindAnyObjectByType<GameStateManager>();
            if (dayTimeController == null) dayTimeController = FindAnyObjectByType<DayTimeController>();
            if (statsManager == null) statsManager = FindAnyObjectByType<DormStatsManager>();
            if (questManager == null) questManager = FindAnyObjectByType<QuestManager>();
        }

        private void OnEnable()
        {
            if (gameStateManager != null)
                gameStateManager.OnStateChanged += HandleStateChanged;
            if (questManager != null)
            {
                questManager.OnQuestsChanged += RefreshContent;
                questManager.OnChatChanged += RefreshContent;
            }
            if (closeButton != null)
                closeButton.onClick.AddListener(OnCloseClicked);
        }

        private void OnDisable()
        {
            if (gameStateManager != null)
                gameStateManager.OnStateChanged -= HandleStateChanged;
            if (questManager != null)
            {
                questManager.OnQuestsChanged -= RefreshContent;
                questManager.OnChatChanged -= RefreshContent;
            }
            if (closeButton != null)
                closeButton.onClick.RemoveListener(OnCloseClicked);
        }

        private void Start()
        {
            if (phoneOverlayRoot != null)
                phoneOverlayRoot.SetActive(false);
        }

        private void HandleStateChanged(GameState state, string _)
        {
            bool show = state == GameState.Smartphone;
            if (phoneOverlayRoot != null)
                phoneOverlayRoot.SetActive(show);

            if (show)
            {
                questManager?.MarkChatAsRead();
                RefreshContent();
            }
        }

        public void RefreshContent()
        {
            if (phoneOverlayRoot == null || !phoneOverlayRoot.activeSelf)
                return;

            // 1. Top phone status bar
            if (statusBarText != null)
            {
                string timeStr = dayTimeController != null ? dayTimeController.FormattedDayAndTime : "День 1 • 08:00";
                int battery = statsManager != null ? Mathf.RoundToInt(statsManager.Energy) : 100;
                statusBarText.text = $"ДОРМ-7 LTE   |   {timeStr}   |   ЗАРЯД (ЕНЕРГІЯ): {battery}%";
            }

            // 2. Player stats strip (Strength / Stamina / Respect / Reprimands / Trash)
            if (statsSummaryText != null && statsManager != null)
            {
                int maxRep = statsManager.Config != null ? statsManager.Config.maxReprimands : 3;
                string buffTag = statsManager.HasGymBuff ? "  •  <color=#F5B041>БАФ КАВИ</color>" : "";
                statsSummaryText.text =
                    $"<b>СИЛА:</b> {statsManager.Strength}/5   •   <b>ВИТРИВАЛІСТЬ:</b> {statsManager.Stamina}/5   •   " +
                    $"<b>РЕСПЕКТ:</b> {Mathf.RoundToInt(statsManager.Respect)}\n" +
                    $"<b>ТРЕШ:</b> {Mathf.RoundToInt(statsManager.Trash)}%   •   <b>ДОГАНИ:</b> {statsManager.Reprimands}/{maxRep}{buffTag}";
            }

            // 3. Active Quests (Daily + Weekly + Friday Climax)
            if (questsBodyText != null && questManager != null)
            {
                var sb = new StringBuilder();
                var list = questManager.ActiveQuests;
                if (list.Count == 0)
                {
                    sb.AppendLine("На сьогодні активних задач немає.");
                }
                else
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var q = list[i];
                        if (q?.Definition == null) continue;

                        string catTag = q.Definition.category switch
                        {
                            QuestCategory.Weekly => "<color=#66C2FF>[ТИЖНЕВИЙ]</color>",
                            QuestCategory.Climax => "<color=#FF6B57>[П'ЯТНИЦЯ!]</color>",
                            _ => "<color=#F5C451>[ЩОДЕННИЙ]</color>"
                        };

                        string statusTag = q.IsCompleted
                            ? "<color=#5CE67A>[ВИКОНАНО]</color>"
                            : $"<color=#E6E6E6>({q.CurrentCount}/{q.TargetCount})</color>";

                        sb.AppendLine($"{catTag} <b>{q.Definition.title}</b> {statusTag}");
                        sb.AppendLine($"  <color=#B8C2BA>{q.Definition.description}</color>");
                        sb.AppendLine();
                    }
                }
                questsBodyText.text = sb.ToString().TrimEnd();
            }

            // 4. Floor Chat (most recent messages first or chronological latest 10)
            if (floorChatBodyText != null && questManager != null)
            {
                var sb = new StringBuilder();
                var msgs = questManager.FloorChat;
                int start = Mathf.Max(0, msgs.Count - 10);
                for (int i = start; i < msgs.Count; i++)
                {
                    var m = msgs[i];
                    string headerColor = m.IsAlert ? "#FF6B57" : "#8ED09A";
                    sb.AppendLine($"<color={headerColor}><b>{m.Sender}</b></color> <color=#8A948D>[{m.Timestamp}]</color>");
                    sb.AppendLine($"  {m.Body}");
                }
                floorChatBodyText.text = sb.ToString().TrimEnd();
            }
        }

        private void OnCloseClicked()
        {
            gameStateManager?.ToggleSmartphone();
        }
    }
}

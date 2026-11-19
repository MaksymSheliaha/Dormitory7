using UnityEngine;

namespace Dormitory7.Features.Quests
{
    public enum QuestCategory
    {
        Daily = 0,
        Weekly = 1,
        Climax = 2
    }

    public enum QuestObjectiveType
    {
        CookMeal = 0,
        KillCockroaches = 1,
        GreetGymBoss = 2,
        CompleteGymWorkout = 3,
        RepairHazard = 4,
        CleanMessOrDispose = 5,
        GiveBossGift = 6,
        FridayBossBenchChallenge = 7
    }

    [CreateAssetMenu(fileName = "Quest_New", menuName = "Dormitory7/Quest Definition")]
    public class QuestDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string questId = "quest_default";
        public string title = "Задача поверху";
        [TextArea(2, 3)]
        public string description = "Виконайте завдання в гуртожитку.";
        public QuestCategory category = QuestCategory.Daily;

        [Header("Objective")]
        public QuestObjectiveType objectiveType = QuestObjectiveType.CookMeal;
        [Min(1)] public int targetCount = 1;
        [Range(1, 5)] public int minDay = 1;
        [Range(1, 5)] public int maxDay = 5;

        [Header("Rewards")]
        public float respectReward = 5f;
        public float trashReliefReward = 3f;
    }
}

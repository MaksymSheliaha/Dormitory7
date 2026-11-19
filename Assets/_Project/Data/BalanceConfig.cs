using UnityEngine;

namespace Dormitory7.Data
{
    [CreateAssetMenu(fileName = "BalanceConfig", menuName = "Dormitory7/Balance Config")]
    public class BalanceConfig : ScriptableObject
    {
        [Header("Energy / Satiety (0-100%)")]
        public float startingEnergy = 75f;
        public float maxEnergy = 100f;
        public float lowEnergyWarningThreshold = 20f;
        public float gymMinEnergyThreshold = 15f;
        public float sleepEnergyRestore = 25f;
        public float passiveEnergyDrainPerGameHour = 2.2f;
        public float sprintEnergyDrainPerSecond = 6.0f;
        public float exhaustionGameOverSeconds = 12f;

        [Header("Social Status / Respect (0-100)")]
        public float startingRespect = 10f;
        public float minRespect = 0f;
        public float maxRespect = 100f;

        [Header("Trash & Reprimands")]
        public float cockroachTrashPerMinute = 0.5f;
        public float komendantTrashThreshold = 50f;
        public float maxTrash = 100f;
        public int maxReprimands = 3;

        [Header("Day Cycle & Week")]
        [Range(0f, 23f)] public float dayStartHour = 8f;
        [Range(0f, 24f)] public float curfewWarningHour = 21.5f;
        [Range(0f, 24f)] public float curfewHour = 22f;
        [Tooltip("Real seconds to pass from dayStartHour (08:00) to curfewHour (22:00). GDD: 5-8 min.")]
        public float realSecondsFromStartToCurfew = 360f;
        public int daysInWeek = 5;

        [Header("Player Movement")]
        public float walkSpeed = 4.2f;
        public float sprintSpeed = 6.8f;
        public float crouchSpeed = 2.2f;
        public float jumpHeight = 1.15f;
        public float gravity = -20f;
        public float standingHeight = 1.8f;
        public float crouchHeight = 1.05f;
        public float standingCameraY = 1.62f;
        public float crouchCameraY = 0.92f;
        public float crouchTransitionSpeed = 10f;
        public float mouseSensitivity = 0.14f;
        public float gamepadLookSpeed = 140f;

        [Header("Stealth Noise Levels (0-1)")]
        [Range(0f, 1f)] public float idleNoise = 0.02f;
        [Range(0f, 1f)] public float crouchNoise = 0.12f;
        [Range(0f, 1f)] public float walkNoise = 0.45f;
        [Range(0f, 1f)] public float sprintNoise = 1.0f;

        [Header("Interaction & Throwing")]
        public float interactDistance = 2.8f;
        public float dropForwardOffset = 0.95f;
        public float baseThrowForce = 9.5f;
        public float throwUpwardBoost = 2.2f;
        public float hotPanCoolingSeconds = 18f;

        [Header("Track A — Cockroaches & Cleaning")]
        public float cockroachPatrolSpeed = 1.55f;
        public float cockroachFleeSpeed = 3.5f;
        public float cockroachFleePlayerDistance = 3.2f;
        public float cockroachLightFleeDistance = 4.6f;
        public float cockroachHealth = 1.0f;
        public float slipperMeleeRange = 2.3f;
        public float slipperMeleeDamage = 1.2f;
        public float fryingPanMeleeRange = 2.4f;
        public float fryingPanMeleeDamage = 1.8f;
        public float dichlorvosRange = 3.2f;
        public float dichlorvosDps = 2.2f;
        public float messTrashPerMinute = 0.8f;
        public float cleanHoldSeconds = 1.8f;
        public float cleanTrashReduction = 5.0f;
        public float disposeCockroachTrashReduction = 2.5f;

        [Header("Track B — Cooking")]
        public float cookTimeToFlipSeconds = 7.0f;
        public float flipWindowSeconds = 8.0f;
        public float cookTimeAfterFlipSeconds = 6.0f;
        public float readyBeforeBurnSeconds = 9.0f;
        public float cookedMealEnergyRestore = 50f;
        public float mivinaEnergyRestore = 25f;
        public float burnTrashPerSecond = 1.2f;

        [Header("Track C — Repairs")]
        public float pipeBurstTrashPerMinute = 2.5f;
        public float cloggedToiletTrashPerMinute = 2.0f;
        public float electricalTripTrashPerMinute = 1.8f;
        public float tempTapePatchSeconds = 55f;
        public float repairBaseProgressSpeed = 0.28f;
        public float repairEnergyCost = 8f;
        public float repairTrashRelief = 7f;

        [Header("Track C — Basement Gym")]
        public float gymWorkoutEnergyCost = 14f;
        public int gymRepsPerSet = 4;
        public float gymSuccessRespect = 6f;
        public float gymFailEnergyPenalty = 15f;
        public float gymFailRespectPenalty = 5f;
        public float gymBuffDurationSeconds = 90f;
        public float gymBuffZoneBonus = 0.12f;

        [Header("Week 3 — Gym Boss NPC")]
        public float gymBossGreetRespect = 7f;
        public float gymBossMorningShowerMultiplier = 2f;
        public float gymBossIgnoreRespectPenalty = 8f;
        public float gymBossShowerEndHour = 10.5f;
        public float gymBossWashroomEndHour = 12.0f;
        public float gymBossKitchenEndHour = 14.5f;

        [Header("Week 3 — Komendant (Zinaida Petrivna)")]
        public float komendantPatrolSpeed = 2.6f;
        public float komendantChaseSpeed = 4.4f;
        public float komendantVisionRange = 11.5f;
        public float komendantVisionHalfAngle = 36f;
        public float komendantDarkRoomVisionMultiplier = 0.42f;
        public float komendantHearingMaxRadius = 11.0f;
        public float komendantCatchDistance = 1.65f;
        public float komendantSuspicionSeconds = 5.5f;
        public float komendantBribeWindowSeconds = 5.0f;
        public float komendantBribeTrashRelief = 10f;
        public int komendantWeeklyBribeLimit = 1;

        [Header("Week 3 — Hungry Neighbors")]
        public float neighborWalkSpeed = 2.4f;
        public float neighborFleeSpeed = 4.5f;
        public float neighborUnattendedPlayerDist = 7.5f;
        public int neighborIntimidateMinStrength = 2;
        public float neighborIntimidateMinRespect = 25f;
        public float neighborTheftRespectPenalty = 6f;
        public float neighborReclaimRespectBonus = 4f;
        public float neighborMessCooldownSeconds = 55f;

        [Header("Week 3 — Quests, Escalation & Friday Climax")]
        public float baseHazardIntervalSeconds = 110f;
        public float fridayHazardIntervalMultiplier = 0.55f;
        public float fridayKomendantSpeedMultiplier = 1.22f;
        public float fridayKomendantVisionMultiplier = 1.20f;
        public int fridayBossChallengeReps = 6;
        public float fridayBossChallengeRespect = 18f;

        [Header("MoSCoW Should Features (GDD 10.1)")]
        public float yeastSabotageTrashSpike = 8f;
        public float friedCockroachEnergyRestore = 10f;
        public float friedCockroachWitnessRespectPenalty = 5f;
        public float friedCockroachWitnessRange = 8.5f;
        public float barterRespectBonus = 2f;
        public int armWrestleCooldownDays = 2;
        public float armWrestleWinRespect = 12f;
        public float armWrestleLoseRespect = 3f;
        public float armWrestleLoseEnergy = 8f;
    }
}

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
    }
}

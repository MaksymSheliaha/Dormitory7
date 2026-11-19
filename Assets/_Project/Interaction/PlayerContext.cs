using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Player;
using UnityEngine;

namespace Dormitory7.Interaction
{
    public readonly struct PlayerContext
    {
        public GameObject PlayerObject { get; }
        public Transform CameraTransform { get; }
        public FirstPersonController Controller { get; }
        public QuickInventory Inventory { get; }
        public DormStatsManager Stats { get; }
        public DayTimeController DayTime { get; }

        public ItemDefinition ActiveItem => Inventory != null ? Inventory.GetActiveItemDefinition() : null;
        public bool IsCarryingTwoHanded => Inventory != null && Inventory.IsCarryingTwoHanded;
        public float CurrentEnergy => Stats != null ? Stats.Energy : 100f;

        public PlayerContext(
            GameObject playerObject,
            Transform cameraTransform,
            FirstPersonController controller,
            QuickInventory inventory,
            DormStatsManager stats,
            DayTimeController dayTime)
        {
            PlayerObject = playerObject;
            CameraTransform = cameraTransform;
            Controller = controller;
            Inventory = inventory;
            Stats = stats;
            DayTime = dayTime;
        }
    }
}

using System;
using UnityEngine;

namespace Dormitory7.Data
{
    public enum ItemCarryType
    {
        SmallInventory = 0,
        TwoHandedCarry = 1
    }

    [Flags]
    public enum ItemTag
    {
        None = 0,
        BossGift = 1 << 0,
        RepairTool = 1 << 1,
        CleaningTool = 1 << 2,
        Weapon = 1 << 3,
        Food = 1 << 4,
        Contraband = 1 << 5,
        Bribe = 1 << 6,
        HotCookware = 1 << 7
    }

    [CreateAssetMenu(fileName = "ItemDefinition", menuName = "Dormitory7/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string itemId = "item_id";
        public string displayName = "Предмет";
        [TextArea(1, 3)] public string description = "";

        [Header("Carry & Tags")]
        public ItemCarryType carryType = ItemCarryType.SmallInventory;
        public ItemTag tags = ItemTag.None;

        [Header("Hot Cookware Cooling")]
        [Tooltip("If assigned, a hot two-handed item cools down into this definition after coolingDurationSeconds.")]
        public ItemDefinition coolsToItem;
        public float coolingDurationSeconds = 18f;

        [Header("Gameplay Values")]
        public float energyRestore = 0f;
        public float respectGiftBonus = 0f;
        public float throwForceMultiplier = 1f;
        public float physicsMass = 1f;
        public Color tintColor = new Color(0.8f, 0.8f, 0.8f, 1f);

        [Header("Optional Prefab")]
        public GameObject worldPrefab;

        public bool HasTag(ItemTag tag) => (tags & tag) == tag;
        public bool IsHot => HasTag(ItemTag.HotCookware) && coolsToItem != null;
    }
}

using System;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Interaction;
using Dormitory7.Player;
using Dormitory7.UI;
using UnityEngine;

namespace Dormitory7.Features.Cockroaches
{
    public class PlayerToolController : MonoBehaviour
    {
        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private QuickInventory inventory;
        [SerializeField] private DormStatsManager statsManager;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private GameObject sprayCloudVisual;

        public static event Action<WorldItem, Vector3, Collider> OnPlayerMeleeStrike;

        public bool IsSpraying { get; private set; }

        public void Configure(BalanceConfig config, QuickInventory inv, DormStatsManager stats, Transform cam, GameObject sprayVisual)
        {
            balanceConfig = config;
            inventory = inv;
            statsManager = stats;
            cameraTransform = cam;
            sprayCloudVisual = sprayVisual;
        }

        private void Awake()
        {
            if (balanceConfig == null) balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();
            if (inventory == null) inventory = GetComponent<QuickInventory>();
            if (statsManager == null) statsManager = FindAnyObjectByType<DormStatsManager>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

            if (sprayCloudVisual != null)
                sprayCloudVisual.SetActive(false);
        }

        private void OnEnable()
        {
            if (inventory != null)
            {
                inventory.OnItemUseStarted += HandleUseStarted;
                inventory.OnItemUseHeld += HandleUseHeld;
                inventory.OnItemUseReleased += HandleUseReleased;
            }
        }

        private void OnDisable()
        {
            SetSprayVisual(false);
            if (inventory != null)
            {
                inventory.OnItemUseStarted -= HandleUseStarted;
                inventory.OnItemUseHeld -= HandleUseHeld;
                inventory.OnItemUseReleased -= HandleUseReleased;
            }
        }

        private void HandleUseStarted(WorldItem item)
        {
            if (item == null || item.Definition == null) return;
            string id = item.Definition.itemId;

            if (id == "slipper")
            {
                PerformMeleeStrike(item, balanceConfig.slipperMeleeRange, balanceConfig.slipperMeleeDamage, "Шльоп! Таргана прибито капцем!");
            }
            else if (id == "pan_cold" || id == "pan_hot")
            {
                PerformMeleeStrike(item, balanceConfig.fryingPanMeleeRange, balanceConfig.fryingPanMeleeDamage, "БАМ! Удар сковорідкою!");
            }
            else if (id == "dichlorvos")
            {
                SetSprayVisual(true);
            }
        }

        private void HandleUseHeld(WorldItem item, float deltaTime)
        {
            if (item == null || item.Definition == null) return;
            string id = item.Definition.itemId;

            if (id == "dichlorvos")
            {
                SetSprayVisual(true);
                PerformDichlorvosSpray(deltaTime);
            }
            else if (id == "mop")
            {
                PerformMopClean(deltaTime);
            }
        }

        private void HandleUseReleased(WorldItem item)
        {
            SetSprayVisual(false);
        }

        public int PerformMeleeStrike(WorldItem weapon, float range, float baseDamage, string killToast)
        {
            if (cameraTransform == null) return 0;

            float mult = statsManager != null ? statsManager.StrengthSpeedMultiplier : 1f;
            float dmg = baseDamage * mult;

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            RaycastHit[] hits = Physics.SphereCastAll(ray, 0.35f, range, ~0, QueryTriggerInteraction.Collide);
            int kills = 0;

            Collider primaryHit = null;
            Vector3 hitPoint = ray.origin + ray.direction * range;

            if (hits != null && hits.Length > 0)
            {
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider c = hits[i].collider;
                    if (c == null || c.transform.IsChildOf(transform)) continue;

                    if (primaryHit == null)
                    {
                        primaryHit = c;
                        hitPoint = hits[i].point;
                    }

                    var bug = c.GetComponentInParent<CockroachAI>();
                    if (bug != null && bug.IsAlive)
                    {
                        if (bug.TakeDamage(dmg, false))
                            kills++;
                    }
                }
            }

            OnPlayerMeleeStrike?.Invoke(weapon, hitPoint, primaryHit);
            if (kills > 0)
                HudController.Instance?.ShowToast(killToast);
            return kills;
        }

        public int PerformDichlorvosSpray(float deltaTime)
        {
            if (cameraTransform == null) return 0;

            Vector3 origin = cameraTransform.position;
            Vector3 forward = cameraTransform.forward;
            float range = balanceConfig.dichlorvosRange;
            float dps = balanceConfig.dichlorvosDps;
            int kills = 0;

            // Cone check against all living cockroaches (reaches under cabinets too!)
            var snapshot = CockroachAI.LivingCockroaches.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                var bug = snapshot[i];
                if (bug == null || !bug.IsAlive) continue;

                Vector3 toBug = bug.transform.position - origin;
                float dist = toBug.magnitude;
                if (dist > range || dist < 0.05f) continue;

                float angle = Vector3.Angle(forward, toBug / dist);
                if (angle <= 32f)
                {
                    if (bug.TakeDamage(dps * deltaTime, isSpray: true))
                        kills++;
                }
            }

            if (kills > 0)
                HudController.Instance?.ShowToast("Дихлофос подіяв! Таргана викурено й знищено!");
            return kills;
        }

        private void PerformMopClean(float deltaTime)
        {
            if (cameraTransform == null) return;
            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            RaycastHit[] hits = Physics.SphereCastAll(ray, 0.45f, 2.8f, ~0, QueryTriggerInteraction.Collide);
            if (hits == null) return;

            for (int i = 0; i < hits.Length; i++)
            {
                var mess = hits[i].collider != null ? hits[i].collider.GetComponentInParent<MessSpot>() : null;
                if (mess != null && !mess.IsCleaned)
                {
                    mess.ApplyMopCleaning(deltaTime);
                    return;
                }
            }
        }

        private void SetSprayVisual(bool active)
        {
            IsSpraying = active;
            if (sprayCloudVisual != null && sprayCloudVisual.activeSelf != active)
                sprayCloudVisual.SetActive(active);
        }
    }
}

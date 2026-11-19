using System.Collections.Generic;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Interaction;
using Dormitory7.UI;
using UnityEngine;

namespace Dormitory7.Features.Cockroaches
{
    public enum CockroachState
    {
        Patrolling = 0,
        Fleeing = 1,
        Dead = 2
    }

    public class CockroachAI : MonoBehaviour, IInteractable
    {
        public static readonly List<CockroachAI> LivingCockroaches = new List<CockroachAI>();

        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private ItemDefinition deadCockroachItem;
        [SerializeField] private Transform bodyVisual;
        [SerializeField] private Transform antennaLeft;
        [SerializeField] private Transform antennaRight;
        [SerializeField] private GameObject greenSplatVisual;

        public CockroachState State { get; private set; } = CockroachState.Patrolling;
        public bool IsAlive => State != CockroachState.Dead;

        private DormStatsManager _stats;
        private Transform _playerTransform;
        private float _health = 1f;
        private float _floorY;
        private Vector3 _moveDir;
        private float _wanderTimer;
        private float _fleeTimer;
        private float _lightCheckTimer;
        private Light[] _cachedLights;

        public void Configure(
            BalanceConfig config,
            ItemDefinition corpseItem,
            Transform body,
            Transform antL,
            Transform antR,
            GameObject splat)
        {
            balanceConfig = config;
            deadCockroachItem = corpseItem;
            bodyVisual = body;
            antennaLeft = antL;
            antennaRight = antR;
            greenSplatVisual = splat;
        }

        private void Awake()
        {
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();
            _health = Mathf.Max(0.2f, balanceConfig.cockroachHealth);
            _floorY = transform.position.y;
            _moveDir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;

            if (greenSplatVisual != null)
                greenSplatVisual.SetActive(false);
        }

        private void OnEnable()
        {
            if (IsAlive && !LivingCockroaches.Contains(this))
                LivingCockroaches.Add(this);
        }

        private void OnDisable()
        {
            LivingCockroaches.Remove(this);
        }

        private void Start()
        {
            _stats = FindAnyObjectByType<DormStatsManager>();
            var playerGo = GameObject.FindWithTag("Player");
            if (playerGo != null)
                _playerTransform = playerGo.transform;

            _cachedLights = FindObjectsByType<Light>();
        }

        private void Update()
        {
            if (State == CockroachState.Dead) return;
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            // Global Trash contribution: +0.5% per minute per living cockroach
            if (_stats != null && balanceConfig != null)
            {
                float trashPerSec = balanceConfig.cockroachTrashPerMinute / 60f;
                _stats.ModifyTrash(trashPerSec * Time.deltaTime);
            }

            UpdateAntennaeTremble();
            EvaluateThreats();

            if (State == CockroachState.Patrolling)
                UpdatePatrol();
            else if (State == CockroachState.Fleeing)
                UpdateFlee();
        }

        private void EvaluateThreats()
        {
            if (_playerTransform != null)
            {
                Vector3 delta = transform.position - _playerTransform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude < balanceConfig.cockroachFleePlayerDistance * balanceConfig.cockroachFleePlayerDistance)
                {
                    StartFleeFrom(_playerTransform.position);
                    return;
                }
            }

            _lightCheckTimer -= Time.deltaTime;
            if (_lightCheckTimer <= 0f)
            {
                _lightCheckTimer = 0.45f;
                if (_cachedLights != null)
                {
                    float maxDistSqr = balanceConfig.cockroachLightFleeDistance * balanceConfig.cockroachLightFleeDistance;
                    for (int i = 0; i < _cachedLights.Length; i++)
                    {
                        Light l = _cachedLights[i];
                        if (l == null || !l.enabled || !l.gameObject.activeInHierarchy || l.intensity < 1f) continue;
                        Vector3 d = transform.position - l.transform.position;
                        if (Mathf.Abs(d.y) < 3.1f && (d.x * d.x + d.z * d.z) < maxDistSqr)
                        {
                            StartFleeFrom(l.transform.position);
                            break;
                        }
                    }
                }
            }
        }

        public void StartFleeFrom(Vector3 threatPosition)
        {
            if (State == CockroachState.Dead) return;
            State = CockroachState.Fleeing;
            _fleeTimer = Random.Range(2.4f, 3.6f);

            Vector3 away = transform.position - threatPosition;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
                away = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            else
                away = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * away.normalized;

            _moveDir = away;
        }

        public void FlushFromHiding(Vector3 origin)
        {
            StartFleeFrom(origin);
        }

        private void UpdatePatrol()
        {
            _wanderTimer -= Time.deltaTime;
            if (_wanderTimer <= 0f)
            {
                _wanderTimer = Random.Range(1.0f, 2.6f);
                _moveDir = Quaternion.Euler(0f, Random.Range(-75f, 75f), 0f) * _moveDir;
                _moveDir.y = 0f;
                _moveDir.Normalize();
            }

            MoveWithWallAvoidance(balanceConfig.cockroachPatrolSpeed);
        }

        private void UpdateFlee()
        {
            _fleeTimer -= Time.deltaTime;
            MoveWithWallAvoidance(balanceConfig.cockroachFleeSpeed);

            if (_fleeTimer <= 0f)
                State = CockroachState.Patrolling;
        }

        private void MoveWithWallAvoidance(float speed)
        {
            Vector3 origin = transform.position + Vector3.up * 0.08f;
            float checkDist = 0.42f;
            if (Physics.Raycast(origin, _moveDir, out RaycastHit hit, checkDist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && !hit.collider.transform.IsChildOf(transform))
                {
                    Vector3 reflected = Vector3.Reflect(_moveDir, hit.normal);
                    reflected.y = 0f;
                    _moveDir = reflected.sqrMagnitude > 0.01f
                        ? (Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f) * reflected.normalized)
                        : -_moveDir;
                }
            }

            Vector3 nextPos = transform.position + _moveDir * (speed * Time.deltaTime);
            nextPos.y = _floorY;
            transform.position = nextPos;

            if (_moveDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(_moveDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 14f);
            }
        }

        private void UpdateAntennaeTremble()
        {
            float wiggle = Mathf.Sin(Time.time * (State == CockroachState.Fleeing ? 42f : 18f)) * 18f;
            if (antennaLeft != null)
                antennaLeft.localRotation = Quaternion.Euler(15f, -24f + wiggle, 0f);
            if (antennaRight != null)
                antennaRight.localRotation = Quaternion.Euler(15f, 24f - wiggle, 0f);
        }

        public bool TakeDamage(float damage, bool isSpray = false)
        {
            if (State == CockroachState.Dead) return false;

            _health -= Mathf.Max(0.05f, damage);
            if (_health <= 0f)
            {
                Die();
                return true;
            }

            if (_playerTransform != null)
                StartFleeFrom(_playerTransform.position);
            return false;
        }

        private void Die()
        {
            State = CockroachState.Dead;
            LivingCockroaches.Remove(this);

            if (bodyVisual != null)
            {
                bodyVisual.localRotation = Quaternion.Euler(0f, 0f, 180f);
                bodyVisual.localPosition = new Vector3(0f, 0.04f, 0f);
            }
            if (greenSplatVisual != null)
                greenSplatVisual.SetActive(true);
        }

        public string GetPrompt(PlayerContext context)
        {
            if (State != CockroachState.Dead)
                return "Тарган! (Вдарте капцем або розпиліть дихлофос [ЛКМ])";

            if (context.IsCarryingTwoHanded)
                return "Звільніть руки ([G]), щоб підібрати тушку таргана";

            if (context.Inventory != null && !context.Inventory.HasFreeSmallSlot())
                return "Інвентар повний (Тушка таргана)";

            return "[E] Підібрати тушку таргана";
        }

        public bool CanInteract(PlayerContext context)
        {
            if (State != CockroachState.Dead) return false;
            if (context.Inventory == null || context.IsCarryingTwoHanded) return false;
            return context.Inventory.HasFreeSmallSlot();
        }

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;
            if (deadCockroachItem != null && context.Inventory.TrySpawnAndAddItem(deadCockroachItem, new Vector3(0.14f, 0.06f, 0.20f)))
            {
                HudController.Instance?.ShowToast("Підібрано: Тушка таргана. Викиньте у смітник!");
                Destroy(gameObject);
            }
        }
    }
}

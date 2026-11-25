using System;
using System.Collections.Generic;
using Dormitory7.Audio;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Interaction;
using Dormitory7.UI;
using UnityEngine;
using Random = UnityEngine.Random;

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
        public static event Action<CockroachAI> OnAnyCockroachKilled;

        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private ItemDefinition deadCockroachItem;
        [SerializeField] private Transform bodyVisual;
        [SerializeField] private Transform antennaLeft;
        [SerializeField] private Transform antennaRight;
        [SerializeField] private GameObject greenSplatVisual;
        [SerializeField] private ParticleSystem splatParticles;
        [SerializeField] private AudioSource scurryAudioSource;
        [SerializeField] private Animator roachAnimator;

        private static readonly int AnimSpeedHash = Animator.StringToHash("Speed");
        private static readonly int AnimIsDeadHash = Animator.StringToHash("IsDead");

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
        private bool[] _lightWasOn;

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
            if (scurryAudioSource == null)
                scurryAudioSource = GetComponent<AudioSource>();
            if (splatParticles == null)
                splatParticles = GetComponentInChildren<ParticleSystem>(true);
            if (roachAnimator == null)
                roachAnimator = GetComponentInChildren<Animator>(true);
            if (roachAnimator != null)
            {
                roachAnimator.applyRootMotion = false;
                if (roachAnimator.runtimeAnimatorController != null)
                {
                    roachAnimator.SetBool(AnimIsDeadHash, false);
                    roachAnimator.SetFloat(AnimSpeedHash, 1f);
                }
            }
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
            if (_cachedLights != null)
            {
                _lightWasOn = new bool[_cachedLights.Length];
                for (int i = 0; i < _cachedLights.Length; i++)
                    _lightWasOn[i] = _cachedLights[i] != null && _cachedLights[i].enabled && _cachedLights[i].gameObject.activeInHierarchy;
            }

            if (scurryAudioSource == null)
            {
                scurryAudioSource = gameObject.AddComponent<AudioSource>();
                scurryAudioSource.spatialBlend = 1f;
                scurryAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                scurryAudioSource.minDistance = 0.5f;
                scurryAudioSource.maxDistance = 6.5f;
                scurryAudioSource.loop = true;
                scurryAudioSource.playOnAwake = false;
                scurryAudioSource.volume = 0.30f;
            }
            if (scurryAudioSource.clip == null && AudioManager.Instance != null)
                scurryAudioSource.clip = AudioManager.Instance.LoopRoachScurry;
            if (scurryAudioSource.clip != null && IsAlive && !scurryAudioSource.isPlaying)
                scurryAudioSource.Play();
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

            if (scurryAudioSource != null && scurryAudioSource.isPlaying)
                scurryAudioSource.pitch = State == CockroachState.Fleeing ? 1.45f : 0.95f;

            if (roachAnimator != null && roachAnimator.runtimeAnimatorController != null)
            {
                bool fleeing = State == CockroachState.Fleeing;
                roachAnimator.SetFloat(AnimSpeedHash, 1f);
                roachAnimator.speed = fleeing ? 1.6f : 1.0f;
            }
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
                _lightCheckTimer = 0.25f;
                if (_cachedLights != null && _lightWasOn != null)
                {
                    float maxDistSqr = balanceConfig.cockroachLightFleeDistance * balanceConfig.cockroachLightFleeDistance;
                    for (int i = 0; i < _cachedLights.Length; i++)
                    {
                        Light l = _cachedLights[i];
                        bool isOnNow = l != null && l.enabled && l.gameObject.activeInHierarchy && l.intensity >= 1f;
                        bool justTurnedOn = isOnNow && !_lightWasOn[i];
                        _lightWasOn[i] = isOnNow;

                        if (justTurnedOn)
                        {
                            Vector3 d = transform.position - l.transform.position;
                            if (Mathf.Abs(d.y) < 3.2f && (d.x * d.x + d.z * d.z) < maxDistSqr * 1.6f)
                            {
                                StartFleeFrom(l.transform.position);
                                break;
                            }
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

            if (roachAnimator != null && roachAnimator.runtimeAnimatorController != null)
            {
                roachAnimator.speed = 1f;
                roachAnimator.SetBool(AnimIsDeadHash, true);
            }
            else if (bodyVisual != null)
            {
                bodyVisual.localRotation = Quaternion.Euler(0f, 0f, 180f);
                bodyVisual.localPosition = new Vector3(0f, 0.04f, 0f);
            }
            if (greenSplatVisual != null)
                greenSplatVisual.SetActive(true);
            if (scurryAudioSource != null && scurryAudioSource.isPlaying)
                scurryAudioSource.Stop();
            if (splatParticles != null)
            {
                splatParticles.gameObject.SetActive(true);
                splatParticles.Play();
            }
            OnAnyCockroachKilled?.Invoke(this);
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

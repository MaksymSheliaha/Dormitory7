using System;
using System.Collections.Generic;
using Dormitory7.Core;
using Dormitory7.Interaction;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Dormitory7.NPC
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class BaseNpcAgent : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] protected DayTimeController dayTimeController;
        [SerializeField] protected NavMeshAgent navAgent;
        [SerializeField] protected Animator characterAnimator;

        [Header("Movement Settings")]
        [SerializeField] protected float walkSpeed = 2.6f;
        [SerializeField] protected float runSpeed = 4.2f;
        [SerializeField] protected float arrivalThreshold = 0.45f;
        [SerializeField] protected bool autoOpenUnlockedDoors = true;
        [SerializeField] protected float doorOpenRadius = 1.85f;
        [SerializeField] protected float doorAutoCloseDelay = 3.5f;

        private readonly List<NpcWaypoint> _patrolRoute = new List<NpcWaypoint>();
        private int _patrolIndex = -1;
        private bool _patrolLoop;
        private float _patrolWaitTimer;
        private bool _isWaitingAtWaypoint;
        private float _doorCheckTimer;
        private bool _wasStoppedBeforePause;
        private DoorInteractable[] _cachedDoors;
        private static readonly int AnimSpeedHash = Animator.StringToHash("Speed");

        public NavMeshAgent Agent => navAgent;
        public NpcWaypoint CurrentTargetWaypoint { get; private set; }
        public bool IsPatrolling => _patrolRoute.Count > 0 && _patrolIndex >= 0;

        public bool HasArrived =>
            navAgent != null &&
            navAgent.isOnNavMesh &&
            !navAgent.pathPending &&
            navAgent.remainingDistance <= Mathf.Max(arrivalThreshold, navAgent.stoppingDistance);

        public event Action<NpcWaypoint> OnWaypointReached;

        protected virtual void Awake()
        {
            if (navAgent == null)
                navAgent = GetComponent<NavMeshAgent>();

            if (characterAnimator == null)
                characterAnimator = GetComponentInChildren<Animator>();

            if (characterAnimator != null)
                characterAnimator.applyRootMotion = false;

            if (navAgent != null)
            {
                navAgent.speed = walkSpeed;
                navAgent.angularSpeed = 240f;
                navAgent.acceleration = 10f;
                navAgent.stoppingDistance = 0.25f;
                navAgent.radius = 0.35f;
                navAgent.height = 1.85f;
                navAgent.autoRepath = true;
            }
        }

        protected virtual void Start()
        {
            if (dayTimeController == null)
                dayTimeController = FindAnyObjectByType<DayTimeController>();

            _cachedDoors = FindObjectsByType<DoorInteractable>();
            SubscribeDayTime(true);

            EnsureNavMeshBound();
        }

        protected void EnsureNavMeshBound()
        {
            if (navAgent == null) return;

            // Ensure NavMeshSurface has registered its baked NavMeshData (critical in cold Standalone builds!)
            if (!NavMesh.SamplePosition(transform.position, out _, 50f, NavMesh.AllAreas))
            {
                var surface = FindAnyObjectByType<NavMeshSurface>();
                if (surface != null && surface.navMeshData != null)
                {
                    surface.AddData();
                }
            }

            if (!navAgent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
                {
                    // Cycle enabled so Unity recreates the internal native NavMeshAgent once NavMesh data is live
                    navAgent.enabled = false;
                    transform.position = hit.position;
                    navAgent.enabled = true;
                    if (navAgent.isOnNavMesh)
                        navAgent.Warp(hit.position);
                }
            }
        }

        protected virtual void OnDestroy()
        {
            SubscribeDayTime(false);
        }

        private void SubscribeDayTime(bool subscribe)
        {
            if (dayTimeController == null) return;
            if (subscribe)
            {
                dayTimeController.OnTimeChanged += HandleTimeChanged;
                dayTimeController.OnCurfewWarning += OnCurfewWarningTriggered;
                dayTimeController.OnCurfewStarted += OnCurfewStartedTriggered;
                dayTimeController.OnDayAdvanced += HandleDayAdvanced;
            }
            else
            {
                dayTimeController.OnTimeChanged -= HandleTimeChanged;
                dayTimeController.OnCurfewWarning -= OnCurfewWarningTriggered;
                dayTimeController.OnCurfewStarted -= OnCurfewStartedTriggered;
                dayTimeController.OnDayAdvanced -= HandleDayAdvanced;
            }
        }

        protected virtual void Update()
        {
            if (navAgent == null || !navAgent.isOnNavMesh) return;

            bool isPaused = GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing;
            if (isPaused)
            {
                if (!navAgent.isStopped)
                {
                    _wasStoppedBeforePause = false;
                    navAgent.isStopped = true;
                }
                UpdateAnimatorSpeed(0f);
                return;
            }
            else if (!_wasStoppedBeforePause && navAgent.isStopped && (IsPatrolling || navAgent.hasPath))
            {
                navAgent.isStopped = false;
            }

            if (autoOpenUnlockedDoors)
                UpdateDoorProximity();

            if (IsPatrolling)
                UpdatePatrol();

            UpdateAnimatorSpeed(navAgent.isStopped ? 0f : navAgent.velocity.magnitude);
        }

        private void UpdateAnimatorSpeed(float speed)
        {
            if (characterAnimator == null || !characterAnimator.isActiveAndEnabled || characterAnimator.runtimeAnimatorController == null)
                return;

            characterAnimator.SetFloat(AnimSpeedHash, speed, 0.1f, Time.deltaTime);
        }

        private void UpdateDoorProximity()
        {
            _doorCheckTimer -= Time.deltaTime;
            if (_doorCheckTimer > 0f) return;
            _doorCheckTimer = 0.2f;

            if (_cachedDoors == null || _cachedDoors.Length == 0) return;
            float rSqr = doorOpenRadius * doorOpenRadius;
            Vector3 myPos = transform.position;

            for (int i = 0; i < _cachedDoors.Length; i++)
            {
                DoorInteractable door = _cachedDoors[i];
                if (door == null || door.IsLocked) continue;

                Vector3 delta = door.transform.position - myPos;
                if (Mathf.Abs(delta.y) > 2.2f) continue;

                delta.y = 0f;
                if (delta.sqrMagnitude <= rSqr)
                    door.OpenForNpc(doorAutoCloseDelay);
            }
        }

        private void UpdatePatrol()
        {
            if (_isWaitingAtWaypoint)
            {
                _patrolWaitTimer -= Time.deltaTime;
                if (_patrolWaitTimer <= 0f)
                {
                    _isWaitingAtWaypoint = false;
                    AdvancePatrolIndex();
                }
                return;
            }

            if (HasArrived)
            {
                NpcWaypoint reached = CurrentTargetWaypoint;
                OnWaypointReached?.Invoke(reached);
                _isWaitingAtWaypoint = true;
                _patrolWaitTimer = reached != null ? Mathf.Max(0f, reached.DefaultWaitSeconds) : 1.0f;
            }
        }

        private void AdvancePatrolIndex()
        {
            if (_patrolRoute.Count == 0) return;

            int next = _patrolIndex + 1;
            if (next >= _patrolRoute.Count)
            {
                if (!_patrolLoop)
                {
                    _patrolIndex = -1;
                    return;
                }
                next = 0;
            }

            _patrolIndex = next;
            MoveToWaypoint(_patrolRoute[_patrolIndex]);
        }

        public bool MoveToPosition(Vector3 targetWorldPos, float? speedOverride = null)
        {
            if (navAgent == null || !navAgent.isOnNavMesh) return false;

            if (speedOverride.HasValue)
                navAgent.speed = speedOverride.Value;

            Vector3 dest = targetWorldPos;
            if (NavMesh.SamplePosition(targetWorldPos, out NavMeshHit hit, 2.0f, navAgent.areaMask))
                dest = hit.position;

            _wasStoppedBeforePause = false;
            navAgent.isStopped = false;
            return navAgent.SetDestination(dest);
        }

        public bool MoveToWaypoint(NpcWaypoint waypoint, float? speedOverride = null)
        {
            if (waypoint == null) return false;
            CurrentTargetWaypoint = waypoint;
            return MoveToPosition(waypoint.Position, speedOverride);
        }

        public bool MoveToZone(NpcZone zone, float? speedOverride = null)
        {
            NpcWaypoint wp = NpcWaypoint.GetFirstInZone(zone);
            return wp != null && MoveToWaypoint(wp, speedOverride);
        }

        public void SetPatrolRoute(IList<NpcWaypoint> route, bool loop = true, float? speedOverride = null)
        {
            _patrolRoute.Clear();
            _isWaitingAtWaypoint = false;
            _patrolLoop = loop;

            if (route == null || route.Count == 0)
            {
                _patrolIndex = -1;
                return;
            }

            for (int i = 0; i < route.Count; i++)
            {
                if (route[i] != null)
                    _patrolRoute.Add(route[i]);
            }

            if (_patrolRoute.Count == 0)
            {
                _patrolIndex = -1;
                return;
            }

            if (navAgent != null)
                navAgent.autoBraking = !loop;

            _patrolIndex = 0;
            MoveToWaypoint(_patrolRoute[0], speedOverride ?? walkSpeed);
        }

        public void StopMovement()
        {
            _patrolRoute.Clear();
            _patrolIndex = -1;
            _isWaitingAtWaypoint = false;
            _wasStoppedBeforePause = true;
            if (navAgent != null && navAgent.isOnNavMesh)
            {
                navAgent.isStopped = true;
                navAgent.ResetPath();
            }
        }

        public bool WarpToPosition(Vector3 worldPos)
        {
            if (navAgent == null) return false;
            Vector3 target = worldPos;
            if (NavMesh.SamplePosition(worldPos, out NavMeshHit hit, 2.5f, navAgent.areaMask))
                target = hit.position;
            return navAgent.Warp(target);
        }

        public bool WarpToZone(NpcZone zone)
        {
            NpcWaypoint wp = NpcWaypoint.GetFirstInZone(zone);
            return wp != null && WarpToPosition(wp.Position);
        }

        public bool CanReachPosition(Vector3 targetWorldPos, out NavMeshPath path)
        {
            path = new NavMeshPath();
            if (navAgent == null || !navAgent.isOnNavMesh) return false;

            Vector3 dest = targetWorldPos;
            if (NavMesh.SamplePosition(targetWorldPos, out NavMeshHit hit, 2.0f, navAgent.areaMask))
                dest = hit.position;

            return navAgent.CalculatePath(dest, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        private void HandleTimeChanged(int day, float hour, string formatted) => OnGameTimeUpdated(day, hour);
        private void HandleDayAdvanced(int newDay, string message) => OnNewDayStarted(newDay);

        protected virtual void OnGameTimeUpdated(int day, float hour) { }
        protected virtual void OnCurfewWarningTriggered() { }
        protected virtual void OnCurfewStartedTriggered() { }
        protected virtual void OnNewDayStarted(int newDay) { }
    }
}

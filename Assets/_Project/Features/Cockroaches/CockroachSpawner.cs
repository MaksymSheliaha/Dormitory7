using System.Collections.Generic;
using Dormitory7.Core;
using Dormitory7.Interaction;
using Dormitory7.UI;
using UnityEngine;

namespace Dormitory7.Features.Cockroaches
{
    public class CockroachSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject cockroachPrefab;
        [SerializeField] private int initialSpawnCount = 1;
        [SerializeField] private int maxAliveFromSpawner = 2;
        [SerializeField] private float spawnIntervalSeconds = 45f;
        [SerializeField] private float spawnRadius = 0.55f;
        [SerializeField] private CabinetInteractable linkedCabinet;

        private readonly List<CockroachAI> _spawned = new List<CockroachAI>();
        private float _timer;
        private bool _cabinetFlushedOnce;

        public void Configure(GameObject prefab, int initialCount, int maxAlive, float intervalSec, CabinetInteractable cabinet = null)
        {
            cockroachPrefab = prefab;
            initialSpawnCount = initialCount;
            maxAliveFromSpawner = maxAlive;
            spawnIntervalSeconds = intervalSec;
            linkedCabinet = cabinet;
        }

        private void OnEnable()
        {
            if (linkedCabinet != null)
                linkedCabinet.OnCabinetToggled += HandleCabinetToggled;
        }

        private void OnDisable()
        {
            if (linkedCabinet != null)
                linkedCabinet.OnCabinetToggled -= HandleCabinetToggled;
        }

        private void Start()
        {
            _timer = spawnIntervalSeconds;
            for (int i = 0; i < initialSpawnCount; i++)
                SpawnOne(fleeOnSpawn: false);
        }

        private void Update()
        {
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            CleanupList();
            if (_spawned.Count >= maxAliveFromSpawner) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = spawnIntervalSeconds;
                SpawnOne(fleeOnSpawn: false);
            }
        }

        private void HandleCabinetToggled(CabinetInteractable cabinet, bool isOpen)
        {
            if (!isOpen) return;

            // Flush any nearby cockroaches AND pop out a hiding cockroach the first time cabinet opens!
            bool flushedAny = false;
            for (int i = 0; i < CockroachAI.LivingCockroaches.Count; i++)
            {
                var bug = CockroachAI.LivingCockroaches[i];
                if (bug != null && Vector3.Distance(bug.transform.position, cabinet.transform.position) < 2.2f)
                {
                    bug.FlushFromHiding(cabinet.transform.position);
                    flushedAny = true;
                }
            }

            if (!_cabinetFlushedOnce)
            {
                _cabinetFlushedOnce = true;
                var spawned = SpawnOne(fleeOnSpawn: true);
                if (spawned != null) flushedAny = true;
            }

            if (flushedAny)
                HudController.Instance?.ShowToast("З-за тумбочки вискочив тарган! Хапайте капець або дихлофос!");
        }

        public CockroachAI SpawnOne(bool fleeOnSpawn)
        {
            if (cockroachPrefab == null) return null;
            CleanupList();
            if (_spawned.Count >= maxAliveFromSpawner) return null;

            Vector2 offset2D = Random.insideUnitCircle * spawnRadius;
            Vector3 spawnPos = new Vector3(transform.position.x + offset2D.x, transform.position.y, transform.position.z + offset2D.y);
            GameObject go = Instantiate(cockroachPrefab, spawnPos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            var ai = go.GetComponent<CockroachAI>();
            if (ai != null)
            {
                _spawned.Add(ai);
                if (fleeOnSpawn)
                    ai.FlushFromHiding(transform.position);
            }
            return ai;
        }

        private void CleanupList()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] == null || !_spawned[i].IsAlive)
                    _spawned.RemoveAt(i);
            }
        }
    }
}

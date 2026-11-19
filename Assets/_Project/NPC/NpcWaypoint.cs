using System.Collections.Generic;
using UnityEngine;

namespace Dormitory7.NPC
{
    public enum NpcZone
    {
        CorridorCenter = 0,
        CorridorLeft = 1,
        CorridorRight = 2,
        KitchenLeft = 3,
        KitchenRight = 4,
        WashroomLeft = 5,
        WashroomRight = 6,
        ToiletLeftMen = 7,
        ToiletRightWomen = 8,
        Room104 = 9,
        Room106 = 10,
        Room103 = 11,
        ElectricalPanel = 12,
        StairwellTop = 13,
        BasementHall = 14,
        BasementShower = 15,
        BasementGym = 16
    }

    public class NpcWaypoint : MonoBehaviour
    {
        public static readonly List<NpcWaypoint> AllWaypoints = new List<NpcWaypoint>();

        [SerializeField] private string waypointId = "wp_default";
        [SerializeField] private NpcZone zone = NpcZone.CorridorCenter;
        [SerializeField] private int orderIndex = 0;
        [SerializeField] private float defaultWaitSeconds = 2.0f;
        [SerializeField] private bool isRestrictedSanitaryZone = false;

        public string WaypointId => waypointId;
        public NpcZone Zone => zone;
        public int OrderIndex => orderIndex;
        public float DefaultWaitSeconds => defaultWaitSeconds;
        public bool IsRestrictedSanitaryZone => isRestrictedSanitaryZone;
        public Vector3 Position => transform.position;

        public void Configure(string id, NpcZone targetZone, int order = 0, float waitSeconds = 2.0f, bool restrictedSanitary = false)
        {
            waypointId = id;
            zone = targetZone;
            orderIndex = order;
            defaultWaitSeconds = waitSeconds;
            isRestrictedSanitaryZone = restrictedSanitary;
        }

        private void OnEnable()
        {
            if (!AllWaypoints.Contains(this))
                AllWaypoints.Add(this);
        }

        private void OnDisable()
        {
            AllWaypoints.Remove(this);
        }

        public static NpcWaypoint FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < AllWaypoints.Count; i++)
            {
                if (AllWaypoints[i] != null && AllWaypoints[i].waypointId == id)
                    return AllWaypoints[i];
            }
            return null;
        }

        public static NpcWaypoint GetFirstInZone(NpcZone targetZone)
        {
            NpcWaypoint best = null;
            for (int i = 0; i < AllWaypoints.Count; i++)
            {
                NpcWaypoint wp = AllWaypoints[i];
                if (wp == null || wp.zone != targetZone) continue;
                if (best == null || wp.orderIndex < best.orderIndex)
                    best = wp;
            }
            return best;
        }

        public static List<NpcWaypoint> FindAllInZone(NpcZone targetZone)
        {
            var list = new List<NpcWaypoint>();
            for (int i = 0; i < AllWaypoints.Count; i++)
            {
                if (AllWaypoints[i] != null && AllWaypoints[i].zone == targetZone)
                    list.Add(AllWaypoints[i]);
            }
            list.Sort((a, b) => a.orderIndex.CompareTo(b.orderIndex));
            return list;
        }

        public static List<NpcWaypoint> BuildRouteFromZones(params NpcZone[] zones)
        {
            var route = new List<NpcWaypoint>();
            if (zones == null) return route;
            for (int i = 0; i < zones.Length; i++)
            {
                var wp = GetFirstInZone(zones[i]);
                if (wp != null)
                    route.Add(wp);
            }
            return route;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = isRestrictedSanitaryZone
                ? new Color(0.25f, 0.72f, 0.95f, 0.85f)
                : new Color(0.95f, 0.78f, 0.22f, 0.85f);
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.25f, 0.22f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.25f, transform.forward * 0.65f);
        }
    }
}

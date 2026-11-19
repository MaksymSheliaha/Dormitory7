using System.Collections.Generic;
using UnityEngine;

namespace Dormitory7.Features.Repairs
{
    public class HazardDirector : MonoBehaviour
    {
        [SerializeField] private List<RepairableHazard> hazards = new List<RepairableHazard>();

        public IReadOnlyList<RepairableHazard> Hazards => hazards;

        private void Awake()
        {
            if (hazards == null || hazards.Count == 0)
                hazards = new List<RepairableHazard>(FindObjectsByType<RepairableHazard>());
        }

        public int CountBrokenHazards()
        {
            int count = 0;
            for (int i = 0; i < hazards.Count; i++)
            {
                if (hazards[i] != null && hazards[i].IsBroken)
                    count++;
            }
            return count;
        }

        public RepairableHazard FindByType(HazardType type)
        {
            for (int i = 0; i < hazards.Count; i++)
            {
                if (hazards[i] != null && hazards[i].Type == type)
                    return hazards[i];
            }
            return null;
        }
    }
}

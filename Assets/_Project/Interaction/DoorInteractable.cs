using UnityEngine;

namespace Dormitory7.Interaction
{
    public class DoorInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private Transform doorPivot;
        [SerializeField] private string doorLabel = "двері";
        [SerializeField] private float closedYAngle = 0f;
        [SerializeField] private float openYAngle = 95f;
        [SerializeField] private float swingSpeed = 6f;
        [SerializeField] private bool isLocked = false;
        [SerializeField] private string lockedPrompt = "Зачинено (кімната сусідів)";

        private bool _isOpen;
        private bool _openedByNpc;
        private float _npcAutoCloseTimer;
        private Quaternion _closedRot;
        private Quaternion _openRot;

        public bool IsOpen => _isOpen;
        public bool IsLocked => isLocked;

        public void Configure(Transform pivot, string label, float openAngle, bool locked = false, string lockedMsg = "")
        {
            doorPivot = pivot;
            doorLabel = label;
            openYAngle = openAngle;
            isLocked = locked;
            if (!string.IsNullOrEmpty(lockedMsg))
                lockedPrompt = lockedMsg;
        }

        private void Awake()
        {
            if (doorPivot == null)
                doorPivot = transform;

            Vector3 baseEuler = doorPivot.localEulerAngles;
            _closedRot = Quaternion.Euler(baseEuler.x, closedYAngle, baseEuler.z);
            _openRot = Quaternion.Euler(baseEuler.x, closedYAngle + openYAngle, baseEuler.z);
        }

        private void Update()
        {
            if (_openedByNpc && _isOpen && _npcAutoCloseTimer > 0f)
            {
                _npcAutoCloseTimer -= Time.deltaTime;
                if (_npcAutoCloseTimer <= 0f)
                {
                    _isOpen = false;
                    _openedByNpc = false;
                }
            }

            if (doorPivot == null) return;
            Quaternion target = _isOpen ? _openRot : _closedRot;
            if (Quaternion.Angle(doorPivot.localRotation, target) > 0.1f)
                doorPivot.localRotation = Quaternion.Slerp(doorPivot.localRotation, target, Time.deltaTime * swingSpeed);
        }

        public void OpenForNpc(float autoCloseDelaySeconds = 3.5f)
        {
            if (isLocked) return;
            if (!_isOpen)
            {
                _isOpen = true;
                _openedByNpc = true;
            }
            if (_openedByNpc)
                _npcAutoCloseTimer = Mathf.Max(_npcAutoCloseTimer, autoCloseDelaySeconds);
        }

        public void SetOpen(bool open)
        {
            if (isLocked) return;
            _isOpen = open;
            _openedByNpc = false;
            _npcAutoCloseTimer = 0f;
        }

        public string GetPrompt(PlayerContext context)
        {
            if (isLocked) return lockedPrompt;
            return _isOpen ? $"[E] Зачинити {doorLabel}" : $"[E] Відчинити {doorLabel}";
        }

        public bool CanInteract(PlayerContext context) => !isLocked;

        public void Interact(PlayerContext context)
        {
            if (isLocked) return;
            _isOpen = !_isOpen;
            _openedByNpc = false;
            _npcAutoCloseTimer = 0f;
        }
    }
}

using UnityEngine;

namespace Dormitory7.Interaction
{
    public class CabinetInteractable : MonoBehaviour, IInteractable
    {
        public enum MotionMode
        {
            SlideDrawer = 0,
            SwingDoor = 1
        }

        [SerializeField] private Transform movingPart;
        [SerializeField] private string cabinetLabel = "тумбочку";
        [SerializeField] private MotionMode motionMode = MotionMode.SlideDrawer;
        [SerializeField] private Vector3 openLocalOffset = new Vector3(0f, 0f, 0.38f);
        [SerializeField] private float openYAngle = 95f;
        [SerializeField] private float moveSpeed = 7f;

        private bool _isOpen;
        private Vector3 _closedLocalPos;
        private Quaternion _closedLocalRot;
        private Quaternion _openLocalRot;

        public bool IsOpen => _isOpen;

        public void Configure(Transform part, string label, MotionMode mode, Vector3 slideOffset, float swingAngle = 95f)
        {
            movingPart = part;
            cabinetLabel = label;
            motionMode = mode;
            openLocalOffset = slideOffset;
            openYAngle = swingAngle;
        }

        private void Awake()
        {
            if (movingPart == null)
                movingPart = transform;

            _closedLocalPos = movingPart.localPosition;
            _closedLocalRot = movingPart.localRotation;
            _openLocalRot = _closedLocalRot * Quaternion.Euler(0f, openYAngle, 0f);
        }

        private void Update()
        {
            if (movingPart == null) return;

            if (motionMode == MotionMode.SlideDrawer)
            {
                Vector3 target = _isOpen ? _closedLocalPos + openLocalOffset : _closedLocalPos;
                movingPart.localPosition = Vector3.Lerp(movingPart.localPosition, target, Time.deltaTime * moveSpeed);
            }
            else
            {
                Quaternion target = _isOpen ? _openLocalRot : _closedLocalRot;
                movingPart.localRotation = Quaternion.Slerp(movingPart.localRotation, target, Time.deltaTime * moveSpeed);
            }
        }

        public string GetPrompt(PlayerContext context)
        {
            return _isOpen ? $"[E] Закрити {cabinetLabel}" : $"[E] Відкрити {cabinetLabel}";
        }

        public bool CanInteract(PlayerContext context) => true;

        public void Interact(PlayerContext context)
        {
            _isOpen = !_isOpen;
        }
    }
}

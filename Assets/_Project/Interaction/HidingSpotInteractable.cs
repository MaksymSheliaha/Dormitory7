using UnityEngine;

namespace Dormitory7.Interaction
{
    public class HidingSpotInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private string spotLabel = "шафі";
        [SerializeField] private Transform hideAnchor;
        [SerializeField] private Transform exitAnchor;

        private bool _isOccupied;
        private PlayerContext _occupant;

        public bool IsOccupied => _isOccupied;

        public void Configure(string label, Transform hidePoint, Transform exitPoint)
        {
            spotLabel = label;
            hideAnchor = hidePoint;
            exitAnchor = exitPoint;
        }

        public string GetPrompt(PlayerContext context)
        {
            return _isOccupied ? $"[E] Вийти з {spotLabel}" : $"[E] Сховатися у {spotLabel}";
        }

        public bool CanInteract(PlayerContext context)
        {
            return context.Controller != null;
        }

        public void Interact(PlayerContext context)
        {
            if (context.Controller == null) return;

            if (!_isOccupied)
            {
                _isOccupied = true;
                _occupant = context;
                Vector3 targetPos = hideAnchor != null ? hideAnchor.position : transform.position;
                Quaternion targetRot = hideAnchor != null ? hideAnchor.rotation : transform.rotation;
                context.Controller.EnterHiding(this, targetPos, targetRot);
            }
            else
            {
                ExitHiding();
            }
        }

        public void ExitHiding()
        {
            if (!_isOccupied) return;
            _isOccupied = false;
            if (_occupant.Controller != null)
            {
                Vector3 exitPos = exitAnchor != null ? exitAnchor.position : transform.position + transform.forward * 1.1f;
                _occupant.Controller.ExitHiding(exitPos);
            }
        }
    }
}

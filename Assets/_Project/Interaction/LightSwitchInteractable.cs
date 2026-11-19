using UnityEngine;

namespace Dormitory7.Interaction
{
    public class LightSwitchInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private Light[] controlledLights;
        [SerializeField] private string roomName = "у кімнаті №104";
        [SerializeField] private bool isOn = true;

        public bool IsOn => isOn;

        public void Configure(Light[] lights, string label, bool initiallyOn = true)
        {
            controlledLights = lights;
            roomName = label;
            isOn = initiallyOn;
            ApplyState();
        }

        private void Awake()
        {
            ApplyState();
        }

        private void ApplyState()
        {
            if (controlledLights == null) return;
            for (int i = 0; i < controlledLights.Length; i++)
            {
                if (controlledLights[i] != null)
                    controlledLights[i].enabled = isOn;
            }
        }

        public string GetPrompt(PlayerContext context)
        {
            return isOn ? $"[E] Вимкнути світло {roomName}" : $"[E] Увімкнути світло {roomName}";
        }

        public bool CanInteract(PlayerContext context) => true;

        public void Interact(PlayerContext context)
        {
            isOn = !isOn;
            ApplyState();
        }
    }
}

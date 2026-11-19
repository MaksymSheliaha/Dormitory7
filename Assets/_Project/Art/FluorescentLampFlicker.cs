using Dormitory7.Core;
using UnityEngine;

namespace Dormitory7.Art
{
    public class FluorescentLampFlicker : MonoBehaviour
    {
        [SerializeField] private Light targetLight;
        [SerializeField] private Renderer emissiveTubeRenderer;
        [SerializeField] private float minSteadySeconds = 3.5f;
        [SerializeField] private float maxSteadySeconds = 8.5f;
        [SerializeField] private float flickerBurstSeconds = 0.45f;
        [SerializeField] private float minIntensityMultiplier = 0.22f;

        private float _steadyTimer;
        private float _flickerTimer;
        private bool _isFlickering;
        private float _baseIntensity = 5.2f;
        private Color _baseEmissionColor = new Color(0.76f, 0.98f, 0.88f, 1f);
        private MaterialPropertyBlock _mpb;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public void Configure(Light lightComp, Renderer tubeRenderer, float minSteady = 3.5f, float maxSteady = 8.5f)
        {
            targetLight = lightComp;
            emissiveTubeRenderer = tubeRenderer;
            minSteadySeconds = minSteady;
            maxSteadySeconds = maxSteady;
        }

        private void Awake()
        {
            if (targetLight == null)
                targetLight = GetComponent<Light>();
            if (targetLight != null)
                _baseIntensity = targetLight.intensity;
            _mpb = new MaterialPropertyBlock();
            _steadyTimer = Random.Range(minSteadySeconds * 0.4f, maxSteadySeconds);
        }

        private void Update()
        {
            if (targetLight == null || !targetLight.enabled) return;
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            // Respect electrical panel dimming: if light was dimmed below 60% externally, don't fight it
            if (!_isFlickering)
            {
                _steadyTimer -= Time.deltaTime;
                if (_steadyTimer <= 0f)
                {
                    _baseIntensity = targetLight.intensity;
                    _isFlickering = true;
                    _flickerTimer = Random.Range(flickerBurstSeconds * 0.6f, flickerBurstSeconds * 1.4f);
                }
                return;
            }

            _flickerTimer -= Time.deltaTime;
            if (_flickerTimer <= 0f)
            {
                _isFlickering = false;
                _steadyTimer = Random.Range(minSteadySeconds, maxSteadySeconds);
                ApplyMultiplier(1f);
                return;
            }

            float noise = Mathf.PerlinNoise(Time.time * 42f, transform.position.x * 1.7f);
            float mult = noise < 0.42f ? minIntensityMultiplier : (noise < 0.68f ? 0.62f : 1.04f);
            ApplyMultiplier(mult);
        }

        private void ApplyMultiplier(float mult)
        {
            if (targetLight != null)
                targetLight.intensity = _baseIntensity * mult;

            if (emissiveTubeRenderer != null && _mpb != null)
            {
                emissiveTubeRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionColorId, _baseEmissionColor * (1.35f * mult));
                emissiveTubeRenderer.SetPropertyBlock(_mpb);
            }
        }
    }
}

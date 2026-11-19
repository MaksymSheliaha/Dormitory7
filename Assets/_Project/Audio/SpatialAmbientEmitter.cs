using UnityEngine;

namespace Dormitory7.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public class SpatialAmbientEmitter : MonoBehaviour
    {
        [SerializeField] private AudioClip ambientClip;
        [SerializeField] private float volume = 0.30f;
        [SerializeField] private float minDistance = 1.5f;
        [SerializeField] private float maxDistance = 12f;

        private AudioSource _source;

        public void Configure(AudioClip clip, float vol, float minDist, float maxDist)
        {
            ambientClip = clip;
            volume = vol;
            minDistance = minDist;
            maxDistance = maxDist;
            ApplyToSource();
        }

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            ApplyToSource();
        }

        private void Start()
        {
            if (_source != null && _source.clip != null && !_source.isPlaying)
            {
                _source.time = Random.Range(0f, Mathf.Max(0.01f, _source.clip.length - 0.05f));
                _source.Play();
            }
        }

        private void ApplyToSource()
        {
            if (_source == null) _source = GetComponent<AudioSource>();
            if (_source == null) return;
            _source.clip = ambientClip;
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Logarithmic;
            _source.dopplerLevel = 0f;
            _source.minDistance = Mathf.Max(0.2f, minDistance);
            _source.maxDistance = Mathf.Max(minDistance + 1f, maxDistance);
            _source.volume = Mathf.Clamp01(volume);
        }
    }
}

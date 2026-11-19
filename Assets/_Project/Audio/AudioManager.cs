using System;
using Dormitory7.Core;
using Dormitory7.Features.Repairs;
using Dormitory7.NPC;
using UnityEngine;

namespace Dormitory7.Audio
{
    public enum MusicZone
    {
        CorridorEerie = 0,
        BasementGymPhonk = 1,
        ChaseOrHazardTension = 2
    }

    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("3D Ambient Loop Clips")]
        [SerializeField] private AudioClip ambFluorescentHum;
        [SerializeField] private AudioClip ambWaterDrip;
        [SerializeField] private AudioClip ambBasementPipes;
        [SerializeField] private AudioClip loopPanSizzle;
        [SerializeField] private AudioClip loopRoachScurry;
        [SerializeField] private AudioClip loopDichlorvosSpray;
        [SerializeField] private AudioClip loopPipeWaterJet;
        [SerializeField] private AudioClip loopElectricBuzz;

        [Header("Gameplay SFX Clips")]
        [SerializeField] private AudioClip sfxKomendantStep;
        [SerializeField] private AudioClip sfxKomendantKeys;
        [SerializeField] private AudioClip sfxSlipperSlap;
        [SerializeField] private AudioClip sfxPanClang;
        [SerializeField] private AudioClip sfxTapeWind;
        [SerializeField] private AudioClip sfxWrenchClank;
        [SerializeField] private AudioClip sfxBarbellClang;
        [SerializeField] private AudioClip sfxBossHandshake;
        [SerializeField] private AudioClip sfxDoorCreak;
        [SerializeField] private AudioClip sfxReprimandStamp;
        [SerializeField] private AudioClip sfxHeartbeat;
        [SerializeField] private AudioClip sfxQuestComplete;

        [Header("Zone & Tension Music Clips")]
        [SerializeField] private AudioClip musicCorridorEerie;
        [SerializeField] private AudioClip musicGymPhonk;
        [SerializeField] private AudioClip musicChaseTension;

        [Header("Mix Levels")]
        [Range(0f, 1f)] [SerializeField] private float musicBaseVolume = 0.34f;
        [Range(0f, 1f)] [SerializeField] private float sfxBaseVolume = 0.88f;

        public AudioClip AmbFluorescentHum => ambFluorescentHum;
        public AudioClip AmbWaterDrip => ambWaterDrip;
        public AudioClip AmbBasementPipes => ambBasementPipes;
        public AudioClip LoopPanSizzle => loopPanSizzle;
        public AudioClip LoopRoachScurry => loopRoachScurry;
        public AudioClip LoopDichlorvosSpray => loopDichlorvosSpray;
        public AudioClip LoopPipeWaterJet => loopPipeWaterJet;
        public AudioClip LoopElectricBuzz => loopElectricBuzz;

        public MusicZone ActiveMusicZone { get; private set; } = MusicZone.CorridorEerie;
        public float CurrentDangerLevel { get; private set; }
        public int PlayedSfxCount { get; private set; }

        private AudioSource[] _spatialPool;
        private int _poolCursor;
        private AudioSource _uiSource;
        private AudioSource _heartbeatSource;
        private AudioSource _musicCorridorSource;
        private AudioSource _musicGymSource;
        private AudioSource _musicTensionSource;

        private Transform _playerTransform;
        private KomendantAI _komendant;
        private HazardDirector _hazardDirector;
        private float _heartbeatTimer;

        public void AssignClips(
            AudioClip hum, AudioClip drip, AudioClip pipes,
            AudioClip sizzle, AudioClip scurry, AudioClip spray, AudioClip waterJet, AudioClip elecBuzz,
            AudioClip kStep, AudioClip kKeys, AudioClip slap, AudioClip panClang,
            AudioClip tapeWind, AudioClip wrenchClank, AudioClip barbellClang, AudioClip handshake,
            AudioClip doorCreak, AudioClip reprimandStamp, AudioClip heartbeat, AudioClip questComplete,
            AudioClip mCorridor, AudioClip mGym, AudioClip mTension)
        {
            ambFluorescentHum = hum;
            ambWaterDrip = drip;
            ambBasementPipes = pipes;
            loopPanSizzle = sizzle;
            loopRoachScurry = scurry;
            loopDichlorvosSpray = spray;
            loopPipeWaterJet = waterJet;
            loopElectricBuzz = elecBuzz;
            sfxKomendantStep = kStep;
            sfxKomendantKeys = kKeys;
            sfxSlipperSlap = slap;
            sfxPanClang = panClang;
            sfxTapeWind = tapeWind;
            sfxWrenchClank = wrenchClank;
            sfxBarbellClang = barbellClang;
            sfxBossHandshake = handshake;
            sfxDoorCreak = doorCreak;
            sfxReprimandStamp = reprimandStamp;
            sfxHeartbeat = heartbeat;
            sfxQuestComplete = questComplete;
            musicCorridorEerie = mCorridor;
            musicGymPhonk = mGym;
            musicChaseTension = mTension;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildAudioSources();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            var playerGo = GameObject.FindWithTag("Player");
            if (playerGo != null) _playerTransform = playerGo.transform;
            _komendant = FindAnyObjectByType<KomendantAI>();
            _hazardDirector = FindAnyObjectByType<HazardDirector>();

            StartMusicStems();
        }

        private void BuildAudioSources()
        {
            const int poolSize = 16;
            _spatialPool = new AudioSource[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject($"SpatialSfx_{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.dopplerLevel = 0f;
                _spatialPool[i] = src;
            }

            _uiSource = Create2DSource("UiSfxSource", loop: false);
            _heartbeatSource = Create2DSource("HeartbeatSource", loop: false);
            _musicCorridorSource = Create2DSource("Music_Corridor", loop: true);
            _musicGymSource = Create2DSource("Music_GymPhonk", loop: true);
            _musicTensionSource = Create2DSource("Music_ChaseTension", loop: true);
        }

        private AudioSource Create2DSource(string childName, bool loop)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;
            src.volume = 0f;
            return src;
        }

        private void StartMusicStems()
        {
            if (musicCorridorEerie != null)
            {
                _musicCorridorSource.clip = musicCorridorEerie;
                _musicCorridorSource.volume = musicBaseVolume * 0.45f;
                _musicCorridorSource.Play();
            }
            if (musicGymPhonk != null)
            {
                _musicGymSource.clip = musicGymPhonk;
                _musicGymSource.volume = 0f;
                _musicGymSource.Play();
            }
            if (musicChaseTension != null)
            {
                _musicTensionSource.clip = musicChaseTension;
                _musicTensionSource.volume = 0f;
                _musicTensionSource.Play();
            }
        }

        private void Update()
        {
            UpdateMusicZones();
            UpdateKomendantHeartbeat();
        }

        private void UpdateMusicZones()
        {
            bool isPlaying = GameStateManager.Instance == null || GameStateManager.Instance.CurrentState == GameState.Playing;
            float menuDuck = isPlaying ? 1f : 0.42f;

            // Priority 1: Komendant Chase / Investigation OR Hazard Cascade (>= 2 broken hazards)
            bool komendantHot = _komendant != null &&
                (_komendant.State == KomendantState.Chasing ||
                 _komendant.State == KomendantState.Investigating ||
                 _komendant.State == KomendantState.BribeWindow);
            bool hazardCascade = _hazardDirector != null && _hazardDirector.CountBrokenHazards() >= 2;

            // Priority 2: Player inside Basement Gym (y < -1.8, x > 1.0, z < -10.5)
            bool inBasementGym = false;
            if (_playerTransform != null)
            {
                Vector3 p = _playerTransform.position;
                inBasementGym = p.y < -1.8f && p.x > 1.0f && p.z < -10.5f;
            }

            if (komendantHot || hazardCascade)
                ActiveMusicZone = MusicZone.ChaseOrHazardTension;
            else if (inBasementGym)
                ActiveMusicZone = MusicZone.BasementGymPhonk;
            else
                ActiveMusicZone = MusicZone.CorridorEerie;

            float targetCorridor = (ActiveMusicZone == MusicZone.CorridorEerie ? musicBaseVolume * 0.85f : 0f) * menuDuck;
            float targetGym = (ActiveMusicZone == MusicZone.BasementGymPhonk ? musicBaseVolume * 1.15f : 0f) * menuDuck;
            float targetTension = (ActiveMusicZone == MusicZone.ChaseOrHazardTension ? musicBaseVolume * 1.10f : 0f) * menuDuck;

            float fadeRate = Time.unscaledDeltaTime * 0.65f;
            if (_musicCorridorSource != null) _musicCorridorSource.volume = Mathf.MoveTowards(_musicCorridorSource.volume, targetCorridor, fadeRate);
            if (_musicGymSource != null) _musicGymSource.volume = Mathf.MoveTowards(_musicGymSource.volume, targetGym, fadeRate);
            if (_musicTensionSource != null) _musicTensionSource.volume = Mathf.MoveTowards(_musicTensionSource.volume, targetTension, fadeRate);
        }

        private void UpdateKomendantHeartbeat()
        {
            bool isPlaying = GameStateManager.Instance == null || GameStateManager.Instance.CurrentState == GameState.Playing;
            CurrentDangerLevel = _komendant != null ? _komendant.DangerLevel : 0f;

            if (!isPlaying || CurrentDangerLevel <= 0.12f || sfxHeartbeat == null)
            {
                _heartbeatTimer = 0f;
                return;
            }

            _heartbeatTimer -= Time.deltaTime;
            if (_heartbeatTimer <= 0f)
            {
                // Accelerates from ~1.05s at low danger down to ~0.38s at max danger!
                _heartbeatTimer = Mathf.Lerp(1.05f, 0.38f, CurrentDangerLevel);
                float vol = Mathf.Lerp(0.18f, 0.85f, CurrentDangerLevel) * sfxBaseVolume;
                _heartbeatSource.pitch = Mathf.Lerp(0.92f, 1.24f, CurrentDangerLevel);
                _heartbeatSource.PlayOneShot(sfxHeartbeat, vol);
            }
        }

        // ==================== 3D Spatial & 2D One-Shot API ====================

        public void PlayOneShot3D(AudioClip clip, Vector3 worldPos, float volume = 0.8f, float minDist = 1.5f, float maxDist = 16f, float pitchJitter = 0.06f)
        {
            if (clip == null || _spatialPool == null || _spatialPool.Length == 0) return;

            AudioSource src = _spatialPool[_poolCursor];
            _poolCursor = (_poolCursor + 1) % _spatialPool.Length;

            src.transform.position = worldPos;
            src.minDistance = Mathf.Max(0.2f, minDist);
            src.maxDistance = Mathf.Max(minDist + 1f, maxDist);
            src.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            src.volume = 1f;
            src.PlayOneShot(clip, Mathf.Clamp01(volume * sfxBaseVolume));
            PlayedSfxCount++;
        }

        public void Play2D(AudioClip clip, float volume = 0.85f, float pitch = 1f)
        {
            if (clip == null || _uiSource == null) return;
            _uiSource.pitch = pitch;
            _uiSource.PlayOneShot(clip, Mathf.Clamp01(volume * sfxBaseVolume));
            PlayedSfxCount++;
        }

        // Audible through corridor walls!
        public void PlayKomendantStep(Vector3 pos) => PlayOneShot3D(sfxKomendantStep, pos, 0.92f, 3.0f, 24f, 0.05f);
        public void PlayKomendantKeys(Vector3 pos) => PlayOneShot3D(sfxKomendantKeys, pos, 0.78f, 3.2f, 26f, 0.08f);

        public void PlaySlipperSlap(Vector3 pos) => PlayOneShot3D(sfxSlipperSlap, pos, 0.95f, 1.5f, 14f, 0.09f);
        public void PlayPanClang(Vector3 pos) => PlayOneShot3D(sfxPanClang, pos, 0.95f, 1.8f, 18f, 0.07f);
        public void PlayDichlorvosBurst(Vector3 pos) => PlayOneShot3D(loopDichlorvosSpray, pos, 0.52f, 1.0f, 10f, 0.05f);
        public void PlayTapeWind(Vector3 pos) => PlayOneShot3D(sfxTapeWind, pos, 0.80f, 1.2f, 11f, 0.08f);
        public void PlayWrenchRepair(Vector3 pos) => PlayOneShot3D(sfxWrenchClank, pos, 0.85f, 1.5f, 15f, 0.08f);
        public void PlayBarbellClang(Vector3 pos) => PlayOneShot3D(sfxBarbellClang, pos, 0.90f, 2.0f, 18f, 0.06f);
        public void PlayBossHandshake(Vector3 pos) => PlayOneShot3D(sfxBossHandshake, pos, 1.0f, 2.2f, 20f, 0.04f);
        public void PlayDoorCreak(Vector3 pos) => PlayOneShot3D(sfxDoorCreak, pos, 0.65f, 1.2f, 12f, 0.08f);
        public void PlayReprimandStamp() => Play2D(sfxReprimandStamp, 0.95f, 1.0f);
        public void PlayQuestChime() => Play2D(sfxQuestComplete, 0.70f, 1.0f);
    }
}

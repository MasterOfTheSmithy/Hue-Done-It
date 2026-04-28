// File: Assets/_Project/Gameplay/Beta/BetaSlimeAudioFeedbackDirector.cs
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Local slime movement audio: squish landings, wall-sticks, low-gravity entry, water saturation warning,
    /// and death/spectator transition. Clips are generated at runtime; no imported audio assets required.
    /// </summary>
    [DefaultExecutionOrder(940)]
    [DisallowMultipleComponent]
    public sealed class BetaSlimeAudioFeedbackDirector : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float volume = 0.34f;
        [SerializeField, Min(0.05f)] private float scanIntervalSeconds = 0.10f;
        [SerializeField, Min(0.2f)] private float squishCooldownSeconds = 0.42f;

        private AudioSource _source;
        private AudioClip _landClip;
        private AudioClip _wallClip;
        private AudioClip _lowGravityClip;
        private AudioClip _wetWarningClip;
        private AudioClip _deathClip;

        private NetworkPlayerAuthoritativeMover _mover;
        private PlayerFloodZoneTracker _floodTracker;
        private PlayerLifeState _lifeState;

        private NetworkPlayerAuthoritativeMover.LocomotionState _lastState;
        private bool _initializedState;
        private bool _wasAlive = true;
        private bool _wasLowGravity;
        private bool _wasWetCritical;
        private float _lastLandingImpact;
        private float _nextScanTime;
        private float _lastSquishTime;

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.volume = volume;
        }

        private void Start()
        {
            _landClip = CreateSweepClip("HDI Slime Landing", 180f, 82f, 0.22f, 0.54f);
            _wallClip = CreateSweepClip("HDI Slime Wall Stick", 260f, 380f, 0.15f, 0.34f);
            _lowGravityClip = CreateSweepClip("HDI Low Gravity Bloom", 420f, 720f, 0.32f, 0.30f);
            _wetWarningClip = CreateSweepClip("HDI Wet Critical", 330f, 160f, 0.44f, 0.42f);
            _deathClip = CreateSweepClip("HDI Slime Diffuse", 220f, 55f, 0.72f, 0.52f);
        }

        private void Update()
        {
            if (_source != null)
            {
                _source.volume = volume;
            }

            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.unscaledTime + scanIntervalSeconds;
            ResolveLocalPlayer();
            TickFeedback();
        }

        private void ResolveLocalPlayer()
        {
            NetworkManager manager = NetworkManager.Singleton;
            NetworkObject playerObject = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            if (playerObject == null)
            {
                return;
            }

            _mover = playerObject.GetComponent<NetworkPlayerAuthoritativeMover>();
            _floodTracker = playerObject.GetComponent<PlayerFloodZoneTracker>();
            _lifeState = playerObject.GetComponent<PlayerLifeState>();
        }

        private void TickFeedback()
        {
            if (_lifeState != null)
            {
                bool alive = _lifeState.IsAlive;
                if (_wasAlive && !alive)
                {
                    Play(_deathClip, 1f);
                }
                _wasAlive = alive;
            }

            if (_mover != null)
            {
                NetworkPlayerAuthoritativeMover.LocomotionState state = _mover.CurrentState;
                if (!_initializedState)
                {
                    _initializedState = true;
                    _lastState = state;
                    _lastLandingImpact = _mover.LastLandingImpact;
                }

                bool landed = _lastState != NetworkPlayerAuthoritativeMover.LocomotionState.Grounded &&
                              state == NetworkPlayerAuthoritativeMover.LocomotionState.Grounded;

                float landingImpact = _mover.LastLandingImpact;
                if ((landed || landingImpact > _lastLandingImpact + 0.25f) && Time.unscaledTime - _lastSquishTime >= squishCooldownSeconds)
                {
                    _lastSquishTime = Time.unscaledTime;
                    Play(_landClip, Mathf.Clamp01(0.45f + landingImpact / 16f));
                }

                if ((_lastState != NetworkPlayerAuthoritativeMover.LocomotionState.WallStick &&
                     state == NetworkPlayerAuthoritativeMover.LocomotionState.WallStick) ||
                    (_lastState != NetworkPlayerAuthoritativeMover.LocomotionState.WallSlide &&
                     state == NetworkPlayerAuthoritativeMover.LocomotionState.WallSlide))
                {
                    Play(_wallClip, 0.75f);
                }

                bool lowGravity = _mover.IsInAlteredGravity;
                if (lowGravity && !_wasLowGravity)
                {
                    Play(_lowGravityClip, 0.85f);
                }

                _wasLowGravity = lowGravity;
                _lastLandingImpact = landingImpact;
                _lastState = state;
            }

            if (_floodTracker != null)
            {
                bool wetCritical = _floodTracker.IsCritical;
                if (wetCritical && !_wasWetCritical)
                {
                    Play(_wetWarningClip, 0.9f);
                }
                _wasWetCritical = wetCritical;
            }
        }

        private void Play(AudioClip clip, float gain)
        {
            if (clip == null || _source == null)
            {
                return;
            }

            _source.PlayOneShot(clip, Mathf.Clamp01(gain) * volume);
        }

        private static AudioClip CreateSweepClip(string name, float startHz, float endHz, float durationSeconds, float gain)
        {
            int sampleRate = Mathf.Max(22050, AudioSettings.outputSampleRate);
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(sampleRate * Mathf.Max(0.04f, durationSeconds)));
            float[] data = new float[sampleCount];

            double phase = 0.0;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)(sampleCount - 1);
                float hz = Mathf.Lerp(startHz, endHz, t);
                phase += (2.0 * Mathf.PI * hz) / sampleRate;

                float attack = Mathf.Clamp01(t / 0.06f);
                float release = Mathf.Clamp01((1f - t) / 0.22f);
                float envelope = Mathf.Min(attack, release);
                float body = Mathf.Sin((float)phase);
                float wobble = Mathf.Sin((float)(phase * 0.47f)) * 0.28f;
                data[i] = (body + wobble) * envelope * gain;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

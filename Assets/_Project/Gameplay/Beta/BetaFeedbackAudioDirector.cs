// File: Assets/_Project/Gameplay/Beta/BetaFeedbackAudioDirector.cs
using System.Collections.Generic;
using HueDoneIt.Flood;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Runtime-generated beta audio. No imported assets required: it synthesizes a soft looping music bed and
    /// short UI/gameplay feedback tones for tasks, objective changes, flood warnings, and round pressure.
    /// </summary>
    [DefaultExecutionOrder(460)]
    [DisallowMultipleComponent]
    public sealed class BetaFeedbackAudioDirector : MonoBehaviour
    {
        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.16f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.42f;
        [SerializeField] private bool playProceduralMusic = true;

        [Header("Feedback Rate Limits")]
        [SerializeField, Min(0.1f)] private float scanIntervalSeconds = 0.25f;
        [SerializeField, Min(1f)] private float floodWarningRepeatSeconds = 4f;
        [SerializeField, Min(1f)] private float lowTimeRepeatSeconds = 8f;

        private AudioSource _musicSource;
        private AudioSource _sfxSource;

        private AudioClip _musicClip;
        private AudioClip _taskStartClip;
        private AudioClip _taskCompleteClip;
        private AudioClip _taskFailClip;
        private AudioClip _objectiveClip;
        private AudioClip _floodWarningClip;
        private AudioClip _floodSurgeClip;
        private AudioClip _lowTimeClip;
        private AudioClip _deathClip;
        private AudioClip _slimeLandingClip;
        private AudioClip _slimeWallClip;
        private AudioClip _lowGravityClip;
        private AudioClip _wetCriticalClip;

        private readonly Dictionary<NetworkRepairTask, RepairTaskState> _repairTaskStates = new();
        private readonly Dictionary<TaskObjectiveBase, RepairTaskState> _advancedTaskStates = new();

        private NetworkRoundState _roundState;
        private FloodSequenceController _floodController;
        private PlayerLifeState _localLifeState;
        private NetworkPlayerAuthoritativeMover _localMover;
        private PlayerFloodZoneTracker _localFloodTracker;
        private string _lastObjective = string.Empty;
        private RoundPhase _lastPhase;
        private NetworkRoundState.PressureStage _lastPressure;
        private bool _initializedRoundState;
        private bool _initializedLifeState;
        private bool _initializedMoveState;
        private bool _localWasAlive;
        private bool _wasLowGravity;
        private bool _wasWetCritical;
        private bool _wasFloodTelegraphActive;
        private bool _wasFloodPulseActive;
        private NetworkPlayerAuthoritativeMover.LocomotionState _lastMoveState;
        private float _lastLandingImpact;
        private float _nextScanTime;
        private float _lastSquishTime = -999f;
        private float _lastFloodWarningTime = -999f;
        private float _lastLowTimeWarningTime = -999f;

        private void Awake()
        {
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.spatialBlend = 0f;
            _musicSource.volume = musicVolume;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.loop = false;
            _sfxSource.spatialBlend = 0f;
            _sfxSource.volume = sfxVolume;
        }

        private void Start()
        {
            CreateClips();
            if (playProceduralMusic && _musicClip != null)
            {
                _musicSource.clip = _musicClip;
                _musicSource.volume = musicVolume;
                _musicSource.Play();
            }
        }

        private void Update()
        {
            if (_musicSource != null)
            {
                _musicSource.volume = musicVolume;
            }

            if (_sfxSource != null)
            {
                _sfxSource.volume = sfxVolume;
            }

            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.unscaledTime + scanIntervalSeconds;
            ResolveReferences();
            ScanRoundFeedback();
            ScanLocalLifeFeedback();
            ScanSlimeMovementFeedback();
            ScanFloodFeedback();
            ScanTaskFeedback();
        }

        private void ResolveReferences()
        {
            if (_roundState == null)
            {
                _roundState = FindFirstObjectByType<NetworkRoundState>();
            }

            if (_floodController == null)
            {
                _floodController = FindFirstObjectByType<FloodSequenceController>();
            }

            if (_localLifeState == null)
            {
                NetworkManager manager = NetworkManager.Singleton;
                NetworkObject playerObject = manager != null && manager.LocalClient != null
                    ? manager.LocalClient.PlayerObject
                    : null;
                if (playerObject != null)
                {
                    _localLifeState = playerObject.GetComponent<PlayerLifeState>();
                    _localMover = playerObject.GetComponent<NetworkPlayerAuthoritativeMover>();
                    _localFloodTracker = playerObject.GetComponent<PlayerFloodZoneTracker>();
                }
            }
        }

        private void ScanLocalLifeFeedback()
        {
            if (_localLifeState == null)
            {
                _initializedLifeState = false;
                return;
            }

            bool alive = _localLifeState.IsAlive;
            if (!_initializedLifeState)
            {
                _initializedLifeState = true;
                _localWasAlive = alive;
                return;
            }

            if (_localWasAlive && !alive)
            {
                Play(_deathClip);
            }

            _localWasAlive = alive;
        }

        private void ScanSlimeMovementFeedback()
        {
            if (_localMover != null)
            {
                NetworkPlayerAuthoritativeMover.LocomotionState state = _localMover.CurrentState;
                if (!_initializedMoveState)
                {
                    _initializedMoveState = true;
                    _lastMoveState = state;
                    _lastLandingImpact = _localMover.LastLandingImpact;
                }

                bool landed = _lastMoveState != NetworkPlayerAuthoritativeMover.LocomotionState.Grounded &&
                              state == NetworkPlayerAuthoritativeMover.LocomotionState.Grounded;

                float landingImpact = _localMover.LastLandingImpact;
                if ((landed || landingImpact > _lastLandingImpact + 0.25f) && Time.unscaledTime - _lastSquishTime >= 0.42f)
                {
                    _lastSquishTime = Time.unscaledTime;
                    Play(_slimeLandingClip, Mathf.Clamp01(0.45f + landingImpact / 16f));
                }

                if ((_lastMoveState != NetworkPlayerAuthoritativeMover.LocomotionState.WallStick &&
                     state == NetworkPlayerAuthoritativeMover.LocomotionState.WallStick) ||
                    (_lastMoveState != NetworkPlayerAuthoritativeMover.LocomotionState.WallSlide &&
                     state == NetworkPlayerAuthoritativeMover.LocomotionState.WallSlide))
                {
                    Play(_slimeWallClip, 0.75f);
                }

                bool lowGravity = _localMover.IsInAlteredGravity;
                if (lowGravity && !_wasLowGravity)
                {
                    Play(_lowGravityClip, 0.85f);
                }

                _wasLowGravity = lowGravity;
                _lastLandingImpact = landingImpact;
                _lastMoveState = state;
            }
            else
            {
                _initializedMoveState = false;
            }

            if (_localFloodTracker != null)
            {
                bool wetCritical = _localFloodTracker.IsCritical;
                if (wetCritical && !_wasWetCritical)
                {
                    Play(_wetCriticalClip, 0.85f);
                }

                _wasWetCritical = wetCritical;
            }
        }

        private void ScanRoundFeedback()
        {
            if (_roundState == null)
            {
                return;
            }

            if (!_initializedRoundState)
            {
                _initializedRoundState = true;
                _lastPhase = _roundState.CurrentPhase;
                _lastPressure = _roundState.CurrentPressureStage;
                _lastObjective = _roundState.CurrentObjective;
                return;
            }

            if (_lastPhase != _roundState.CurrentPhase)
            {
                _lastPhase = _roundState.CurrentPhase;
                Play(_objectiveClip);
            }

            if (_lastPressure != _roundState.CurrentPressureStage)
            {
                _lastPressure = _roundState.CurrentPressureStage;
                Play(_floodWarningClip);
            }

            string objective = _roundState.CurrentObjective;
            if (!string.IsNullOrWhiteSpace(objective) && objective != _lastObjective)
            {
                _lastObjective = objective;
                Play(_objectiveClip);
            }

            if (_roundState.RoundTimeRemaining > 0f &&
                _roundState.RoundTimeRemaining <= 30f &&
                Time.unscaledTime - _lastLowTimeWarningTime >= lowTimeRepeatSeconds)
            {
                _lastLowTimeWarningTime = Time.unscaledTime;
                Play(_lowTimeClip);
            }
        }

        private void ScanFloodFeedback()
        {
            if (_floodController == null)
            {
                return;
            }

            bool telegraph = _floodController.IsPulseTelegraphActive;
            bool pulse = _floodController.IsPulseActive;

            if (telegraph && !_wasFloodTelegraphActive && Time.unscaledTime - _lastFloodWarningTime >= floodWarningRepeatSeconds)
            {
                _lastFloodWarningTime = Time.unscaledTime;
                Play(_floodWarningClip);
            }

            if (pulse && !_wasFloodPulseActive)
            {
                Play(_floodSurgeClip);
            }

            _wasFloodTelegraphActive = telegraph;
            _wasFloodPulseActive = pulse;
        }

        private void ScanTaskFeedback()
        {
            NetworkRepairTask[] repairTasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            for (int i = 0; i < repairTasks.Length; i++)
            {
                NetworkRepairTask task = repairTasks[i];
                if (task == null)
                {
                    continue;
                }

                RepairTaskState current = task.CurrentState;
                if (!_repairTaskStates.TryGetValue(task, out RepairTaskState previous))
                {
                    _repairTaskStates[task] = current;
                    continue;
                }

                if (previous == current)
                {
                    continue;
                }

                _repairTaskStates[task] = current;
                PlayTaskTransition(previous, current);
            }

            TaskObjectiveBase[] advancedTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            for (int i = 0; i < advancedTasks.Length; i++)
            {
                TaskObjectiveBase task = advancedTasks[i];
                if (task == null)
                {
                    continue;
                }

                RepairTaskState current = task.CurrentState;
                if (!_advancedTaskStates.TryGetValue(task, out RepairTaskState previous))
                {
                    _advancedTaskStates[task] = current;
                    continue;
                }

                if (previous == current)
                {
                    continue;
                }

                _advancedTaskStates[task] = current;
                PlayTaskTransition(previous, current);
            }
        }

        private void PlayTaskTransition(RepairTaskState previous, RepairTaskState current)
        {
            switch (current)
            {
                case RepairTaskState.InProgress:
                    Play(_taskStartClip);
                    break;
                case RepairTaskState.Completed:
                    Play(_taskCompleteClip);
                    break;
                case RepairTaskState.FailedAttempt:
                case RepairTaskState.Cancelled:
                case RepairTaskState.Locked:
                    Play(_taskFailClip);
                    break;
            }
        }

        private void Play(AudioClip clip)
        {
            Play(clip, 1f);
        }

        private void Play(AudioClip clip, float gain)
        {
            if (clip == null || _sfxSource == null)
            {
                return;
            }

            _sfxSource.PlayOneShot(clip, Mathf.Clamp01(gain) * sfxVolume);
        }

        private void CreateClips()
        {
            _musicClip = CreateMusicLoop("HDI Procedural Beta Music", 12f);
            _taskStartClip = CreateToneSweep("HDI Task Start", 440f, 720f, 0.16f, 0.35f);
            _taskCompleteClip = CreateToneSweep("HDI Task Complete", 520f, 1040f, 0.34f, 0.45f);
            _taskFailClip = CreateToneSweep("HDI Task Fail", 260f, 145f, 0.28f, 0.55f);
            _objectiveClip = CreateToneSweep("HDI Objective Ping", 620f, 980f, 0.22f, 0.40f);
            _floodWarningClip = CreateToneSweep("HDI Flood Warning", 330f, 220f, 0.42f, 0.58f);
            _floodSurgeClip = CreateToneSweep("HDI Flood Surge", 130f, 85f, 0.72f, 0.70f);
            _lowTimeClip = CreateToneSweep("HDI Low Time", 880f, 880f, 0.12f, 0.44f);
            _deathClip = CreateToneSweep("HDI Spectator Transition", 180f, 92f, 0.58f, 0.62f);
            _slimeLandingClip = CreateToneSweep("HDI Slime Landing", 180f, 82f, 0.22f, 0.54f);
            _slimeWallClip = CreateToneSweep("HDI Slime Wall Stick", 260f, 380f, 0.15f, 0.34f);
            _lowGravityClip = CreateToneSweep("HDI Low Gravity Bloom", 420f, 720f, 0.32f, 0.30f);
            _wetCriticalClip = CreateToneSweep("HDI Wet Critical", 330f, 160f, 0.44f, 0.42f);
        }

        private static AudioClip CreateToneSweep(string name, float startHz, float endHz, float durationSeconds, float gain)
        {
            int sampleRate = Mathf.Max(22050, AudioSettings.outputSampleRate);
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(sampleRate * Mathf.Max(0.02f, durationSeconds)));
            float[] data = new float[sampleCount];

            double phase = 0.0;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)(sampleCount - 1);
                float hz = Mathf.Lerp(startHz, endHz, t);
                phase += (2.0 * Mathf.PI * hz) / sampleRate;

                float attack = Mathf.Clamp01(t / 0.08f);
                float release = Mathf.Clamp01((1f - t) / 0.18f);
                float envelope = Mathf.Min(attack, release);
                float sine = Mathf.Sin((float)phase);
                float second = Mathf.Sin((float)(phase * 1.5)) * 0.20f;
                data[i] = (sine + second) * envelope * gain;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateMusicLoop(string name, float durationSeconds)
        {
            int sampleRate = Mathf.Max(22050, AudioSettings.outputSampleRate);
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(sampleRate * Mathf.Max(1f, durationSeconds)));
            float[] data = new float[sampleCount];

            float[] notes = { 196f, 246.94f, 293.66f, 392f, 329.63f, 293.66f, 246.94f, 220f };
            for (int i = 0; i < sampleCount; i++)
            {
                float seconds = i / (float)sampleRate;
                int step = Mathf.FloorToInt(seconds * 2f) % notes.Length;
                float local = (seconds * 2f) - Mathf.Floor(seconds * 2f);
                float hz = notes[step];

                float envelope = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(local / 0.12f)) *
                                 Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - local) / 0.20f));

                float bass = Mathf.Sin(2f * Mathf.PI * (hz * 0.5f) * seconds) * 0.18f;
                float lead = Mathf.Sin(2f * Mathf.PI * hz * seconds) * 0.10f;
                float shimmer = Mathf.Sin(2f * Mathf.PI * (hz * 2f) * seconds) * 0.035f;
                data[i] = (bass + ((lead + shimmer) * envelope)) * 0.55f;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

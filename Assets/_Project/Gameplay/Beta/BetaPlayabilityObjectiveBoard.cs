// File: Assets/_Project/Gameplay/Beta/BetaPlayabilityObjectiveBoard.cs
using System.Text;
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Lightweight world-space beta guidance board. The map currently has a lot of systems active at once;
    /// this board gives testers a stable "what do I do now" answer without changing win logic.
    /// </summary>
    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    public sealed class BetaPlayabilityObjectiveBoard : MonoBehaviour
    {
        private const string RootName = "__BetaPlayabilityObjectiveBoard";

        [SerializeField, Min(0.25f)] private float refreshIntervalSeconds = 0.5f;
        [SerializeField] private Vector3 boardPosition = new(0f, 2.85f, -7.85f);
        [SerializeField] private Vector3 arrowPosition = new(0f, 1.25f, -5.9f);

        private Transform _root;
        private TextMesh _boardText;
        private Transform _arrow;
        private TextMesh _arrowText;
        private float _nextRefreshTime;

        private void Start()
        {
            EnsureVisuals();
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
            EnsureVisuals();
            Refresh();
        }

        private void EnsureVisuals()
        {
            if (_root != null)
            {
                return;
            }

            GameObject existing = GameObject.Find(RootName);
            if (existing != null)
            {
                _root = existing.transform;
                _boardText = existing.GetComponentInChildren<TextMesh>();
                return;
            }

            GameObject rootObject = new GameObject(RootName);
            _root = rootObject.transform;

            GameObject boardBacking = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boardBacking.name = "ObjectiveBoardBacking";
            boardBacking.transform.SetParent(_root, false);
            boardBacking.transform.position = boardPosition + new Vector3(0f, 0f, 0.09f);
            boardBacking.transform.localScale = new Vector3(8.4f, 2.15f, 0.12f);
            ApplyMaterial(boardBacking, new Color(0.025f, 0.028f, 0.04f, 0.98f));
            DestroyCollider(boardBacking);

            GameObject boardTextObject = new GameObject("ObjectiveBoardText");
            boardTextObject.transform.SetParent(_root, false);
            boardTextObject.transform.position = boardPosition;
            _boardText = boardTextObject.AddComponent<TextMesh>();
            _boardText.anchor = TextAnchor.MiddleCenter;
            _boardText.alignment = TextAlignment.Center;
            _boardText.characterSize = 0.13f;
            _boardText.fontSize = 42;
            _boardText.color = new Color(0.92f, 1f, 0.82f, 1f);

            GameObject arrowObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arrowObject.name = "ObjectiveDirectionArrow";
            arrowObject.transform.SetParent(_root, false);
            arrowObject.transform.position = arrowPosition;
            arrowObject.transform.localScale = new Vector3(0.28f, 0.08f, 1.05f);
            ApplyMaterial(arrowObject, new Color(1f, 0.84f, 0.12f, 1f));
            DestroyCollider(arrowObject);
            _arrow = arrowObject.transform;

            GameObject arrowTextObject = new GameObject("ObjectiveArrowText");
            arrowTextObject.transform.SetParent(_root, false);
            arrowTextObject.transform.position = arrowPosition + Vector3.up * 0.9f;
            _arrowText = arrowTextObject.AddComponent<TextMesh>();
            _arrowText.anchor = TextAnchor.MiddleCenter;
            _arrowText.alignment = TextAlignment.Center;
            _arrowText.characterSize = 0.10f;
            _arrowText.fontSize = 34;
            _arrowText.color = Color.white;
        }

        private void Refresh()
        {
            Transform localPlayer = ResolveLocalPlayer();
            Transform target = ResolveNextTarget(localPlayer, out string targetLabel, out string targetState);

            if (_boardText != null)
            {
                _boardText.text = BuildBoardText(targetLabel, targetState);
                FaceCamera(_boardText.transform);
            }

            if (_arrow != null)
            {
                bool hasTarget = target != null && localPlayer != null;
                _arrow.gameObject.SetActive(hasTarget);
                if (hasTarget)
                {
                    Vector3 dir = target.position - _arrow.position;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.001f)
                    {
                        _arrow.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
                    }
                }
            }

            if (_arrowText != null)
            {
                bool hasTarget = target != null && localPlayer != null;
                _arrowText.gameObject.SetActive(hasTarget);
                if (hasTarget)
                {
                    float distance = Vector3.Distance(localPlayer.position, target.position);
                    _arrowText.text = $"NEXT: {targetLabel}\n{distance:0}m";
                    FaceCamera(_arrowText.transform);
                }
            }
        }

        private string BuildBoardText(string targetLabel, string targetState)
        {
            NetworkRoundState round = FindFirstObjectByType<NetworkRoundState>();
            FloodSequenceController flood = FindFirstObjectByType<FloodSequenceController>();

            StringBuilder sb = new StringBuilder(256);
            sb.AppendLine("BETA PLAY LOOP");
            if (round != null)
            {
                sb.AppendLine(string.IsNullOrWhiteSpace(round.CurrentObjective) ? "Restore systems, survive flood, identify Bleach." : round.CurrentObjective);
                sb.AppendLine($"Ship timer: {FormatTime(round.RoundTimeRemaining)}  Pressure: {round.CurrentPressureStage}");
            }
            else
            {
                sb.AppendLine("Waiting for round state...");
            }

            if (flood != null)
            {
                sb.AppendLine(flood.IsPulseActive ? "Flood surge active: move to high/dry route." : flood.IsPulseTelegraphActive ? "Flood warning: leave marked flood zone." : "Flood: stable for now.");
            }

            sb.AppendLine(string.IsNullOrWhiteSpace(targetLabel) ? "Next: find a highlighted task terminal." : $"Next: {targetLabel} ({targetState})");
            return sb.ToString().TrimEnd();
        }

        private static Transform ResolveLocalPlayer()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.LocalClient != null && manager.LocalClient.PlayerObject != null)
            {
                return manager.LocalClient.PlayerObject.transform;
            }

            Camera cameraRef = Camera.main;
            return cameraRef != null ? cameraRef.transform : null;
        }

        private static Transform ResolveNextTarget(Transform origin, out string label, out string state)
        {
            label = string.Empty;
            state = string.Empty;
            if (origin == null)
            {
                return null;
            }

            Transform best = null;
            float bestScore = float.MaxValue;
            ulong localClientId = ResolveLocalClientId();

            NetworkRepairTask[] repairTasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            for (int i = 0; i < repairTasks.Length; i++)
            {
                NetworkRepairTask task = repairTasks[i];
                if (!IsRepairTaskGuideCandidate(task, localClientId))
                {
                    continue;
                }

                float score = (task.transform.position - origin.position).sqrMagnitude;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = task.transform;
                    label = task.DisplayName;
                    state = task.CurrentState.ToString();
                }
            }

            TaskObjectiveBase[] advancedTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            for (int i = 0; i < advancedTasks.Length; i++)
            {
                TaskObjectiveBase task = advancedTasks[i];
                if (!IsAdvancedTaskGuideCandidate(task, localClientId))
                {
                    continue;
                }

                float score = (task.transform.position - origin.position).sqrMagnitude * 1.15f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = task.transform;
                    label = task.DisplayName;
                    state = task.CurrentState.ToString();
                }
            }

            return best;
        }

        private static ulong ResolveLocalClientId()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null ? manager.LocalClientId : ulong.MaxValue;
        }

        private static bool IsRepairTaskGuideCandidate(NetworkRepairTask task, ulong localClientId)
        {
            if (task == null || task.IsCompleted || task.CurrentState == RepairTaskState.Locked)
            {
                return false;
            }

            if (task.CurrentState == RepairTaskState.InProgress &&
                localClientId != ulong.MaxValue &&
                task.ActiveClientId != localClientId)
            {
                return false;
            }

            return task.IsTaskEnvironmentSafe();
        }

        private static bool IsAdvancedTaskGuideCandidate(TaskObjectiveBase task, ulong localClientId)
        {
            if (task == null || task.IsCompleted || task.IsLocked)
            {
                return false;
            }

            return task.CurrentState != RepairTaskState.InProgress ||
                   localClientId == ulong.MaxValue ||
                   task.ActiveClientId == ulong.MaxValue ||
                   task.ActiveClientId == localClientId;
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        private static void FaceCamera(Transform target)
        {
            Camera cameraRef = Camera.main;
            if (target == null || cameraRef == null)
            {
                return;
            }

            Vector3 dir = target.position - cameraRef.transform.position;
            if (dir.sqrMagnitude > 0.001f)
            {
                target.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
        }

        private static void ApplyMaterial(GameObject go, Color color)
        {
            Renderer renderer = go != null ? go.GetComponent<Renderer>() : null;
            if (renderer == null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader) { color = color, name = go.name + " Material" };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            renderer.sharedMaterial = material;
        }

        private static void DestroyCollider(GameObject go)
        {
            Collider collider = go != null ? go.GetComponent<Collider>() : null;
            if (collider != null)
            {
                Destroy(collider);
            }
        }
    }
}

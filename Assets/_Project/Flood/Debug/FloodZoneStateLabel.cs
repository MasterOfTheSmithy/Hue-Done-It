// File: Assets/_Project/Flood/Debug/FloodZoneStateLabel.cs
using UnityEngine;
using UnityEngine.UI;

namespace HueDoneIt.Flood.Debug
{
    public sealed class FloodZoneStateLabel : MonoBehaviour
    {
        [SerializeField] private FloodZone zone;
        [SerializeField] private Text targetText;

        private void Awake()
        {
            if (zone == null)
            {
                zone = GetComponentInParent<FloodZone>();
            }
        }

        private void OnEnable()
        {
            if (zone != null)
            {
                zone.StateChanged += HandleStateChanged;
                Refresh();
            }
        }

        private void OnDisable()
        {
            if (zone != null)
            {
                zone.StateChanged -= HandleStateChanged;
            }
        }

        private void HandleStateChanged(FloodZoneState previous, FloodZoneState current)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (targetText == null)
            {
                return;
            }

            targetText.text = zone == null ? "Zone: <none>" : $"{zone.ZoneId}: {zone.CurrentState}";
        }
    }
}

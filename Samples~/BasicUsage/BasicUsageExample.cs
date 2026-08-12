#nullable enable
using System;
using System.Text;
using SK.CloudSave;
using SK.CloudSave.Unity;
using UnityEngine;

namespace SK.CloudSave.Samples
{
    /// <summary>
    /// Minimal end to end example: creates the manager, syncs on startup, tracks
    /// playtime, and saves/loads a tiny JSON payload.
    ///
    /// In the editor this talks to a fake cloud directory under Library/, so you can
    /// exercise the full pipeline (including conflicts, edit the files by hand)
    /// without a device.
    /// </summary>
    public class BasicUsageExample : MonoBehaviour
    {
        [Serializable]
        private class GameState
        {
            public int gold;
            public int level;
        }

        private const string Slot = "main";

        private CloudSaveManager? _saves;
        private GameState _state = new GameState();

        /// <summary>Playtime carried over from previous sessions, loaded with the save.</summary>
        private TimeSpan _previousPlaytime;
        private float _sessionStartTime;

        private TimeSpan TotalPlaytime => _previousPlaytime + TimeSpan.FromSeconds(Time.realtimeSinceStartup - _sessionStartTime);

        private async void Start()
        {
            _sessionStartTime = Time.realtimeSinceStartup;

            // One line of setup. Uses LongestPlaytimeResolver by default.
            _saves = CloudSaveKitFactory.CreateDefault();

            // Sign in / check availability. The game works either way.
            bool cloudReady = await _saves.InitializeAsync();
            Debug.Log($"Cloud ready: {cloudReady} (provider: {_saves.CloudProvider.ProviderName})");

            // React to changes pushed from other devices (iCloud only; may arrive
            // off the main thread, so only set a flag here).
            _saves.RemoteChanged += slot => Debug.Log($"Remote change reported for slot '{slot}'; consider SyncAsync on next focus.");

            await LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            LoadReport report = await _saves!.LoadAsync(Slot);
            if (report.Found)
            {
                _state = JsonUtility.FromJson<GameState>(Encoding.UTF8.GetString(report.Payload!));
                _previousPlaytime = report.Metadata.TotalPlaytime;
                Debug.Log($"Loaded from {report.Origin} (conflict: {report.HadConflict}, sync: {report.SyncAction}). " +
                          $"Gold={_state.gold}, playtime={report.Metadata.TotalPlaytime}");
            }
            else
            {
                Debug.Log("No save found anywhere; starting fresh.");
            }
        }

        public async void SaveGame()
        {
            _state.gold += 100; // pretend progress
            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(_state));

            SaveReport report = await _saves!.SaveAsync(Slot, payload, TotalPlaytime);
            Debug.Log(report.PushedToCloud
                ? "Saved locally and to the cloud."
                : $"Saved locally; cloud pending ({report.CloudError?.Message ?? "cloud unavailable"}).");
        }

        private async void OnApplicationFocus(bool hasFocus)
        {
            // Re entering the app is the natural moment to pick up other devices' progress.
            if (hasFocus && _saves != null)
            {
                SyncReport report = await _saves.SyncAsync(Slot);
                if (report.Action == SyncAction.PulledCloudToLocal)
                {
                    Debug.Log("Newer progress arrived from the cloud; reloading.");
                    await LoadAsync();
                }
            }
        }

        private void OnDestroy() => _saves?.Dispose();
    }
}

#nullable enable
using UnityEngine;

namespace SK.CloudSave.Unity
{
    public sealed class UnityCloudSaveLogger : ICloudSaveLogger
    {
        public static readonly UnityCloudSaveLogger Instance = new UnityCloudSaveLogger();

        public void Info(string message) => Debug.Log(message);
        public void Warn(string message) => Debug.LogWarning(message);
        public void Error(string message) => Debug.LogError(message);
    }
}

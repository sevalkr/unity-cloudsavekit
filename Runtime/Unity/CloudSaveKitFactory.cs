#nullable enable
using System;
using System.IO;
using SK.CloudSave.Providers;
using UnityEngine;

namespace SK.CloudSave.Unity
{
    /// <summary>
    /// Convenience factory that wires up a <see cref="CloudSaveManager"/> with the
    /// right provider for the current platform:
    ///
    /// <list type="bullet">
    /// <item>Editor, standalone dev: <see cref="FileCloudSaveProvider"/>, a fake cloud
    /// directory so the full sync/conflict pipeline runs in Play Mode.</item>
    /// <item>Android (with the Play Games plugin installed): <c>GooglePlaySaveProvider</c>.</item>
    /// <item>iOS / tvOS / visionOS: <c>ICloudKvsSaveProvider</c>.</item>
    /// <item>Anything else: <see cref="NullCloudSaveProvider"/>, saves stay local only.</item>
    /// </list>
    /// </summary>
    public static class CloudSaveKitFactory
    {
        private const string RootFolderName = "CloudSaveKit";
        private const string WriterIdFileName = "writer_id";

        public static CloudSaveManager CreateDefault(
            ISaveConflictResolver? conflictResolver = null,
            CloudSaveOptions? options = null,
            ICloudSaveLogger? logger = null)
        {
            logger ??= UnityCloudSaveLogger.Instance;
            conflictResolver ??= new LongestPlaytimeResolver();

            string root = Path.Combine(Application.persistentDataPath, RootFolderName);
            var localStore = new FileLocalSaveStore(root);
            string writerId = LoadOrCreateWriterId(root);

            ICloudSaveProvider provider = CreatePlatformProvider(conflictResolver, logger);
            logger.Info($"[CloudSaveKit] Using cloud provider: {provider.ProviderName}");

            return new CloudSaveManager(provider, localStore, conflictResolver, writerId, options, logger);
        }

        /// <summary>Creates the default cloud provider for the current platform without
        /// the manager, for advanced setups that compose their own manager.</summary>
        public static ICloudSaveProvider CreatePlatformProvider(ISaveConflictResolver conflictResolver, ICloudSaveLogger? logger = null)
        {
#if UNITY_EDITOR
            // A directory backed fake cloud, so Play Mode exercises the real sync logic.
            // Located outside Application.persistentDataPath to survive "clear save data" tests.
            string fakeCloudDir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Library", "CloudSaveKitFakeCloud");
            return new FileCloudSaveProvider(fakeCloudDir);
#elif UNITY_ANDROID && CLOUDSAVEKIT_GPGS
            return new GooglePlaySaveProvider(conflictResolver, logger);
#elif UNITY_ANDROID
            // Play Games plugin not detected. Install com.google.play.games (or define
            // CLOUDSAVEKIT_GPGS for .unitypackage installs) to enable cloud saves on Android.
            return new NullCloudSaveProvider();
#elif UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS
            return new ICloudKvsSaveProvider(logger);
#else
            return new NullCloudSaveProvider();
#endif
        }

        /// <summary>
        /// A stable per installation id stamped into every envelope this device writes.
        /// A random GUID persisted next to the saves.
        /// </summary>
        private static string LoadOrCreateWriterId(string rootDirectory)
        {
            string path = Path.Combine(rootDirectory, WriterIdFileName);
            try
            {
                if (File.Exists(path))
                {
                    string existing = File.ReadAllText(path).Trim();
                    if (Guid.TryParse(existing, out _))
                    {
                        return existing;
                    }
                }
                string fresh = Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(rootDirectory);
                File.WriteAllText(path, fresh);
                return fresh;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CloudSaveKit] Could not persist writer id ({e.Message}); using a session only id.");
                return Guid.NewGuid().ToString("N");
            }
        }
    }
}

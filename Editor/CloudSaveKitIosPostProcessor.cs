// Automates the Xcode setup that competing plugins leave to the user:
// adds the iCloud capability with key-value storage enabled to the generated project.
#if UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS
#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace SK.CloudSave.Editor
{
    public static class CloudSaveKitIosPostProcessor
    {
        private const string EntitlementsFileName = "CloudSaveKit.entitlements";

        [PostProcessBuild(45)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS && target != BuildTarget.tvOS
#if UNITY_2022_1_OR_NEWER
                && target != BuildTarget.VisionOS
#endif
                )
            {
                return;
            }

            try
            {
                string pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
                var project = new PBXProject();
                project.ReadFromFile(pbxPath);

                string mainTargetGuid = project.GetUnityMainTargetGuid();
                string mainTargetName = "Unity-iPhone";
                string entitlementsRelativePath = Path.Combine(mainTargetName, EntitlementsFileName);

                var capabilities = new ProjectCapabilityManager(pbxPath, entitlementsRelativePath, targetGuid: mainTargetGuid);
                capabilities.AddiCloud(
                    enableKeyValueStorage: true,
                    enableiCloudDocument: false,
                    enablecloudKit: false,
                    addDefaultContainers: false,
                    customContainers: Array.Empty<string>());
                capabilities.WriteToFile();

                Debug.Log($"[CloudSaveKit] Added iCloud key-value storage entitlement ({entitlementsRelativePath}). " +
                          "Remember to enable the iCloud capability for your App ID in the Apple Developer portal.");
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[CloudSaveKit] Failed to add the iCloud entitlement to the Xcode project automatically. " +
                    "Add it manually in Xcode: target > Signing & Capabilities > + iCloud > Key-value storage. " +
                    $"Error: {e}");
            }
        }
    }
}
#endif

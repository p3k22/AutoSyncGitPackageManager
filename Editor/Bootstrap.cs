#if UNITY_EDITOR
   namespace P3k.AutoSyncGitPackageManager.Editor
   {
      using System.Linq;

      using UnityEditor;

      using UnityEngine;

      [InitializeOnLoad]
      public static class Bootstrap
      {
         private const double MAX_WAIT_SECONDS = 120.0;

         private const int REQUIRED_STABLE_FRAMES = 20;

         private const string SESSION_KEY_DID_RUN = "P3k.AutoSyncGitPM.DidRunThisSession";

         private static readonly double Start = EditorApplication.timeSinceStartup;

         private static int _stableFrames;

         static Bootstrap()
         {
            if (SessionState.GetBool(SESSION_KEY_DID_RUN, false))
            {
               return;
            }

            EditorApplication.update += WaitForEditorReady;
         }

         private static AutoSyncGitPackageConfig FindAnyConfig()
         {
            var guids = AssetDatabase.FindAssets("t:AutoSyncGitPackageConfig");
            if (guids != null && guids.Length > 0)
            {
               var path = AssetDatabase.GUIDToAssetPath(guids[0]);
               return AssetDatabase.LoadAssetAtPath<AutoSyncGitPackageConfig>(path);
            }

            return null;
         }

         private static bool HasManagerWindowOpen()
         {
            return Resources.FindObjectsOfTypeAll<GitPackageManagerWindow>().Length > 0;
         }

         private static void AutoInitAndMaybeUpdate()
         {
            // Before: LoadRememberedConfig() ?? CreateOrRecoverConfigFromSnapshot()  (this created duplicates) :contentReference[oaicite:2]{index=2}
            var cfg = FindAnyConfig();

            // If no config is in the project, do nothing on startup.
            // The user can open the window and use Create/Find (which already exists in your UI). :contentReference[oaicite:3]{index=3}
            if (cfg == null)
            {
               return;
            }

            // Do not RememberConfig here; “remembered config” is removed per request.
            AutoSyncGitPackageService.Initialize(cfg);

            if (!cfg.AutoUpdatePackages)
            {
               return;
            }

            var wasOpen = HasManagerWindowOpen();
            GitPackageManagerWindow openedWin = null;

            if (!wasOpen)
            {
               openedWin = EditorWindow.GetWindow<GitPackageManagerWindow>(false, "P3k Git Package Manager (GPM)", false);
               openedWin.ShowUtility();
            }

            var candidates = AutoSyncGitPackageService.CheckForGitUpdates(cfg);
            if (candidates != null && candidates.Count > 0)
            {
               AutoSyncGitPackageService.UpdateSpecificUrls(candidates.Select(c => c.Url));
            }

            SelfUpdateUtility.CheckForSelfUpdate();

            if (!wasOpen && openedWin != null)
            {
               openedWin.Close();
            }
         }

         private static void RunNow()
         {
            EditorApplication.update -= WaitForEditorReady;
            SessionState.SetBool(SESSION_KEY_DID_RUN, true);
            AutoInitAndMaybeUpdate();
         }

         private static void WaitForEditorReady()
         {
            if (SessionState.GetBool(SESSION_KEY_DID_RUN, false))
            {
               EditorApplication.update -= WaitForEditorReady;
               return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
            {
               _stableFrames = 0;
               return;
            }

            if (EditorApplication.timeSinceStartup - Start > MAX_WAIT_SECONDS)
            {
               RunNow();
               return;
            }

            _stableFrames++;
            if (_stableFrames < REQUIRED_STABLE_FRAMES)
            {
               return;
            }

            RunNow();
         }
      }
   }
#endif
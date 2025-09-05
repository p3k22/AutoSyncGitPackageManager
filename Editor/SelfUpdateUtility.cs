namespace P3k.AutoSyncGitPackageManager.Editor
{
   using System.Linq;

   using UnityEditor;

   using UnityEngine;

   internal static class SelfUpdateUtility
   {
      private const string SelfPackageName = "com.p3k.autosyncgitpackagemanager";

      private const string SelfPackageGitUrl = "git@github.com:p3k22/AutoSyncGitPackageManager.git";

      private static UnityEditor.PackageManager.Requests.ListRequest _listRequest;

      /// <summary>
      /// For Git packages, there’s no registry search.
      /// We simply check if the package is installed, and if so, re-add it to pull latest.
      /// </summary>
      public static void CheckForSelfUpdate()
      {
         _listRequest = UnityEditor.PackageManager.Client.List(true);
         EditorApplication.update += OnListProgress;
      }

      private static void OnListProgress()
      {
         if (!_listRequest.IsCompleted)
            return;

         EditorApplication.update -= OnListProgress;

         if (_listRequest.Status == UnityEditor.PackageManager.StatusCode.Failure)
         {
            Debug.LogError($"Failed to list packages: {_listRequest.Error.message}");
            return;
         }

         var selfPkg = _listRequest.Result.FirstOrDefault(p => p.name == SelfPackageName);
         if (selfPkg == null)
         {
            Debug.LogWarning("Self package not found in manifest. Is this the actual Project??");
            return;
         }

         UpdateSelfPackage();
      }

      /// <summary>
      /// Re-adds the Git package, forcing Unity to fetch the latest commit/tag.
      /// </summary>
      private static void UpdateSelfPackage()
      {
         Debug.Log($"Updating {SelfPackageName} from {SelfPackageGitUrl}...");
         UnityEditor.PackageManager.Client.Add(SelfPackageGitUrl);
      }
   }
}

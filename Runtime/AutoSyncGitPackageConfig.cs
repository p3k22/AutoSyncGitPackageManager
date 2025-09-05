namespace P3k.AutoSyncGitPackageManager
{
   using System;
   using System.Collections.Generic;
   using System.Linq;

   using UnityEngine;

   /// <summary>
   ///    ScriptableObject configuration stored in the project.
   /// </summary>
   public class AutoSyncGitPackageConfig : ScriptableObject
   {
      public List<string> GitUrls = new List<string>();

      public bool WatchPackageCache = true;

      public bool AutoUpdatePackages;

      public List<GitPackageEntry> Resolved = new List<GitPackageEntry>();

      /// <summary>
      ///    Upsert mapping for URL -> package name + resolved path.
      /// </summary>
      /// <param name="url"></param>
      /// <param name="packageName"></param>
      /// <param name="resolvedPath"></param>
      public void UpsertResolved(string url, string packageName, string resolvedPath)
      {
         var norm = NormalizeGitUrl(url);
         var entry = Resolved.FirstOrDefault(e => string.Equals(e.GitUrl, norm, StringComparison.OrdinalIgnoreCase));
         if (entry == null)
         {
            entry = new GitPackageEntry {GitUrl = norm, PackageName = packageName, LastResolvedPath = resolvedPath};
            Resolved.Add(entry);
         }
         else
         {
            entry.PackageName = packageName;
            entry.LastResolvedPath = resolvedPath;
         }
      }

      /// <summary>
      ///    Lookup helpers.
      /// </summary>
      /// <param name="url"></param>
      /// <returns></returns>
      public string GetPackageNameForUrl(string url)
      {
         var norm = NormalizeGitUrl(url);
         var e = Resolved.FirstOrDefault(x => string.Equals(x.GitUrl, norm, StringComparison.OrdinalIgnoreCase));
         return e?.PackageName;
      }

      /// <summary>
      ///    Retrieves the resolved Git URL for the package name,
      /// </summary>
      /// <param name="packageName"></param>
      /// <returns> </returns>
      public string GetUrlForPackageName(string packageName)
      {
         var e = Resolved.FirstOrDefault(x => string.Equals(
         x.PackageName,
         packageName,
         StringComparison.OrdinalIgnoreCase));
         return e?.GitUrl;
      }

      /// <summary>
      ///    URL normalization kept here so config can normalize itself without Editor code.
      /// </summary>
      /// <param name="raw"></param>
      /// <returns></returns>
      public static string NormalizeGitUrl(string raw)
      {
         if (string.IsNullOrWhiteSpace(raw))
         {
            return string.Empty;
         }

         var s = raw.Trim();

         if (s.StartsWith("git@"))
         {
            var atIdx = s.IndexOf('@');
            var colonIdx = s.IndexOf(':', atIdx + 1);
            if (colonIdx > 0)
            {
               var host = s[(atIdx + 1)..colonIdx];
               var path = s[(colonIdx + 1)..];
               return $"ssh://git@{host}/{path}";
            }
         }

         return s;
      }
   }
}
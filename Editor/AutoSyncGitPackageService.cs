namespace P3k.AutoSyncGitPackageManager.Editor
{
   using System;
   using System.Collections.Concurrent;
   using System.Collections.Generic;
   using System.Diagnostics;
   using System.IO;
   using System.Linq;
   using System.Text.RegularExpressions;

   using Unity.Plastic.Newtonsoft.Json.Linq;

   using UnityEditor;
   using UnityEditor.PackageManager;
   using UnityEditor.PackageManager.Requests;

   using UnityEngine;

   using Debug = UnityEngine.Debug;

   internal static class AutoSyncGitPackageService
   {
      // Prevents accidental self-uninstall.
      private const string SELF_PACKAGE_NAME = "com.p3k.autosyncgitpackagemanager";

      private const double DEBOUNCE_SECONDS = 1.0;

      private const string DEFAULT_CONFIG_PATH = "Assets/AutoSyncGitPackageManager/GitPackageConfig.asset";

      private const string PREF_KEY_GUID = "P3k.AutoSyncGitPackage.ConfigGUID";

      private const string PREF_KEY_SNAPSHOT = "P3k.AutoSyncGitPackage.ConfigSnapshotJSON";

      private const string SESSION_KEY_AUTO_SYNCED = "P3k.AutoSyncGitPackage.AutoSynced";

      private const double SUPPRESS_AFTER_READD_SECONDS = 8.0;

      private static readonly List<AddJob> AddJobs = new List<AddJob>();

      private static readonly Dictionary<string, double> LastEventAt = new Dictionary<string, double>();

      private static readonly ConcurrentQueue<string> PendingReadd = new ConcurrentQueue<string>();

      private static readonly List<RemoveJob> RemoveJobs = new List<RemoveJob>();

      private static readonly Dictionary<string, double> SuppressedUntil = new Dictionary<string, double>();

      private static readonly Dictionary<string, FileSystemWatcher> Watchers =
         new Dictionary<string, FileSystemWatcher>();

      private static bool _autoSyncRequested;

      private static AutoSyncGitPackageConfig _config;

      private static ListRequest _listRequest;

      private static bool _updateHooked;

      private static bool IsSelfPackage(string packageName)
      {
         return !string.IsNullOrEmpty(packageName) && string.Equals(
                packageName,
                SELF_PACKAGE_NAME,
                StringComparison.OrdinalIgnoreCase);
      }

      private static bool IsManagedUrl(string normalizedUrl)
      {
         if (_config == null || string.IsNullOrEmpty(normalizedUrl)) return false;
         return _config.GitUrls.Contains(
         AutoSyncGitPackageConfig.NormalizeGitUrl(normalizedUrl),
         StringComparer.OrdinalIgnoreCase);
      }

      private static bool IsManagedPackageName(string packageName)
      {
         if (_config == null || string.IsNullOrEmpty(packageName)) return false;

         var entry = _config.Resolved.FirstOrDefault(e =>
            !string.IsNullOrEmpty(e?.PackageName) && string.Equals(
            e.PackageName,
            packageName,
            StringComparison.OrdinalIgnoreCase));

         if (entry == null || string.IsNullOrEmpty(entry.GitUrl)) return false;

         return IsManagedUrl(entry.GitUrl);
      }

      public static bool IsBusy
      {
         get
         {
            if (AddJobs.Count > 0)
            {
               return true;
            }

            if (RemoveJobs.Count > 0)
            {
               return true;
            }

            if (_listRequest is {IsCompleted: false})
            {
               return true;
            }

            return false;
         }
      }

      public static List<UpdateCandidate> CheckForGitUpdates(AutoSyncGitPackageConfig config)
      {
         Initialize(config);
         var result = new List<UpdateCandidate>();
         if (_config == null)
         {
            return result;
         }

         var urls = _config.GitUrls.Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(AutoSyncGitPackageConfig.NormalizeGitUrl).Distinct(StringComparer.OrdinalIgnoreCase);

         foreach (var url in urls)
         {
            var pkgName = _config.GetPackageNameForUrl(url);
            if (string.IsNullOrEmpty(pkgName))
            {
               continue;
            }

            var entry = _config.Resolved.FirstOrDefault(e => string.Equals(
            e.PackageName,
            pkgName,
            StringComparison.OrdinalIgnoreCase));

            var currentHash = GetHashFromResolvedPath(pkgName, entry?.LastResolvedPath);
            var remoteHash = TryGetRemoteHeadHash(url);
            var remoteTag = TryGetTagForHash(url, remoteHash);

            if (HashesDiffer(currentHash, remoteHash))
            {
               result.Add(
               new UpdateCandidate
                  {
                     Url = url,
                     PackageName = pkgName,
                     CurrentHash = currentHash,
                     RemoteHash = remoteHash,
                     RemoteTag = remoteTag
                  });
            }
         }

         return result;
      }

      public static void ForceRedownloadByPackageName(string packageName)
      {
         if (_config == null)
         {
            return;
         }

         if (IsSelfPackage(packageName))
         {
            return;
         }

         var now = EditorApplication.timeSinceStartup;
         if (SuppressedUntil.TryGetValue(packageName, out var until) && now < until)
         {
            return;
         }

         var url = _config.GetUrlForPackageName(packageName);
         if (string.IsNullOrEmpty(url))
         {
            return;
         }

         QueueRemoveThenAdd(packageName, url);
      }

      public static void Initialize(AutoSyncGitPackageConfig config)
      {
         _config = config;

         if (!_updateHooked)
         {
            EditorApplication.update += UpdatePump;
            EditorApplication.quitting += OnEditorQuitting;
            _updateHooked = true;
         }

         EnsureConfigRemembered();
         EnsureConfigHydratedFromBackupIfEmpty();
         RequestAutoSyncIfNeeded(_config);

         if (_config != null && _config.WatchPackageCache)
         {
            RefreshWatchers();
         }
      }

      public static AutoSyncGitPackageConfig LoadRememberedConfig()
      {
         var guid = EditorPrefs.GetString(PREF_KEY_GUID, string.Empty);
         if (string.IsNullOrEmpty(guid))
         {
            return null;
         }

         var path = AssetDatabase.GUIDToAssetPath(guid);
         if (string.IsNullOrEmpty(path))
         {
            EditorPrefs.DeleteKey(PREF_KEY_GUID);
            return null;
         }

         var cfg = AssetDatabase.LoadAssetAtPath<AutoSyncGitPackageConfig>(path);
         if (cfg == null)
         {
            EditorPrefs.DeleteKey(PREF_KEY_GUID);
            return null;
         }

         return cfg;
      }

      public static void RefreshWatchers()
      {
         DisposeAllWatchers();
         if (_config == null || !_config.WatchPackageCache)
         {
            return;
         }

         StartResolveAllPaths();
      }

      public static void RememberConfig(AutoSyncGitPackageConfig cfg)
      {
         if (cfg == null)
         {
            return;
         }

         var path = AssetDatabase.GetAssetPath(cfg);
         var guid = AssetDatabase.AssetPathToGUID(path);
         if (!string.IsNullOrEmpty(guid))
         {
            EditorPrefs.SetString(PREF_KEY_GUID, guid);
         }
      }

      public static void RemoveAndUninstallUrl(string rawUrl)
      {
         Initialize(_config);
         if (_config == null || string.IsNullOrWhiteSpace(rawUrl))
         {
            return;
         }

         var url = AutoSyncGitPackageConfig.NormalizeGitUrl(rawUrl);

         var before = _config.GitUrls.Count;
         _config.GitUrls = _config.GitUrls.Where(u => !string.Equals(
                                                      AutoSyncGitPackageConfig.NormalizeGitUrl(u),
                                                      url,
                                                      StringComparison.OrdinalIgnoreCase)).ToList();

         if (_config.GitUrls.Count != before)
         {
            EditorUtility.SetDirty(_config);
         }

         // Explicit user intent to uninstall this URL. Still block self-removal.
         QueueRemoveByUrl(url, force: true);
      }

      private static string StripUpmRevision(string url)
      {
         if (string.IsNullOrWhiteSpace(url)) return url;
         var hashIdx = url.IndexOf('#');
         return hashIdx >= 0 ? url.Substring(0, hashIdx) : url;
      }

      private static bool IsLikelyGitUrl(string s)
      {
         if (string.IsNullOrWhiteSpace(s)) return false;
         return s.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("git+ssh://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("git@", StringComparison.OrdinalIgnoreCase) || s.StartsWith(
                "https://",
                StringComparison.OrdinalIgnoreCase);
      }

      private static void ReconcileResolvedMappings(IList<UnityEditor.PackageManager.PackageInfo> installed)
      {
         try
         {
            if (_config == null || installed == null) return;

            var manifestPath = Path.Combine("Packages", "manifest.json");
            if (!File.Exists(manifestPath)) return;

            var text = File.ReadAllText(manifestPath);
            var root = JObject.Parse(text);
            var deps = root["dependencies"] as JObject;
            if (deps == null) return;

            var installedByName = installed.ToDictionary(p => p.name, p => p, StringComparer.OrdinalIgnoreCase);
            var resolvedByName = _config.Resolved.ToDictionary(
            r => r.PackageName ?? string.Empty,
            r => r,
            StringComparer.OrdinalIgnoreCase);

            var changed = false;

            foreach (var prop in deps.Properties())
            {
               var packageName = prop.Name;
               var val = prop.Value?.ToString() ?? string.Empty;
               if (!IsLikelyGitUrl(val)) continue;

               var rawUrl = StripUpmRevision(val);
               var normalized = AutoSyncGitPackageConfig.NormalizeGitUrl(rawUrl);

               if (!installedByName.TryGetValue(packageName, out var pkgInfo)) continue;
               if (pkgInfo == null || string.IsNullOrEmpty(pkgInfo.resolvedPath)) continue;

               // Only track packages whose URL is in the config (i.e., managed by this PM).
               if (!_config.GitUrls.Contains(normalized, StringComparer.OrdinalIgnoreCase)) continue;

               var already = resolvedByName.ContainsKey(packageName) || _config.Resolved.Any(r => string.Equals(
                             AutoSyncGitPackageConfig.NormalizeGitUrl(r.GitUrl),
                             normalized));

               if (already) continue;

               _config.UpsertResolved(normalized, packageName, pkgInfo.resolvedPath);
               changed = true;
            }

            if (changed)
            {
               EditorUtility.SetDirty(_config);
               AssetDatabase.SaveAssets();
               AssetDatabase.Refresh();
            }
         }
         catch
         {
            // Best-effort only.
         }
      }

      public static void RequestAutoSyncIfNeeded(AutoSyncGitPackageConfig cfg)
      {
         if (cfg == null)
         {
            return;
         }

         if (!cfg.AutoUpdatePackages)
         {
            return;
         }

         if (SessionState.GetBool(SESSION_KEY_AUTO_SYNCED, false))
         {
            return;
         }

         _autoSyncRequested = true;
      }

      public static void Shutdown()
      {
         DisposeAllWatchers();
         AddJobs.Clear();
         RemoveJobs.Clear();
         PendingReadd.Clear();
         LastEventAt.Clear();
         SuppressedUntil.Clear();
         _listRequest = null;
         _autoSyncRequested = false;

         if (_updateHooked)
         {
            EditorApplication.update -= UpdatePump;
            EditorApplication.quitting -= OnEditorQuitting;
            _updateHooked = false;
         }
      }

      public static void SyncAll(AutoSyncGitPackageConfig config)
      {
         Initialize(config);

         var uniqueUrls = config.GitUrls.Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(AutoSyncGitPackageConfig.NormalizeGitUrl).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

         if (config.AutoUpdatePackages)
         {
            foreach (var url in uniqueUrls)
            {
               QueueAdd(url);
            }
         }
         else
         {
            foreach (var url in uniqueUrls)
            {
               var knownPkg = config.GetPackageNameForUrl(url);
               if (string.IsNullOrEmpty(knownPkg))
               {
                  QueueAdd(url);
               }
            }
         }

         PersistNormalizedUrls(config, uniqueUrls);

         // Only prune packages that are tracked in _config.Resolved (i.e., managed by this PM).
         PruneStalePackages(uniqueUrls);
      }

      public static void UpdateSpecificUrls(IEnumerable<string> urls)
      {
         // Updates must not remove first; use in-place add to avoid resolver pruning other packages.
         if (urls == null)
         {
            return;
         }

         foreach (var u in urls)
         {
            if (string.IsNullOrWhiteSpace(u))
            {
               continue;
            }

            var normalized = AutoSyncGitPackageConfig.NormalizeGitUrl(u);

            // Always add in place; Unity will fetch latest for existing Git deps.
            QueueAdd(normalized);
         }
      }

      private static bool HashesDiffer(string current, string remote)
      {
         if (string.IsNullOrEmpty(remote))
         {
            return false;
         }

         if (string.IsNullOrEmpty(current))
         {
            return true;
         }

         if (current.Length < 40)
         {
            return !remote.StartsWith(current, StringComparison.OrdinalIgnoreCase);
         }

         return !string.Equals(current, remote, StringComparison.OrdinalIgnoreCase);
      }

      private static string GetHashFromResolvedPath(string packageName, string resolvedPath)
      {
         if (string.IsNullOrEmpty(resolvedPath))
         {
            return null;
         }

         try
         {
            var folder = Path.GetFileName(resolvedPath.Replace('\\', '/'));
            var at = folder.LastIndexOf('@');
            if (at >= 0 && at < folder.Length - 1)
            {
               return folder.Substring(at + 1);
            }
         }
         catch
         {
         }

         return null;
      }

      private static string TryGetRemoteHeadHash(string normalizedUrl)
      {
         try
         {
            var psi = new ProcessStartInfo
                         {
                            FileName = "git",
                            Arguments =
                               $"ls-remote {Quote(AutoSyncGitPackageConfig.NormalizeGitUrl(normalizedUrl))} HEAD",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                         };
            using (var p = Process.Start(psi))
            {
               var stdout = p.StandardOutput.ReadToEnd();
               var _ = p.StandardError.ReadToEnd();
               p.WaitForExit(10000);
               var m = Regex.Match(stdout, @"^[0-9a-fA-F]{40}\b", RegexOptions.Multiline);
               if (m.Success)
               {
                  return m.Value.ToLowerInvariant();
               }
            }
         }
         catch
         {
         }

         return null;

         static string Quote(string s)
         {
            return s.Contains(" ") ? $"\"{s}\"" : s;
         }
      }

      private static string TryGetTagForHash(string normalizedUrl, string targetHash)
      {
         if (string.IsNullOrEmpty(targetHash))
         {
            return null;
         }

         try
         {
            var psi = new ProcessStartInfo
                         {
                            FileName = "git",
                            Arguments = $"ls-remote --tags {Quote(normalizedUrl)}",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                         };
            using (var p = Process.Start(psi))
            {
               var stdout = p.StandardOutput.ReadToEnd();
               p.WaitForExit(10000);

               foreach (var line in stdout.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries))
               {
                  var parts = line.Split('\t');
                  if (parts.Length != 2)
                  {
                     continue;
                  }

                  var hash = parts[0].Trim();
                  var refname = parts[1].Trim();
                  if (!hash.Equals(targetHash, StringComparison.OrdinalIgnoreCase))
                  {
                     continue;
                  }

                  if (refname.EndsWith("^{}", StringComparison.Ordinal))
                  {
                     refname = refname.Substring(0, refname.Length - 3);
                  }

                  var m = Regex.Match(refname, @"refs/tags/(?<tag>[^\s/]+)$");
                  if (m.Success)
                  {
                     return m.Groups["tag"].Value;
                  }
               }
            }
         }
         catch
         {
         }

         return null;

         static string Quote(string s)
         {
            return s.Contains(" ") ? $"\"{s}\"" : s;
         }
      }

      private static string[] GetDependenciesFromInstalledPackageResolvedPath(string resolvedPath)
      {
         try
         {
            if (string.IsNullOrEmpty(resolvedPath))
            {
               return Array.Empty<string>();
            }

            var packageJsonPath = Path.Combine(resolvedPath, "package.json");
            if (!File.Exists(packageJsonPath))
            {
               return Array.Empty<string>();
            }

            var json = File.ReadAllText(packageJsonPath);
            var root = JObject.Parse(json);

            if (root["gitdependencies"] is not JObject gitDeps)
            {
               return Array.Empty<string>();
            }

            return gitDeps.Properties().Select(p => p.Value?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v))
               .ToArray();
         }
         catch
         {
            return Array.Empty<string>();
         }
      }

      private static void CreateOrRefreshWatcher(string packageName, string folder)
      {
         try
         {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
               return;
            }

            if (Watchers.TryGetValue(packageName, out var existing))
            {
               existing.EnableRaisingEvents = false;
               existing.Dispose();
               Watchers.Remove(packageName);
            }

            var watcher = new FileSystemWatcher(folder, "*.cs");
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;

            watcher.Changed += (s, e) => PendingReadd.Enqueue(packageName);
            watcher.Created += (s, e) => PendingReadd.Enqueue(packageName);
            watcher.Renamed += (s, e) => PendingReadd.Enqueue(packageName);
            watcher.Deleted += (s, e) => PendingReadd.Enqueue(packageName);

            watcher.EnableRaisingEvents = true;
            Watchers[packageName] = watcher;
         }
         catch (Exception e)
         {
            Debug.LogWarning($"AutoSyncGitPackage: Failed to watch {packageName} at {folder}. {e.Message}");
         }
      }

      private static void DisposeAllWatchers()
      {
         foreach (var kvp in Watchers.ToList())
         {
            try
            {
               kvp.Value.EnableRaisingEvents = false;
               kvp.Value.Dispose();
            }
            catch
            {
            }
         }

         Watchers.Clear();
      }

      private static void DisposeWatcherForPackage(string packageName)
      {
         if (string.IsNullOrEmpty(packageName))
         {
            return;
         }

         if (Watchers.TryGetValue(packageName, out var w))
         {
            try
            {
               w.EnableRaisingEvents = false;
               w.Dispose();
            }
            catch
            {
            }

            Watchers.Remove(packageName);
         }
      }

      private static void EnsureConfigHydratedFromBackupIfEmpty()
      {
         if (_config == null)
         {
            return;
         }

         var needsHydrate = _config.GitUrls == null || _config.GitUrls.Count == 0;
         if (!needsHydrate)
         {
            return;
         }

         var json = EditorPrefs.GetString(PREF_KEY_SNAPSHOT, string.Empty);
         if (string.IsNullOrEmpty(json))
         {
            return;
         }

         var snap = JsonUtility.FromJson<ConfigSnapshot>(json);
         if (snap == null || snap.GitUrls == null || snap.GitUrls.Count == 0)
         {
            return;
         }

         _config.GitUrls = snap.GitUrls.Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(AutoSyncGitPackageConfig.NormalizeGitUrl).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
         _config.WatchPackageCache = snap.WatchPackageCache;
         _config.AutoUpdatePackages = snap.AutoUpdatePackages;

         EditorUtility.SetDirty(_config);
         AssetDatabase.SaveAssets();
         AssetDatabase.Refresh();
      }

      private static void EnsureConfigRemembered()
      {
         if (_config == null)
         {
            return;
         }

         RememberConfig(_config);
      }

      private static void ExpireSuppressions()
      {
         if (SuppressedUntil.Count == 0)
         {
            return;
         }

         var now = EditorApplication.timeSinceStartup;
         var keys = SuppressedUntil.Keys.ToArray();
         foreach (var k in keys)
         {
            if (SuppressedUntil[k] <= now)
            {
               SuppressedUntil.Remove(k);
            }
         }
      }

      private static void OnEditorQuitting()
      {
         if (_config == null)
         {
            return;
         }

         AssetDatabase.SaveAssets();
         AssetDatabase.Refresh();
      }

      private static void PersistNormalizedUrls(AutoSyncGitPackageConfig config, List<string> normalized)
      {
         if (config.GitUrls.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase))
         {
            return;
         }

         config.GitUrls = normalized;
         EditorUtility.SetDirty(config);
         AssetDatabase.SaveAssets();
         AssetDatabase.Refresh();
      }

      private static void PruneStalePackages(List<string> desiredUrls)
      {
         if (_config == null)
         {
            return;
         }

         var desired = new HashSet<string>(
         desiredUrls.Select(AutoSyncGitPackageConfig.NormalizeGitUrl),
         StringComparer.OrdinalIgnoreCase);

         var resolvedSnapshot = _config.Resolved.ToList();

         foreach (var entry in resolvedSnapshot)
         {
            if (string.IsNullOrEmpty(entry?.GitUrl) || string.IsNullOrEmpty(entry?.PackageName))
            {
               continue;
            }

            if (IsSelfPackage(entry.PackageName))
            {
               continue;
            }

            var url = AutoSyncGitPackageConfig.NormalizeGitUrl(entry.GitUrl);

            if (!desired.Contains(url))
            {
               Debug.Log($"AutoSyncGitPackage: Pruning {entry.PackageName} (URL removed from config)");
               QueueRemoveByUrl(url);
            }
         }
      }

      private static void PumpAddJobs()
      {
         if (AddJobs.Count == 0)
         {
            return;
         }

         for (var i = AddJobs.Count - 1; i >= 0; i--)
         {
            var job = AddJobs[i];
            if (!job.Request.IsCompleted)
            {
               continue;
            }

            if (job.Request.Status == StatusCode.Success && job.Request.Result != null)
            {
               var pkg = job.Request.Result;

               _config.UpsertResolved(job.URL, pkg.name, pkg.resolvedPath);
               EditorUtility.SetDirty(_config);
               AssetDatabase.SaveAssets();
               AssetDatabase.Refresh();

               SuppressedUntil[pkg.name] = EditorApplication.timeSinceStartup + SUPPRESS_AFTER_READD_SECONDS;

               if (_config.WatchPackageCache)
               {
                  CreateOrRefreshWatcher(pkg.name, pkg.resolvedPath);
               }

               var deps = GetDependenciesFromInstalledPackageResolvedPath(pkg.resolvedPath);
               if (deps.Length > 0)
               {
                  var changed = false;
                  foreach (var dep in deps)
                  {
                     var normalized = AutoSyncGitPackageConfig.NormalizeGitUrl(dep);
                     if (!_config.GitUrls.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                     {
                        Debug.Log($"AutoSyncGitPackage: Found dependency {normalized} in {pkg.name}");
                        _config.GitUrls.Add(normalized);
                        QueueAdd(normalized);
                        changed = true;
                     }
                  }

                  if (changed)
                  {
                     var normalizedAll = _config.GitUrls.Select(AutoSyncGitPackageConfig.NormalizeGitUrl)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                     PersistNormalizedUrls(_config, normalizedAll);

                     _listRequest ??= Client.List(true);
                  }
               }
            }
            else
            {
               Debug.LogError($"AutoSyncGitPackage: Failed to add {job.URL}: {job.Request.Error?.message}");
            }

            AddJobs.RemoveAt(i);
         }
      }

      private static void PumpAutoSyncScheduler()
      {
         if (!_autoSyncRequested)
         {
            return;
         }

         if (EditorApplication.isCompiling || EditorApplication.isUpdating
             || EditorApplication.isPlayingOrWillChangePlaymode)
         {
            return;
         }

         if (_config != null && _config.GitUrls.Count > 0)
         {
            SyncAll(_config);
            SessionState.SetBool(SESSION_KEY_AUTO_SYNCED, true);
         }

         _autoSyncRequested = false;
      }

      private static void PumpListRequest()
      {
         if (_listRequest == null) return;
         if (!_listRequest.IsCompleted) return;

         if (_listRequest.Status == StatusCode.Success && _listRequest.Result != null && _config != null)
         {
            foreach (var entry in _config.Resolved)
            {
               if (string.IsNullOrEmpty(entry.PackageName)) continue;

               var pkg = _listRequest.Result.FirstOrDefault(p => string.Equals(
               p.name,
               entry.PackageName,
               StringComparison.OrdinalIgnoreCase));
               if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath) && Directory.Exists(pkg.resolvedPath))
               {
                  entry.LastResolvedPath = pkg.resolvedPath;
                  CreateOrRefreshWatcher(entry.PackageName, entry.LastResolvedPath);
               }
            }

            ReconcileResolvedMappings(_listRequest.Result.ToList());

            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
         }
         else if (_listRequest.Status >= StatusCode.Failure)
         {
            Debug.LogWarning(
            $"AutoSyncGitPackage: Failed to list packages for watcher restore: {_listRequest.Error?.message}");
         }

         _listRequest = null;
      }

      private static void PumpRemoveJobs()
      {
         if (RemoveJobs.Count == 0)
         {
            return;
         }

         for (var i = RemoveJobs.Count - 1; i >= 0; i--)
         {
            var job = RemoveJobs[i];
            if (!job.Request.IsCompleted)
            {
               continue;
            }

            if (job.Request.Status == StatusCode.Success)
            {
               if (_config != null && !string.IsNullOrEmpty(job.PackageName))
               {
                  var match = _config.Resolved.FirstOrDefault(r => string.Equals(
                  r.PackageName,
                  job.PackageName,
                  StringComparison.OrdinalIgnoreCase));
                  if (match != null)
                  {
                     _config.Resolved.Remove(match);
                     EditorUtility.SetDirty(_config);
                     AssetDatabase.SaveAssets();
                     AssetDatabase.Refresh();
                  }
               }

               if (!string.IsNullOrEmpty(job.URL))
               {
                  QueueAdd(job.URL);
               }
            }
            else
            {
               Debug.LogError($"AutoSyncGitPackage: Failed to remove {job.PackageName}: {job.Request.Error?.message}");
            }

            RemoveJobs.RemoveAt(i);
         }
      }

      private static void PumpWatcherQueue()
      {
         var now = EditorApplication.timeSinceStartup;

         while (PendingReadd.TryPeek(out _) && PendingReadd.TryDequeue(out var packageName))
         {
            if (SuppressedUntil.TryGetValue(packageName, out var until) && now < until)
            {
               continue;
            }

            if (LastEventAt.TryGetValue(packageName, out var last) && now - last < DEBOUNCE_SECONDS)
            {
               continue;
            }

            LastEventAt[packageName] = now;
            ForceRedownloadByPackageName(packageName);
         }
      }

      private static void QueueAdd(string normalizedUrl)
      {
         var req = Client.Add(normalizedUrl);
         AddJobs.Add(new AddJob {URL = normalizedUrl, Request = req});
      }

      // Removes by URL only if entry is tracked in _config.Resolved (managed). Self is never removed.
      private static void QueueRemoveByUrl(string normalizedUrl, bool force = false)
      {
         if (_config == null)
         {
            return;
         }

         var entry = _config.Resolved.FirstOrDefault(e => string.Equals(
         AutoSyncGitPackageConfig.NormalizeGitUrl(e.GitUrl),
         normalizedUrl,
         StringComparison.OrdinalIgnoreCase));

         if (entry == null || string.IsNullOrEmpty(entry.PackageName))
         {
            return;
         }

         if (IsSelfPackage(entry.PackageName))
         {
            return;
         }

         if (!force && !IsManagedUrl(entry.GitUrl))
         {
            return;
         }

         DisposeWatcherForPackage(entry.PackageName);

         var rem = Client.Remove(entry.PackageName);

         RemoveJobs.Add(new RemoveJob {PackageName = entry.PackageName, URL = null, Request = rem});
      }

      // Removes then re-adds, but only for managed packages and never for self.
      private static void QueueRemoveThenAdd(string packageName, string normalizedUrl)
      {
         if (IsSelfPackage(packageName))
         {
            return;
         }

         if (!IsManagedPackageName(packageName))
         {
            return;
         }

         var rem = Client.Remove(packageName);
         RemoveJobs.Add(new RemoveJob {PackageName = packageName, URL = normalizedUrl, Request = rem});
         SuppressedUntil[packageName] = EditorApplication.timeSinceStartup + SUPPRESS_AFTER_READD_SECONDS;
      }

      private static void StartResolveAllPaths()
      {
         _listRequest = Client.List(true);
      }

      private static void UpdatePump()
      {
         PumpAutoSyncScheduler();
         PumpRemoveJobs();
         PumpAddJobs();
         PumpWatcherQueue();
         PumpListRequest();
         ExpireSuppressions();
      }

      public class UpdateCandidate
      {
         public string CurrentHash;

         public string PackageName;

         public string RemoteHash;

         public string RemoteTag;

         public string Url;
      }

      private class AddJob
      {
         public AddRequest Request;

         public string URL;
      }

      private class RemoveJob
      {
         public RemoveRequest Request;

         public string PackageName;

         public string URL;
      }
   }
}

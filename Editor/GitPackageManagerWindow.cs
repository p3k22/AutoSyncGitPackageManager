#if UNITY_EDITOR
   namespace P3k.AutoSyncGitPackageManager.Editor
   {
      using System;
      using System.Collections.Generic;
      using System.IO;
      using System.Linq;

      using UnityEditor;

      using UnityEngine;

      public class GitPackageManagerWindow : EditorWindow
      {
         private const string PREF_KEY_LEFT_WIDTH_RATIO = "P3k.AutoSyncGitPackage.Window.LeftWidthRatio";

         private const string PREF_KEY_TOP_HEIGHT_RATIO = "P3k.AutoSyncGitPackage.Window.TopHeightRatio";

         private const string PREF_KEY_LEFT_TOP_HEIGHT_RATIO = "P3k.AutoSyncGitPackage.Window.LeftTopHeightRatio";

         private const float DEFAULT_LEFT_WIDTH_RATIO = 0.28f;

         private const float DEFAULT_TOP_HEIGHT_RATIO = 0.55f;

         private const float DEFAULT_LEFT_TOP_HEIGHT_RATIO = 0.70f;

         private const float MIN_LEFT_WIDTH = 260f;

         private const float MIN_RIGHT_WIDTH = 420f;

         private const float MIN_TOP_HEIGHT = 160f;

         private const float MIN_BOTTOM_HEIGHT = 140f;

         private const float MIN_LEFT_TOP = 240f;

         private const float MIN_LEFT_BOTTOM = 140f;

         private const float SPLITTER_THICKNESS = 4f;

         private static readonly (string label, string url)[] BuiltInQuick =
            {
               ("Get P3k's Editor Tools", "git@github.com:p3k22/UnityEditorTools.git"),
               ("Get P3k's Dependency Injector", "git@github.com:p3k22/FeaturesDependencyInjector.git"),
               ("Get P3k's Feature DI Installer", "git@github.com:p3k22/FeaturesDependencyInstaller.git"),
               ("Get P3k's Unity Logger", "git@github.com:p3k22/UnityLogger.git"),
               ("Get P3k's Universal Asset Loader", "git@github.com:p3k22/UniversalAssetLoading.git"),
               ("Get P3k's Commands & Variables", "git@github.com:p3k22/UnityCommandsAndVariables.git")
            };

         private AutoSyncGitPackageConfig _config;

         private bool _draggingVertical, _draggingHorizontal, _draggingLeftInner;

         private float _leftWidthRatio, _topHeightRatio, _leftTopHeightRatio;

         private List<AutoSyncGitPackageService.UpdateCandidate> _pendingUpdates;

         private Vector2 _urlsScroll, _resolvedScroll, _quickAddScroll;

         private void OnEnable()
         {
            _config = FindAnyConfig();

            AutoSyncGitPackageService.Initialize(_config);

            _leftWidthRatio = Mathf.Clamp(
            EditorPrefs.GetFloat(PREF_KEY_LEFT_WIDTH_RATIO, DEFAULT_LEFT_WIDTH_RATIO),
            0.15f,
            0.85f);
            _topHeightRatio = Mathf.Clamp(
            EditorPrefs.GetFloat(PREF_KEY_TOP_HEIGHT_RATIO, DEFAULT_TOP_HEIGHT_RATIO),
            0.20f,
            0.80f);
            _leftTopHeightRatio = Mathf.Clamp(
            EditorPrefs.GetFloat(PREF_KEY_LEFT_TOP_HEIGHT_RATIO, DEFAULT_LEFT_TOP_HEIGHT_RATIO),
            0.20f,
            0.85f);
         }

         private void OnDisable()
         {
            EditorPrefs.SetFloat(PREF_KEY_LEFT_WIDTH_RATIO, _leftWidthRatio);
            EditorPrefs.SetFloat(PREF_KEY_TOP_HEIGHT_RATIO, _topHeightRatio);
            EditorPrefs.SetFloat(PREF_KEY_LEFT_TOP_HEIGHT_RATIO, _leftTopHeightRatio);
            AutoSyncGitPackageService.Shutdown();
         }

         private void OnGUI()
         {
            if (_config == null)
            {
               DrawNoConfigUI(position);
               return;
            }

            var total = new Rect(0, 0, position.width, position.height);
            var leftWidth = Mathf.Clamp(total.width * _leftWidthRatio, MIN_LEFT_WIDTH, total.width - MIN_RIGHT_WIDTH);
            var leftRect = new Rect(total.x, total.y, leftWidth, total.height);
            var vSplit = new Rect(leftRect.xMax, total.y, SPLITTER_THICKNESS, total.height);
            var rightRect = new Rect(vSplit.xMax, total.y, total.width - vSplit.xMax, total.height);

            EditorGUIUtility.AddCursorRect(vSplit, MouseCursor.ResizeHorizontal);
            if (Event.current.type == EventType.MouseDown && vSplit.Contains(Event.current.mousePosition))
            {
               _draggingVertical = true;
               Event.current.Use();
            }

            if (_draggingVertical && Event.current.type == EventType.MouseDrag)
            {
               var mouseX = Mathf.Clamp(Event.current.mousePosition.x, MIN_LEFT_WIDTH, total.width - MIN_RIGHT_WIDTH);
               _leftWidthRatio = Mathf.Clamp(mouseX / total.width, 0.15f, 0.85f);
               Repaint();
            }

            if (Event.current.type == EventType.MouseUp)
            {
               _draggingVertical = false;
            }

            DrawLeftPane(leftRect);
            DrawSplitter(vSplit, false);
            DrawRightPane(rightRect);
         }

         [MenuItem("Window/P3k's Git Package Manager")]
         public static void Open()
         {
            var win = GetWindow<GitPackageManagerWindow>(true, "P3k Git Package Manager (GPM)");
            win.minSize = new Vector2(900, 500);
            win.Show();
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

         private void DrawLeftPane(Rect rect)
         {
            var topHeight = Mathf.Clamp(rect.height * _leftTopHeightRatio, MIN_LEFT_TOP, rect.height - MIN_LEFT_BOTTOM);
            var topRect = new Rect(rect.x, rect.y, rect.width, topHeight);
            var hSplit = new Rect(rect.x, topRect.yMax, rect.width, SPLITTER_THICKNESS);
            var botRect = new Rect(rect.x, hSplit.yMax, rect.width, rect.height - hSplit.height - topRect.height);

            EditorGUIUtility.AddCursorRect(hSplit, MouseCursor.ResizeVertical);
            if (Event.current.type == EventType.MouseDown && hSplit.Contains(Event.current.mousePosition))
            {
               _draggingLeftInner = true;
               Event.current.Use();
            }

            if (_draggingLeftInner && Event.current.type == EventType.MouseDrag)
            {
               var mouseY = Mathf.Clamp(
               Event.current.mousePosition.y - rect.y,
               MIN_LEFT_TOP,
               rect.height - MIN_LEFT_BOTTOM);
               _leftTopHeightRatio = Mathf.Clamp(mouseY / rect.height, 0.20f, 0.85f);
               Repaint();
            }

            if (Event.current.type == EventType.MouseUp)
            {
               _draggingLeftInner = false;
            }

            DrawLeftTopPane(topRect);
            DrawSplitter(hSplit, true);
            DrawLeftBottomQuickAdd(botRect);
         }

         private void DrawLeftTopPane(Rect rect)
         {
            GUILayout.BeginArea(rect);
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);

            var newConfig = (AutoSyncGitPackageConfig) EditorGUILayout.ObjectField(
            _config,
            typeof(AutoSyncGitPackageConfig),
            false);
            if (newConfig != _config && newConfig != null)
            {
               _config = newConfig;
               AutoSyncGitPackageService.RememberConfig(_config);
               AutoSyncGitPackageService.Initialize(_config);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create New", GUILayout.Height(24)))
            {
               _config = CreateConfigAsset("Assets/AutoSyncGitPackageManager/GitPackageConfig.asset");
               AutoSyncGitPackageService.RememberConfig(_config);
               AutoSyncGitPackageService.Initialize(_config);
               Repaint();
            }

            if (GUILayout.Button("Find Existing", GUILayout.Height(24)))
            {
               var path = EditorUtility.OpenFilePanel("Select GitPackageConfig", "Assets", "asset");
               if (!string.IsNullOrEmpty(path))
               {
                  var rel = ToProjectRelativePath(path);
                  _config = AssetDatabase.LoadAssetAtPath<AutoSyncGitPackageConfig>(rel);
                  if (_config)
                  {
                     AutoSyncGitPackageService.RememberConfig(_config);
                     AutoSyncGitPackageService.Initialize(_config);
                     Repaint();
                  }
               }
            }

            if (!_config)
            {
               EditorGUILayout.EndHorizontal();
               GUILayout.EndArea();
               return;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);

            var newWatch = EditorGUILayout.ToggleLeft(
            new GUIContent(
            "Watch Package Cache For .cs Changes",
            "Watch Library/PackageCache for C# edits and revert back immediately if any detected (enforcing ReadOnly)."),
            _config.WatchPackageCache);

            var newUpdate = EditorGUILayout.ToggleLeft(
            new GUIContent(
            "Auto Update Packages (on Editor load only)",
            "On the next Unity Editor load, update all mapped packages to latest remote without prompting."),
            _config.AutoUpdatePackages);

            if (newWatch != _config.WatchPackageCache || newUpdate != _config.AutoUpdatePackages)
            {
               _config.WatchPackageCache = newWatch;
               _config.AutoUpdatePackages = newUpdate;
               EditorUtility.SetDirty(_config);
               AssetDatabase.SaveAssets();
               AutoSyncGitPackageService.Initialize(_config);
            }

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Sync Now", GUILayout.Height(26)))
            {
               SyncKnownOnly();
            }

            if (GUILayout.Button("Refresh Watchers", GUILayout.Height(24)))
            {
               AutoSyncGitPackageService.Initialize(_config);
               AutoSyncGitPackageService.RefreshWatchers();
            }

            if (GUILayout.Button("Check For Package Updates", GUILayout.Height(24)))
            {
               var candidates = AutoSyncGitPackageService.CheckForGitUpdates(_config);
               if (candidates == null || candidates.Count == 0)
               {
                  _pendingUpdates = null;
                  EditorUtility.DisplayDialog("Git Package Updates", "All packages are up to date.", "OK");
               }
               else
               {
                  _pendingUpdates = candidates;
                  Repaint();

                  var count = candidates.Count;
                  EditorApplication.delayCall += () =>
                     {
                        if (this == null)
                        {
                           return;
                        }

                        var ok = EditorUtility.DisplayDialog(
                        "Updates Available",
                        $"{count} update{(count == 1 ? "" : "s")} found. Update all now?",
                        "Update All",
                        "Cancel");
                        if (ok)
                        {
                           AutoSyncGitPackageService.UpdateSpecificUrls(candidates.Select(c => c.Url));
                           _pendingUpdates = null;
                           Repaint();
                        }
                     };
               }
            }

            if (GUI.Button(new Rect(4, (rect.y + rect.height) - 28, rect.width - 8, 24), "Check For GPM Updates"))
            {
               SelfUpdateUtility.CheckForSelfUpdate();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
         }

         private void DrawLeftBottomQuickAdd(Rect rect)
         {
            GUILayout.BeginArea(rect);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Quick Add Packages", EditorStyles.boldLabel);

            _quickAddScroll = EditorGUILayout.BeginScrollView(_quickAddScroll, GUILayout.ExpandHeight(true));

            if (BuiltInQuick.Length > 0)
            {
               foreach (var (label, url) in BuiltInQuick)
               {
                  if (GUILayout.Button(label, GUILayout.Height(22)))
                  {
                     AddUrlAndSync(url);
                  }
               }

               EditorGUILayout.Space(6);
            }

            EditorGUILayout.EndScrollView();

            GUILayout.EndArea();
         }

         private void DrawRightPane(Rect rect)
         {
            var topHeight = Mathf.Clamp(rect.height * _topHeightRatio, MIN_TOP_HEIGHT, rect.height - MIN_BOTTOM_HEIGHT);
            var topRect = new Rect(rect.x, rect.y, rect.width, topHeight);
            var hSplit = new Rect(rect.x, topRect.yMax, rect.width, SPLITTER_THICKNESS);
            var bottomRect = new Rect(rect.x, hSplit.yMax, rect.width, rect.height - hSplit.height - topRect.height);

            EditorGUIUtility.AddCursorRect(hSplit, MouseCursor.ResizeVertical);
            if (Event.current.type == EventType.MouseDown && hSplit.Contains(Event.current.mousePosition))
            {
               _draggingHorizontal = true;
               Event.current.Use();
            }

            if (_draggingHorizontal && Event.current.type == EventType.MouseDrag)
            {
               var mouseY = Mathf.Clamp(
               Event.current.mousePosition.y - rect.y,
               MIN_TOP_HEIGHT,
               rect.height - MIN_BOTTOM_HEIGHT);
               _topHeightRatio = Mathf.Clamp(mouseY / rect.height, 0.20f, 0.80f);
               Repaint();
            }

            if (Event.current.type == EventType.MouseUp)
            {
               _draggingHorizontal = false;
            }

            DrawUrlsPane(topRect);
            DrawSplitter(hSplit, true);
            DrawResolvedPane(bottomRect);
         }

         private void DrawUrlsPane(Rect rect)
         {
            GUILayout.BeginArea(rect);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Git URLs", EditorStyles.boldLabel);

            _urlsScroll = EditorGUILayout.BeginScrollView(_urlsScroll, GUILayout.ExpandHeight(true));

            for (var i = 0; i < _config.GitUrls.Count; i++)
            {
               EditorGUILayout.BeginHorizontal();

               var current = _config.GitUrls[i];
               var updated = EditorGUILayout.TextField(current);

               if (!string.Equals(updated, current, StringComparison.Ordinal))
               {
                  var normalized = AutoSyncGitPackageConfig.NormalizeGitUrl(updated);
                  _config.GitUrls[i] = normalized;
                  EditorUtility.SetDirty(_config);
                  AssetDatabase.SaveAssets();
               }

               if (AutoSyncGitPackageService.IsBusy)
               {
                  GUI.enabled = false;
               }

               if (GUILayout.Button("X", GUILayout.Width(24)))
               {
                  var normToRemove = AutoSyncGitPackageConfig.NormalizeGitUrl(current);
                  AutoSyncGitPackageService.RemoveAndUninstallUrl(normToRemove);

                  EditorUtility.SetDirty(_config);
                  AssetDatabase.SaveAssets();
                  EditorGUILayout.EndHorizontal();
                  break;
               }

               GUI.enabled = true;

               if (_pendingUpdates != null)
               {
                  var norm = AutoSyncGitPackageConfig.NormalizeGitUrl(current);
                  var cand = _pendingUpdates.FirstOrDefault(c => string.Equals(
                  c.Url,
                  norm,
                  StringComparison.OrdinalIgnoreCase));
                  if (cand != null)
                  {
                     var label = !string.IsNullOrEmpty(cand.RemoteTag) ? cand.RemoteTag : ShortHash(cand.RemoteHash);
                     var tip = $"Update to new version ({label})";

                     var content = EditorGUIUtility.IconContent("Refresh");
                     if (content == null || content.image == null)
                     {
                        content = new GUIContent("↻", tip);
                     }
                     else
                     {
                        content.tooltip = tip;
                     }

                     if (GUILayout.Button(content, GUILayout.Width(24)))
                     {
                        var confirm = EditorUtility.DisplayDialog(
                        "Update Package",
                        $"Update '{cand.PackageName}' to new version ({label})?",
                        "Update",
                        "Cancel");
                        if (confirm)
                        {
                           AutoSyncGitPackageService.UpdateSpecificUrls(new[] {cand.Url});
                           _pendingUpdates.Remove(cand);
                           if (_pendingUpdates.Count == 0)
                           {
                              _pendingUpdates = null;
                           }

                           Repaint();
                        }
                     }
                  }
               }

               EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add URL", GUILayout.Height(22)))
            {
               _config.GitUrls.Add(string.Empty);
               EditorUtility.SetDirty(_config);
               AssetDatabase.SaveAssets();
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.EndArea();
         }

         private static string ShortHash(string h)
         {
            if (string.IsNullOrEmpty(h))
            {
               return "?";
            }

            return h.Length <= 7 ? h : h.Substring(0, 7);
         }

         private void DrawResolvedPane(Rect rect)
         {
            GUILayout.BeginArea(rect);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Resolved Mapping", EditorStyles.boldLabel);

            if (_config.Resolved.Count == 0)
            {
               EditorGUILayout.HelpBox("After syncing, resolved package mappings appear here.", MessageType.Info);
               GUILayout.EndArea();
               return;
            }

            _resolvedScroll = EditorGUILayout.BeginScrollView(_resolvedScroll, GUILayout.ExpandHeight(true));

            foreach (var entry in _config.Resolved)
            {
               if (IsSelf(entry.PackageName))
               {
                  continue;
               }

               EditorGUILayout.BeginVertical("box");
               EditorGUILayout.LabelField("Git URL:", EditorStyles.miniBoldLabel);
               EditorGUILayout.SelectableLabel(entry.GitUrl, GUILayout.Height(16));

               EditorGUILayout.LabelField("Package Name:", EditorStyles.miniBoldLabel);
               EditorGUILayout.SelectableLabel(
               string.IsNullOrEmpty(entry.PackageName) ? "(unknown)" : entry.PackageName,
               GUILayout.Height(16));

               EditorGUILayout.LabelField("Resolved Path:", EditorStyles.miniBoldLabel);
               EditorGUILayout.SelectableLabel(
               string.IsNullOrEmpty(entry.LastResolvedPath) ? "(unknown)" : entry.LastResolvedPath,
               GUILayout.Height(16));

               EditorGUILayout.BeginHorizontal();
               if (!string.IsNullOrEmpty(entry.PackageName))
               {
                  if (GUILayout.Button("Re-download", GUILayout.Width(110)))
                  {
                     AutoSyncGitPackageService.ForceRedownloadByPackageName(entry.PackageName);
                  }
               }

               EditorGUILayout.EndHorizontal();

               EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
         }

         private static void DrawSplitter(Rect rect, bool horizontal)
         {
            var old = GUI.color;
            GUI.color = EditorGUIUtility.isProSkin ? new Color(1, 1, 1, 0.08f) : new Color(0, 0, 0, 0.08f);
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
            GUI.color = old;

            if (horizontal)
            {
               var line = new Rect(rect.x, rect.center.y, rect.width, 1);
               EditorGUI.DrawRect(line, new Color(0, 0, 0, EditorGUIUtility.isProSkin ? 0.3f : 0.45f));
            }
            else
            {
               var line = new Rect(rect.center.x, rect.y, 1, rect.height);
               EditorGUI.DrawRect(line, new Color(0, 0, 0, EditorGUIUtility.isProSkin ? 0.3f : 0.45f));
            }
         }

         private void AddUrlAndSync(string rawUrl)
         {
            var normalized = AutoSyncGitPackageConfig.NormalizeGitUrl(rawUrl);
            if (!_config.GitUrls.Any(u => string.Equals(u, normalized, StringComparison.OrdinalIgnoreCase)))
            {
               _config.GitUrls.Add(normalized);
               EditorUtility.SetDirty(_config);
               AssetDatabase.SaveAssets();
            }

            AutoSyncGitPackageService.SyncAll(_config);
         }

         private void DrawNoConfigUI(Rect rect)
         {
            GUILayout.BeginArea(new Rect(10, 10, 200, 200));
            GUILayout.FlexibleSpace();
            EditorGUILayout.HelpBox(
            "No AutoSyncGitPackageConfig found. Click the button below to create and initialize one.",
            MessageType.Info);
            if (GUILayout.Button("Create Config", GUILayout.Height(26), GUILayout.Width(180)))
            {
               var cfg = CreateConfigAsset("Assets/AutoSyncGitPackageManager/GitPackageConfig.asset");
               AutoSyncGitPackageService.RememberConfig(cfg);
               AutoSyncGitPackageService.Initialize(cfg);

               _config = cfg;
               Repaint();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
         }

         private static AutoSyncGitPackageConfig CreateConfigAsset(string path)
         {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
               Directory.CreateDirectory(dir);
            }

            var unique = AssetDatabase.GenerateUniqueAssetPath(path);
            var asset = CreateInstance<AutoSyncGitPackageConfig>();
            AssetDatabase.CreateAsset(asset, unique);
            AssetDatabase.SaveAssets();

            Selection.activeObject = asset;
            return asset;
         }

         private static string ToProjectRelativePath(string absolutePath)
         {
            absolutePath = absolutePath.Replace('\\', '/');
            var proj = Application.dataPath.Replace("Assets", string.Empty).Replace('\\', '/');
            if (absolutePath.StartsWith(proj, StringComparison.OrdinalIgnoreCase))
            {
               return absolutePath.Substring(proj.Length);
            }

            return absolutePath;
         }

         private void SyncKnownOnly()
         {
            if (_config == null)
            {
               return;
            }

            var originalAutoUpdate = _config.AutoUpdatePackages;

            try
            {
               if (originalAutoUpdate)
               {
                  _config.AutoUpdatePackages = false;
                  EditorUtility.SetDirty(_config);
                  AssetDatabase.SaveAssets();
               }

               AutoSyncGitPackageService.SyncAll(_config);
            }
            finally
            {
               if (originalAutoUpdate)
               {
                  _config.AutoUpdatePackages = true;
                  EditorUtility.SetDirty(_config);
                  AssetDatabase.SaveAssets();
               }
            }
         }

         private static bool IsSelf(string packageName)
         {
            if (string.IsNullOrEmpty(packageName))
            {
               return false;
            }

            var core = packageName.Split('@')[0];
            return string.Equals(core, "com.p3k.autosyncgitpackagemanager", StringComparison.OrdinalIgnoreCase);
         }

         [Serializable]
         private class QuickItem
         {
            public string Label;

            public string Url;
         }

         [Serializable]
         private class QuickList
         {
            public List<QuickItem> Items = new List<QuickItem>();
         }
      }
   }
#endif
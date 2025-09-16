namespace P3k.GitPackageManager
{
   using System;
   using System.Collections.Generic;
   using System.IO;
   using System.Linq;
   using System.Text.RegularExpressions;

   using UnityEditor;
   using UnityEditor.PackageManager;
   using UnityEditor.PackageManager.Requests;

   using UnityEngine;

   using PackageInfo = UnityEditor.PackageManager.PackageInfo;

   public sealed class GPMWindow : EditorWindow
   {
      private const string INPUT_CONTROL = "PkgInputField";

      private readonly List<PackageInfo> _installed = new();

      private readonly Queue<string> _pendingAdds = new();

      private readonly Queue<string> _pendingRemoves = new();

      private readonly HashSet<string> _seenAdds = new(StringComparer.OrdinalIgnoreCase);

      private AddRequest _addRequest;

      private string _inputLink = string.Empty;

      private bool _isBusy;

      private ListRequest _listRequest;

      private RemoveRequest _removeRequest;

      private Vector2 _scroll;

      private SearchRequest _searchRequest;

      private string _status = "";

      private string _updateCheckInstalledVersion;

      private string _updateCheckPackageName;

      private void OnEnable()
      {
         EditorApplication.update += OnEditorUpdate;
         RequestList(true);
      }

      private void OnDisable()
      {
         EditorApplication.update -= OnEditorUpdate;
         ClearProgressBar();
      }

      private void OnGUI()
      {
         minSize = new Vector2(1120, 440);

         DrawHeader();
         EditorGUILayout.Space(6);

         using (new EditorGUILayout.HorizontalScope())
         {
            EditorGUI.BeginDisabledGroup(_isBusy);
            GUI.SetNextControlName(INPUT_CONTROL);
            _inputLink = EditorGUILayout.TextField(
            new GUIContent("Package URL:", "e.g. git@github.com:org/repo.git"),
            _inputLink);

            if (GUILayout.Button(new GUIContent("Add", "Install the above link"), GUILayout.Width(80)))
            {
               OnSyncClicked();
            }

            EditorGUI.EndDisabledGroup();
         }

         EditorGUILayout.Space(8);
         DrawInstalledInfoBox();
         EditorGUILayout.Space(6);
         EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
      }

      [MenuItem("Window/P3k's Git Package Manager")]
      private static void Open()
      {
         var win = GetWindow<GPMWindow>("P3k's Git Package Manager");
         win.Show();
      }

      private static void DrawHeader()
      {
         var rect = EditorGUILayout.GetControlRect(false, 30f);
         EditorGUI.LabelField(rect, "Package Links", EditorStyles.boldLabel);
      }

      private void DrawInstalledInfoBox()
      {
         EditorGUILayout.LabelField("Installed Packages", EditorStyles.boldLabel);
         using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
         {
            var height = Mathf.Clamp(position.height - 200f, 160f, 1200f);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(height));

            if (_installed.Count == 0)
            {
               EditorGUILayout.LabelField("No packages found or list not loaded.", EditorStyles.miniLabel);
            }
            else
            {
               DrawInstalledHeaderRow();
               EditorGUILayout.Space(2);

               foreach (var p in _installed.OrderBy(pi => pi.name, StringComparer.OrdinalIgnoreCase))
               {
                  DrawInstalledRow(p);
               }
            }

            EditorGUILayout.EndScrollView();
         }
      }

      private static void DrawInstalledHeaderRow()
      {
         using (new EditorGUILayout.HorizontalScope())
         {
            GUILayout.Label("", GUILayout.Width(24)); /* update */
            GUILayout.Label("", GUILayout.Width(24)); /* remove */
            GUILayout.Label("Name", GUILayout.Width(260));
            GUILayout.Label("Version", GUILayout.Width(120));
            GUILayout.Label("Source", GUILayout.Width(90));
            GUILayout.Label("Package Id", GUILayout.ExpandWidth(true));
         }

         var r = EditorGUILayout.GetControlRect(false, 1);
         EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.2f));
      }

      private void DrawInstalledRow(PackageInfo p)
      {
         using (new EditorGUILayout.HorizontalScope())
         {
            EditorGUI.BeginDisabledGroup(_isBusy);

            var refreshIcon = EditorGUIUtility.IconContent("Refresh");
            if (GUILayout.Button(
                new GUIContent(refreshIcon.image, "Check for updates"),
                GUILayout.Width(22),
                GUILayout.Height(18)))
            {
               OnCheckUpdatesClicked(p);
            }

            if (GUILayout.Button(new GUIContent("X", "Uninstall this package"), GUILayout.Width(22)))
            {
               EnqueueRemove(p.name);
            }

            EditorGUI.EndDisabledGroup();

            GUILayout.Label(p.name ?? "-", GUILayout.Width(260));
            GUILayout.Label(p.version ?? "-", GUILayout.Width(120));
            GUILayout.Label(p.source.ToString(), GUILayout.Width(90));
            GUILayout.Label(p.packageId ?? "-", GUILayout.ExpandWidth(true));
         }
      }

      private void OnSyncClicked()
      {
         var link = (_inputLink ?? string.Empty).Trim();
         if (string.IsNullOrEmpty(link))
         {
            return;
         }

         EnqueueAdd(link);
         _inputLink = string.Empty;
         GUI.FocusControl(null);
         Repaint();
      }

      private void EnqueueAdd(string link)
      {
         if (string.IsNullOrWhiteSpace(link))
         {
            return;
         }

         if (!_seenAdds.Add(link))
         {
            return;
         }

         _pendingAdds.Enqueue(link);
      }

      private void EnqueueRemove(string idOrName)
      {
         if (string.IsNullOrWhiteSpace(idOrName))
         {
            return;
         }

         _pendingRemoves.Enqueue(idOrName);
      }

      private void OnCheckUpdatesClicked(PackageInfo p)
      {
         if (p == null)
         {
            return;
         }

         if (p.source == PackageSource.Registry)
         {
            _updateCheckPackageName = p.name;
            _updateCheckInstalledVersion = p.version;
            StartSearch(p.name);
            return;
         }

         if (p.source == PackageSource.Git)
         {
            var src = ExtractGitSourceFromPackageId(p.packageId);
            if (string.IsNullOrEmpty(src))
            {
               EditorUtility.DisplayDialog("Update", $"No git source could be derived for {p.name}.", "OK");
               return;
            }

            if (EditorUtility.DisplayDialog(
                "Update from Git",
                $"Reinstall from:\n{src}\n\nUnity will fetch the latest commit for the specified branch or tag.",
                "Update",
                "Cancel"))
            {
               EnqueueAdd(src);
            }

            return;
         }

         EditorUtility.DisplayDialog("Update", $"Update check not supported for source: {p.source}", "OK");
      }

      private void OnEditorUpdate()
      {
         if (_listRequest is {IsCompleted: true})
         {
            if (_listRequest.Status == StatusCode.Success && _listRequest.Result != null)
            {
               _installed.Clear();
               _installed.AddRange(_listRequest.Result);
               _status = $"Loaded {_installed.Count} installed package(s).";
            }
            else
            {
               _status = $"Package list failed: {_listRequest.Error?.message}";
            }

            _listRequest = null;
            _isBusy = false;
            ClearProgressBar();
            Repaint();
            return;
         }

         if (_searchRequest is {IsCompleted: true})
         {
            if (_searchRequest.Status == StatusCode.Success && _searchRequest.Result != null)
            {
               var match = _searchRequest.Result.FirstOrDefault(pi => string.Equals(
               pi.name,
               _updateCheckPackageName,
               StringComparison.OrdinalIgnoreCase));
               var latest = match?.versions?.latestCompatible ?? match?.versions?.latest;
               var have = _updateCheckInstalledVersion;

               if (!string.IsNullOrEmpty(latest) && !string.Equals(latest, have, StringComparison.OrdinalIgnoreCase))
               {
                  var ok = EditorUtility.DisplayDialog(
                  "Update Available",
                  $"{_updateCheckPackageName}\nInstalled: {have}\nLatest:    {latest}\n\nUpdate now?",
                  "Update",
                  "Cancel");

                  if (ok)
                  {
                     EnqueueAdd($"{_updateCheckPackageName}@{latest}");
                  }
                  else
                  {
                     _status = "Update canceled.";
                  }
               }
               else
               {
                  _status = "No update found.";
               }
            }
            else
            {
               _status = $"Search failed: {_searchRequest.Error?.message}";
            }

            _searchRequest = null;
            _isBusy = false;
            ClearProgressBar();
            Repaint();
            return;
         }

         if (_addRequest is {IsCompleted: true})
         {
            if (_addRequest.Status == StatusCode.Success)
            {
               var pkg = _addRequest.Result;
               _status = $"Installed: {pkg?.name} {pkg?.version}";
               TryDiscoverAndEnqueueGitDependencies(pkg);
               RequestList(true);
            }
            else
            {
               _status = $"Add failed: {_addRequest.Error?.message}";
            }

            _addRequest = null;
            _isBusy = false;
            ClearProgressBar();
            Repaint();
            return;
         }

         if (_removeRequest is {IsCompleted: true})
         {
            if (_removeRequest.Status == StatusCode.Success)
            {
               var removedId = _removeRequest.PackageIdOrName;
               _status = $"Removed: {removedId}";
               RequestList(true);
            }
            else
            {
               _status = $"Remove failed: {_removeRequest.Error?.message}";
            }

            _removeRequest = null;
            _isBusy = false;
            ClearProgressBar();
            Repaint();
            return;
         }

         if (_addRequest == null && _removeRequest == null && _searchRequest == null)
         {
            if (_pendingAdds.Count > 0)
            {
               var link = _pendingAdds.Dequeue();
               StartAdd(link);
               return;
            }

            if (_pendingRemoves.Count > 0)
            {
               var id = _pendingRemoves.Dequeue();
               StartRemove(id);
            }
         }
      }

      private void StartAdd(string link)
      {
         _isBusy = true;
         _status = $"Adding {link}";
         EditorUtility.DisplayProgressBar("UPM", _status, 0.3f);
         _addRequest = Client.Add(link);
      }

      private void StartRemove(string idOrName)
      {
         _isBusy = true;
         _status = $"Removing {idOrName}";
         EditorUtility.DisplayProgressBar("UPM", _status, 0.3f);
         _removeRequest = Client.Remove(idOrName);
      }

      private void RequestList(bool refresh)
      {
         _isBusy = true;
         _status = "Refreshing installed packages…";
         EditorUtility.DisplayProgressBar("UPM", _status, 0.1f);
         _listRequest = Client.List(refresh);
      }

      private void StartSearch(string packageName)
      {
         _isBusy = true;
         _status = $"Checking updates for {packageName}…";
         EditorUtility.DisplayProgressBar("UPM", _status, 0.2f);
         _searchRequest = Client.Search(packageName);
      }

      private void TryDiscoverAndEnqueueGitDependencies(PackageInfo pkg)
      {
         if (pkg == null)
         {
            return;
         }

         var path = pkg.resolvedPath;
         if (string.IsNullOrEmpty(path))
         {
            return;
         }

         var jsonFile = Path.Combine(path, "package.json");
         if (!File.Exists(jsonFile))
         {
            return;
         }

         string jsonText;
         try
         {
            jsonText = File.ReadAllText(jsonFile);
         }
         catch
         {
            return;
         }

         var deps = ExtractGitDependencies(jsonText);
         if (deps == null || deps.Count == 0)
         {
            return;
         }

         foreach (var dep in deps)
         {
            var trimmed = (dep ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
               continue;
            }

            if (_seenAdds.Contains(trimmed))
            {
               continue;
            }

            _seenAdds.Add(trimmed);
            _pendingAdds.Enqueue(trimmed);
         }

         Debug.Log($"Discovered {deps.Count} gitdependencies in {pkg.name}");
      }

      private static List<string> ExtractGitDependencies(string json)
      {
         if (string.IsNullOrEmpty(json))
         {
            return null;
         }

         var arrayMatch = Regex.Match(json, "\"gitdependencies\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
         if (arrayMatch.Success)
         {
            var inside = arrayMatch.Groups[1].Value;
            return SplitCsvRespectingQuotes(inside).Select(t => TrimJsonStringToken(t))
               .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
         }

         var strMatch = Regex.Match(json, "\"gitdependencies\"\\s*:\\s*\"(.*?)\"", RegexOptions.Singleline);
         if (strMatch.Success)
         {
            var inside = strMatch.Groups[1].Value;
            return SplitCsvRespectingQuotes(inside).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s))
               .ToList();
         }

         return null;
      }

      private static IEnumerable<string> SplitCsvRespectingQuotes(string s)
      {
         if (string.IsNullOrEmpty(s))
         {
            yield break;
         }

         var inQuotes = false;
         var start = 0;
         for (var i = 0; i < s.Length; i++)
         {
            var c = s[i];
            switch (c)
            {
               case '"':
                  inQuotes = !inQuotes;
                  break;
               case ',' when !inQuotes:
                  yield return s.Substring(start, i - start);
                  start = i + 1;
                  break;
            }
         }

         if (start <= s.Length)
         {
            yield return s[start..];
         }
      }

      private static string TrimJsonStringToken(string token)
      {
         if (token == null)
         {
            return null;
         }

         var t = token.Trim();
         if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
         {
            t = t.Substring(1, t.Length - 2);
         }

         return t.Trim();
      }

      private static string ExtractGitSourceFromPackageId(string packageId)
      {
         if (string.IsNullOrWhiteSpace(packageId))
         {
            return null;
         }

         var at = packageId.IndexOf('@');
         if (at < 0)
         {
            return null;
         }

         var src = packageId[(at + 1)..];
         var hash = src.IndexOf('#');
         if (hash >= 0)
         {
            src = src[..hash];
         }

         return src;
      }

      private static void ClearProgressBar()
      {
         try
         {
            EditorUtility.ClearProgressBar();
         }
         catch
         {
         }
      }
   }
}
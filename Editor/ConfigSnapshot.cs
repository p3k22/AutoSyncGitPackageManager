#if UNITY_EDITOR
namespace P3k.AutoSyncGitPackageManager.Editor
   {
      using System;
      using System.Collections.Generic;
      using System.Linq;

      [Serializable]
      public class ConfigSnapshot
      {
      public List<string> GitUrls = new List<string>();

      public bool WatchPackageCache = true;

      public bool AutoSyncOnLoad;

      public bool AutoUpdatePackages;
   }
}
#endif

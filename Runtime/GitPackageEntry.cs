namespace P3k.AutoSyncGitPackageManager
{
   using System;
   using System.Linq;

   /// <summary>
   /// Data for one Git-sourced package.
   /// </summary>
   [Serializable]
   public record GitPackageEntry
   {
      public string GitUrl;

      public string PackageName;

      public string LastResolvedPath;
   }
}

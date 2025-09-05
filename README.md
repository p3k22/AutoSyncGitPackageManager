# Auto Sync Git Package for Unity

A Unity Editor extension that manages **Git-based Unity packages** directly from the editor.

---

## Features

* **Git URL Management** – Add, edit, and remove Git repository URLs.
* **Update Checking** – Detect newer commits/tags and prompt for updates.
* **Watchers** – Monitor package cache folders for source changes and reloads when triggered.
* **Quick Add Panel** – Built-in and custom shortcuts to quickly add common packages.
* **Transitive Git Dependencies** – Packages can declare extra Git repos via a `gitdependencies` section in their `package.json`; these are auto-detected and pulled in during sync.

---

## Installation

Add via git url in Unity Package Manager or add manually to your Unity project's `manifest.json`:

```json
{
  "dependencies": {
    "com.p3k.autosyncgitpackagemanager": "https://github.com/p3k22/AutoSyncGitPackageManager.git"
  }
}
```

**Option 2:** Copy the Runtime & Editor folders into your project. If you're using assembly definitions, add a reference to the runtime package asmdef. If not using asmdefs, delete the included editor and runtime asmdef files.

---

## Usage

### 1. Open the Window

Go to `Window → P3k's Git Package Manager`

### 2. Create or Find a Config

* **Create New** – Generates a new `GitPackageConfig.asset` file.
* **Find Existing** – Select a previously saved config file.

### 3. Add Git URLs

Paste Git repository links (SSH or HTTPS) into the list. They will be normalized and stored in the config.

### 4. Sync Packages

Click **Sync Now** to fetch all configured Git URLs into Unity’s Package Manager.

### 5. Resolved Mapping

After syncing, view each package’s Git URL, package name (from `package.json`), and resolved cache path. Use the **Re-download** button to force refresh.

### 6. Quick Add Panel

Use built-in or custom shortcuts to add packages faster. Add custom entries with the **+** button.

### 7. Auto Options

* **Auto Sync on Load** – Syncs all packages when Unity starts.
* **Watch Package Cache** – Monitors `.cs` changes inside package cache and re-downloads as needed.
* **Auto Update Packages** – Checks remote repos for new commits/tags on load or sync.

---

## Git Dependencies (`gitdependencies`)

### What it is

A Git-sourced package can declare extra Git repos it depends on by adding a `gitdependencies` object to its `package.json`. During sync, the manager scans installed packages, reads their `package.json`, and if it finds `gitdependencies`, it pulls each listed Git URL as well. The implementation parses `package.json` and collects the **values** of the `gitdependencies` object (the keys are treated as labels only).

### Example `package.json` in a Git package

```json
{
  "name": "com.yourorg.feature",
  "version": "1.2.3",
  "displayName": "Your Feature",
  "gitdependencies": {
    "logger": "git@github.com:p3k22/UnityLogger.git",
    "utils": "https://github.com/yourorg/unity-utils.git"
  }
}
```

Notes:

* Keys like `"logger"` and `"utils"` are labels. Only the **values** (Git URLs) are used by the manager when resolving dependencies.
* Both SSH (`git@github.com:…`) and HTTPS (`https://github.com/…`) are supported; the tool normalizes URLs before use.

### How it works in this tool

1. When a package is installed, the manager locates its `package.json` in the package cache.
2. It reads the `gitdependencies` object and extracts each value as a Git URL.
3. Those Git URLs are then treated the same as top-level entries you added to the tool, and they are synced via UPM.

This provides **transitive Git dependency** support that Unity’s built-in UPM dependency system doesn’t offer for Git URLs inside `package.json`.

### Workflow

* Add your primary Git URLs in the manager (or via Quick Add).
* Ensure dependent packages include a `gitdependencies` object in their `package.json` listing additional Git URLs.
* Run **Sync Now** (or rely on **Auto Sync on Load**). The manager pulls both the primary Git URLs and any `gitdependencies` it discovers.

### Troubleshooting

* **`gitdependencies` not picked up**:
  Ensure the package is installed and its `package.json` truly contains a top-level `gitdependencies` object. Property name is all lowercase as shown. The manager reads and parses that object and uses the values only.
* **URL format**:
  Both SSH and HTTPS are fine; invalid or empty values are ignored. URLs are normalized to a canonical form before use.
* **Conflicts**:
  If two packages declare the same Git URL, the normalization step de-duplicates based on URL equality.

---

## Notes

* Each Git repo must define a unique `name` field in its `package.json`. Unity uses this name to resolve packages; duplicates will conflict.
* Tags in your Git repo (e.g. `v1.0.1`) will be shown in the update UI instead of raw commit hashes when available.
* Declaring `gitdependencies` in a package is optional but recommended when your package needs other Git-based packages.

---

## License

MIT License

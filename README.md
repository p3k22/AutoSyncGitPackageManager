# P3k's Git Package Manager

Minimal Editor window to install, update, and remove Unity packages.

## Open

* **Window → P3k's Git Package Manager**

## Add a Unity Package 

1. Paste a link in **Package URL**:

   * Registry: `com.company.package` or `com.company.package@1.2.3`
   * Git HTTPS: `https://github.com/org/repo.git#v1.0.0`
   * Git SSH: `git@github.com:org/repo.git#main`
2. Click **Add**. 


## Auto `gitdependencies`

* After a package installs, its `package.json` is scanned for `gitdependencies`.
* Supported:

  * Array: `"gitdependencies": ["https://...git#tag", "git@github.com:org/repo.git#main"]`
  * CSV string: `"gitdependencies": "https://...git#tag, git@github.com:org/repo.git#main"`
* Found links are queued and installed. Duplicates are skipped for this session.


## List

* Shows installed packages: Name, Version, Source, Package Id.
* Buttons per row:

  * **⟳** Check for updates
  * **X** Uninstall

## Update

* **Registry**: Finds latest compatible. If newer, click **Update** in the dialog.
* **Git**: Re-adds from the repo URL (without pinned commit). Confirms before updating.

## Remove

* Click **X** next to the package. List auto-refreshes.


## Troubleshooting

* **Add failed**: Check status line and Console for the Package Manager error.
* **Git private repos**: Ensure SSH keys or credentials are configured.
* **No deps installed**: Verify `gitdependencies` exists in the package’s `package.json`.

# Developer installation testing

Use Rhino's PackageManager/Yak for package installation and removal. Keep developer bundles outside Rhino's search paths while testing the package. A command running inside the plugin cannot demonstrate a clean uninstall in that same Rhino process: restart to test which assemblies actually load. We provide a repository-only Terminal helper instead of a shipped `uninstallLayoutFoundry` command.

For first-install qualification, use a separate macOS user account with Rhino available and no developer load paths. The park/restore cycle below is convenient for daily testing in your development account, but does not reset Rhino preferences, plugin registration, credentials, AI conversations or caches. Use disposable documents. Never delete user data as part of uninstall.

## Inside Rhino: LayoutFoundryDev (Debug only)

Run `LayoutFoundryDev` after loading a Debug build. It prints loaded Foundry assembly versions, build configurations and paths, registered Layout/AI plugin status, and installation candidates in standard Rhino 8 user folders and the current process's `RHINO_PACKAGE_DIRS`. Multiple loaded assemblies with the same name or multiple candidate RHP copies are flagged for inspection. The bounded scan skips symlinks and reports access failures; it cannot certify the absence of every custom or system installation.

Choose a command-line option:

- **LayoutFolder / AIFolder** opens the loaded plugin's folder, falling back to its registered path when unloaded.
- **PackageManager / PlugInManager** opens Rhino's native manager.
- **InstallTesting** prints the external park/install/uninstall/restore sequence.
- **Enter** finishes; **Escape** cancels the option prompt.

The command is compiled only under `DEBUG` and is absent from Release beta packages. It does not mutate documents, remove files, clear user data, or publish packages. Fully quit Rhino before installing the changed Debug bundle, then reopen it. Native command discovery and folder/manager actions still require a Rhino smoke check on each platform.

The development installer accepts an optional verified build-output directory and refuses to run while Rhino is open. For the isolated macOS build from this change, fully quit Rhino, then run from the repository:

```sh
./scripts/install-dev-macos.sh Debug /private/tmp/foundry-dev-command/MacOS/Debug/net8.0
```

Fully reopen Rhino and run `LayoutFoundryDev`. The existing AI bundle's shared assemblies are synchronized by the installer. Uninstall any combined test Yak before returning to a development install. Temporary build directories may be cleaned by the OS; rebuild if this path no longer exists.

## macOS developer bundles

From the Layout repository, inspect and preview:

```sh
python3 scripts/dev-install-macos.py status
python3 scripts/dev-install-macos.py park
```

Fully quit Rhino, then park both bundles:

```sh
python3 scripts/dev-install-macos.py park --apply
```

The helper moves `RhinoLayoutFoundry.rhp` and `RhinoLayoutFoundry.AI.rhp` from `~/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns` into `~/Library/Application Support/LayoutFoundry/DevInstallBackup`. It preserves the entire directories, refuses overwrites and symlink bundles, and checks for running Rhino before moving anything. No files are deleted. Keep Rhino closed for the duration; do not run another installer concurrently.

It only handles these two named directories. If you used custom development-install environment variables, supply the corresponding common parent with `--plugins-dir`; if the bundles have different parents, manage those custom paths separately. `--backup-dir` must be on the same volume and outside Rhino's search paths. Inspect `PlugInManager` paths and any `RHINO_PACKAGE_DIRS` settings, including launcher/debugger configuration, for additional copies. Disabling a plugin alone does not establish that its dependencies are absent.

## Local package cycle

1. Inspect PackageManager → Installed (or `yak list`) and uninstall any separate Layout/AI packages or previous combined bundle. Restart Rhino and verify both plugins are absent/unavailable before installing. Stale registrations or additional load paths need investigation; do not treat them as a pass.
2. Install the supplied combined macOS Yak using the [beta installation guide](MACOS_AI_BETA_INSTALL.md). For Terminal installation with Rhino closed:

   ```sh
   "/Applications/Rhino 8.app/Contents/Resources/bin/yak" install "$PWD/artifacts/macos-ai-beta-20260911/rhino-layout-foundry-ai-bundle-0.1.0-beta.1-rh8_34-mac.yak"
   ```

3. Start Rhino. Confirm both plugins load from the package installation, not a source checkout or development bundle. Compare installed assembly SHA-256 values with the candidate's `VERIFICATION.json`; run `LayoutFoundry`, open **+ → Drawing Set**, and exercise a disposable model, save/reopen and restart. Live AI requests require your own API key.
4. Fully quit Rhino. Uninstall the combined package:

   ```sh
   "/Applications/Rhino 8.app/Contents/Resources/bin/yak" uninstall rhino-layout-foundry-ai-bundle
   "/Applications/Rhino 8.app/Contents/Resources/bin/yak" list
   ```

5. Restart and confirm neither plugin loads; verify saved documents and user settings remain. Quit, reinstall the same local Yak, restart and repeat the smoke check. Same-version reinstall tests removal/reinstallation; an **update** check requires a separately versioned candidate.
6. To resume development, uninstall the test package, fully quit Rhino, then restore:

   ```sh
   python3 scripts/dev-install-macos.py restore
   python3 scripts/dev-install-macos.py restore --apply
   ```

   Reopen Rhino and verify its load paths. Restore returns the exact parked files; it does not rebuild them. Do not run the development installer while package testing, or restore alongside an installed Yak containing the same plugins.

The helper does not install/uninstall Yak packages or clear custom search paths. Use PackageManager → Installed → Uninstall for the equivalent native UI operation. Local Yak installation does not publish anything; publication requires explicit owner instruction.

## Windows

Use a separate Windows user account or VM for first-install testing. Remove developer `RHINO_PACKAGE_DIRS` entries from the test launch environment and inspect PlugInManager for registered source/build paths. The current complete Windows AI candidate is the manual matched archive, not the Layout-only Yak; follow [Windows beta testing](WINDOWS_BETA_TESTING.md). Close Rhino before moving a manual installation out of its load path. Use PackageManager/Yak uninstall only for packages actually installed by Yak. The macOS helper does not modify Windows registrations or files.

## Evidence and current status

Record OS/Rhino versions, package hash, actual loaded paths and assembly hashes, first install/restart, uninstall/restart, reinstall, separately versioned update, and sample-workflow results. Tooling and package verification do not count as native installation evidence. The combined macOS Release Yak's clean-account installation cycle and native Windows checks remain pending. See [current qualification](PRE_BETA_CLEAN_BREAK.md).

Helper validation uses disposable directories: `python3 scripts/test-dev-install-macos.py`. It covers preview, pair round trip, overwrite prevention, running-host refusal, symlinks, nested paths and rollback after a second-move failure. The real development installation is not moved by those tests.

References: [McNeel package installation, uninstall and restart behavior](https://developer.rhino3d.com/en/guides/yak/installing-and-managing-packages/), [Yak local-file CLI syntax](https://developer.rhino3d.com/en/guides/yak/yak-cli-reference/).

Developer command verification (2026-09-11): macOS and Windows Debug/Release compilation passed with zero warnings/errors; the command type is present in Debug and absent in Release. The existing 449 Core tests and nine helper safety tests passed. Shared macOS UI package binaries match the pin. Builds used isolated outputs; the running Rhino installation was not replaced. Native command discovery, option cancellation and folder/manager navigation remain pending after installation and restart.

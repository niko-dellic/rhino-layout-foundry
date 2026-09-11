# macOS Layout + AI private beta

Share `artifacts/macos-ai-beta-20260911/Layout-Foundry-AI-macOS-beta.zip`. It contains one combined `.yak`, installation instructions and a checksum. The `.yak` is also available separately in that directory.

Requires Rhino 8.34 or newer on macOS. Both Layout Foundry and the AI companion are included; no SDK, source checkout or Terminal setup is required. AI requests require the tester's own OpenAI API key.

1. Extract the download ZIP. Keep the `.yak` intact.
2. Open Rhino 8 and drag the `.yak` from Finder into a Rhino viewport.
3. Follow Rhino's installation messages, then fully quit and reopen Rhino.
4. Run `LayoutFoundry`, then open **+ → Drawing Set** for AI.

If dragging does not start installation, run `_-PackageManager _Install "/full/path/to/package.yak"` in Rhino, then restart. [McNeel's instructions](https://discourse.mcneel.com/t/rhino-for-mac-drag-drop-install-for-yak-files/164505/5) document both methods.

Use this combined package instead of separate Layout/AI installations. Remove previously installed separate packages through PackageManager and restart first; ask the sender for help removing any older manual/development installation. Uninstall the combined package through PackageManager → Installed → rhino-layout-foundry-ai-bundle → Uninstall, then restart.

This is local installation, not publication. The package is unpublished. Its contents, shared dependencies, platform tag, absence of the Release demo driver, archive integrity and exact Release assembly hashes have been verified. A clean-Mac install/restart/uninstall test of this combined package remains pending; the existing development installation was preserved.

Rebuild with `python3 scripts/package-macos-ai.py artifacts/pre-beta-clean-break-20260911 NEW_EMPTY_OUTPUT_DIRECTORY`. The script does not install, sign or publish anything.

Developers: follow [Developer installation testing](DEVELOPER_INSTALL_TESTING.md) to park and restore both development bundles and test the local Yak without developer dependencies. Uninstall/reinstall preserves user data; use a separate macOS account for first-install qualification.

LAYOUT FOUNDRY + AI — macOS private beta

Requires Rhino 8.34 or newer on macOS. No SDK, source checkout or Terminal setup is needed. This package includes both Layout Foundry and the matched AI companion.

INSTALL
1. Download the macOS .yak file. Leave it as a .yak file; do not unzip it.
2. Open Rhino 8 and drag the .yak file from Finder into a Rhino viewport.
3. Follow Rhino's installation messages, then fully quit and reopen Rhino.
4. Run LayoutFoundry. In the panel, open + > Drawing Set for AI.

If drag-and-drop does not start installation, enter this in Rhino's command line, replacing the example with the full downloaded path:

_-PackageManager _Install "/Users/your-name/Downloads/rhino-layout-foundry-ai-bundle-0.1.0-beta.1-rh8_34-mac.yak"

Then fully quit and reopen Rhino.

This installs a local file. No public Package Manager listing, Rhino account login, SDK or source checkout is needed for this local package.

EXISTING INSTALLATIONS
Use this combined package instead of separate Layout Foundry/Foundry AI installations. If those were installed through PackageManager, uninstall them there first and restart Rhino. Do not keep older manual/development copies alongside this bundle; ask the sender to help remove those load paths before testing. Do not install the Layout-only Yak in addition to this combined package.

AI SETUP
Use your own OpenAI API key in Drawing Set. API usage is billed separately by the provider. The package includes no API key. Basic Layout Foundry features do not require an API key.

BETA TESTING
Use new models or disposable copies. Old AI conversations are not migrated. Unsupported Foundry metadata is preserved but unavailable for editing. This private candidate is not a production release. Send the project owner your macOS version, Rhino version, steps to reproduce and screenshots when reporting a problem.

UNINSTALL
Run PackageManager, open Installed, select rhino-layout-foundry-ai-bundle, and click Uninstall. Fully quit and reopen Rhino.

INSTALLATION REFERENCES
https://discourse.mcneel.com/t/rhino-for-mac-drag-drop-install-for-yak-files/164505/5
https://developer.rhino3d.com/en/guides/yak/yak-cli-reference/
https://docs.mcneel.com/rhino/8mac/help/en-us/commands/packagemanager.htm

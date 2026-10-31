# RenoDX MFG Unlocker

**RenoDX MFG Unlocker** is an independent Windows desktop utility for managing MFG Unlock addons, NVIDIA Streamline components, Intel XeMFG runtime files, and game-specific configuration.

Designed to simplify runtime preparation, installation, diagnostics, and recovery while keeping users in control of changes to their game files.

**Latest stable version: v1.1.0**

**Developer:** [ExiZz](https://github.com/ExiZz1)

**Original MFG Unlock addon:** [MFGAdaUnlock-RenoDx](https://github.com/mavismmg/MFGAdaUnlock-RenoDx) — developed by [Mateus Jorge / mavismmg](https://github.com/mavismmg).

---

## Download

**[Download the latest release](https://github.com/ExiZz1/RenoDX-MFG-Unlocker/releases/latest)**

Download the Windows x64 ZIP from the release assets, extract it to a folder, and launch `RenoDX MFG Unlocker.exe`.

The v1.1.0 application is distributed as a self-contained Windows x64 build.

No separate .NET installation is required for the packaged self-contained release.

## Features

### MFG Unlock addon management

- Browse available addon releases.
- Select a game and its executable.
- Prepare and install supported addon releases.
- Inspect addon versions and relevant configuration details.
- Access official upstream release information.

### NVIDIA Dynamic MFG

- Discover official NVIDIA Streamline SDK releases.
- Prepare supported Streamline runtime components.
- Inspect existing DLL versions and locations.
- Preview additions, replacements, and unchanged files.
- Avoid unnecessary replacements when file hashes match.
- Require explicit target-directory confirmation.

### Intel XeMFG

- Discover official Intel XeSS SDK releases.
- Prepare compatible XeSS-FG and XeLL runtime components.
- Support documented D3D12 integration workflows.
- Expose experimental Vulkan options only where supported by verified addon capabilities.
- Keep unsupported or unverified settings read-only.

### Safe installation and recovery

- Review planned changes before installation.
- Verify files using SHA-256.
- Maintain installation transactions and backups.
- Restore previously modified files when verified backups are available.
- Detect destination conflicts and protect against unintended writes.
- Separate the selected executable directory from other discovered game folders.

### Runtime Diagnostics

- Inspect runtime files detected on disk.
- Review installation and transaction evidence.
- Examine available logs and historical sessions.
- Distinguish installed DLLs from loaded runtime modules.
- Avoid reporting Frame Generation as active without sufficient evidence.

### MFG Settings

- View supported MFG Unlock settings.
- Edit documented configuration values.
- Reload saved configuration from disk.
- Review explanations for individual settings.
- Apply configuration changes that take effect on the next game launch.

### Multilingual interface

Available in six languages:

- English
- Ukrainian
- Polish
- German
- Spanish
- French

## Installation

1. Download the latest Windows x64 ZIP from GitHub Releases.
2. Extract the complete archive into a folder.
3. Run `RenoDX MFG Unlocker.exe`.
4. Choose your game and the correct executable.
5. Select the required workflow.
6. Review compatibility information and confirm the target directory.
7. Follow the wizard and inspect the Preview before applying changes.

**Close the game before modifying or restoring runtime files.**

## Important compatibility information

**Installing DLL files does not automatically add Frame Generation support to a game.**

Compatibility depends on the game's rendering API, integration, addon version, and available runtime support.

A DLL discovered on disk is not necessarily loaded by the game. Likewise, a loaded DLL does not by itself prove that Frame Generation is active.

Some Intel XeMFG and Vulkan features are experimental and may not work reliably in every game.

Always read the [original MFG Unlock documentation](https://github.com/mavismmg/MFGAdaUnlock-RenoDx) and review compatibility requirements before installation.

## ReShade requirement

The original MFG Unlock addon requires a compatible ReShade installation.

**RenoDX MFG Unlocker v1.1.0 does not automatically install ReShade.**

Install and configure ReShade separately as required by the [original addon](https://github.com/mavismmg/MFGAdaUnlock-RenoDx).

## Security and recovery

The installer is designed to minimize unintended changes through destination validation, file verification, installation previews, and transaction-based recovery.

However, users should still maintain their own backups and carefully review every installation.

The executable is not digitally signed, so Windows may display an unrecognized publisher warning.

Download releases only from this repository.

## Source code

The existing source files in this repository originate from the earlier **v1.0.0** development release and should not be treated as the complete source corresponding to the v1.1.0 binary.

The official v1.1.0 Windows application is distributed through the ZIP attached to its GitHub Release.

GitHub-generated source archives reflect the tagged repository contents and are not equivalent to the separately attached Windows executable package.

## Credits

**[ExiZz](https://github.com/ExiZz1)** — development of the RenoDX MFG Unlocker desktop utility.

**[Mateus Jorge / mavismmg](https://github.com/mavismmg)** — creator of the original [MFGAdaUnlock-RenoDx](https://github.com/mavismmg/MFGAdaUnlock-RenoDx) addon and its underlying functionality.

RenoDX MFG Unlocker is an independent utility and is not an official application of the original MFG Unlock project, NVIDIA, Intel, or ReShade.

All third-party software remains subject to its respective licenses and attribution requirements.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for additional licensing and attribution information.

## Releases and updates

New application builds are published on the [GitHub Releases page](https://github.com/ExiZz1/RenoDX-MFG-Unlocker/releases).

Review each release's notes for new features, compatibility changes, known limitations, and installation instructions.

---

**RenoDX MFG Unlocker v1.1.0 — by ExiZz.**

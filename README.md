# RenoDX MFG Unlocker

RenoDX MFG Unlocker is an independent Windows desktop utility for managing MFG Unlock addons, NVIDIA Streamline components, Intel XeMFG runtime files, ReShade setup and game-specific configuration.

It simplifies runtime preparation, installation, diagnostics and recovery while keeping users in control of changes to their game files.

**Latest stable version: v1.2.0**

**Developer:** [ExiZz](https://github.com/ExiZz1)

**Original MFG Unlock addon:** [MFGAdaUnlock-RenoDx](https://github.com/mavismmg/MFGAdaUnlock-RenoDx) — developed by **Mateus Jorge / mavismmg and contributors**.

---

## Download

### [Download the latest release](https://github.com/ExiZz1/RenoDX-MFG-Unlocker/releases/latest)

For manual installation, download:

**`RenoDX_MFG_Unlocker_v1.2.0_Windows_x64.zip`**

Extract the complete archive and launch `RenoDX MFG Unlocker.exe`.

The application is distributed as a **self-contained Windows x64 build**. No separate .NET installation is required.

The additional `.zip.update.json` and `.zip.sig` release assets are used by the secure application updater. Users installing manually only need the ZIP.

## Features

### MFG Unlock addon management

- Browse releases from the original MFG Unlock repository.
- Select an installed game and confirm its main executable.
- Read release notes inside the application.
- Install or update the selected addon release.
- Inspect the installed addon and its supported configuration.

### NVIDIA Dynamic MFG

- Browse official NVIDIA Streamline SDK releases.
- Download, verify and prepare supported production components.
- Inspect existing Streamline and DLSS-G files, versions and locations.
- Use **Verified Update** where its documented runtime and integration requirements are satisfied.
- Use **Manual NVIDIA SDK DLL Installation** for explicit, supported component selections.
- Review Add, Replace and No Change actions before applying.
- Preserve coherent runtime pairs and dependency restrictions.
- Avoid unnecessary replacements when file hashes already match.

Manual DLL management does not create a working Frame Generation integration simply by copying files. Unknown configurations and critical mismatches remain restricted.

### Intel XeMFG / XeLL

- Browse official Intel XeSS SDK releases.
- Separate selected SDK contents from the pinned MFG Unlock compatibility catalog.
- Prepare the reviewed XeSS-FG and XeLL runtime pair.
- Keep SDK versions distinct from actual DLL versions.
- Support reviewed D3D12 workflows.
- Expose experimental Vulkan functionality only where addon capabilities and integration requirements permit it.

**Official Latest does not mean MFG Unlock Compatible.** Downloading a newer SDK does not automatically replace the runtime pair used by the installer.

### ReShade Full Add-on Support

- Inspect known ReShade proxy files and installation evidence.
- Download and verify the reviewed official Full Add-on Support installer.
- Check package integrity and publisher information before launch.
- Open the official installer through its normal visible workflow.
- Rescan afterward to inspect the resulting installation.

The application does not use unattended installation, forced-update command lines or automatic system Vulkan-layer management.

Changes made by the external ReShade installer are outside the application's managed Restore transactions.

### Safe installation and recovery

- Review the exact file plan before applying changes.
- Confirm the target directory explicitly.
- Validate sources, SHA-256 hashes and applicable digital signatures.
- Recheck files and running-game state before writing.
- Preserve original files for managed replacements.
- Verify installed bytes after copying.
- Record managed operations as transactions.
- Roll back supported failures and restore verified originals.
- Refuse unsafe paths, stale plans and conflicting external changes.

Managed recovery covers recorded operations. It should not be treated as a backup of every external installer or manual modification.

### Runtime Diagnostics

- Inspect components found on disk.
- Review available runtime logs and transaction evidence.
- Separate historical sessions from current-session evidence.
- Distinguish installed files from confirmed runtime loading.
- Avoid declaring Frame Generation active without sufficient evidence.

**Files installed**, **runtime loaded** and **Frame Generation active** are separate results.

### MFG Settings

- Access settings through **General / Advanced / Other**.
- Use version-aware definitions, including reviewed MFG Unlock 1.8.0 settings.
- Compare saved, proposed and default values.
- Preserve pending edits while navigating between categories.
- Use one shared Save / Discard / Reload workflow.
- Preserve unrelated INI sections, keys, comments and encoding.
- Block unsupported configuration writes and changes while the game is running.

For supported MFG Unlock 1.8.0 configurations, the generation selector provides:

- **NVIDIA DLSS-G**
- **Intel XeMFG (CUDA)**
- **XeMFG via DLSS-G (XMX)** — experimental

Method selection changes only the supported `[Backend] Selected` and `[DLSSG] XmxGenerator` values. HUD stabilization, Vulkan options, quality controls and other advanced settings remain independently configurable.

XMX has additional runtime and integration requirements. Selecting a method does not prove that it is active in the game.

### GitHub Updates and secure self-update

- Check for newer Stable releases from this repository.
- Enable optional startup checks, which are off by default.
- Download application updates manually.
- Use authenticated Download and Install when all requirements are satisfied.
- Verify an owner-signed RSA-PSS/SHA-256 manifest and package/member hashes.
- Confirm replacement and restart explicitly.
- Preserve recovery information for the application update.

Only the approved production **public key** is embedded in the application. Users never need the publisher's private key or passphrase.

Users upgrading from v1.1.0 can install v1.2.0 manually using the release ZIP.

### Multilingual interface

Available in six languages:

- English
- Ukrainian
- Polish
- German
- Spanish
- French

The interface language can be changed without restarting the application.

## Installation

1. Download the Windows x64 ZIP from [GitHub Releases](https://github.com/ExiZz1/RenoDX-MFG-Unlocker/releases).
2. Extract the complete archive to a writable application folder **outside your game directories**.
3. Run `RenoDX MFG Unlocker.exe`.
4. Select your game and confirm the correct executable.
5. Choose the required addon, runtime, settings or diagnostic workflow.
6. Review compatibility information and confirm the target directory.
7. Inspect the Preview before approving any changes.

Close the game before installing, changing configuration or restoring files.

When updating the application manually, retain your existing settings and recovery storage.

## Important compatibility information

**Installing DLLs does not automatically add Frame Generation support to a game.**

Compatibility depends on the game's rendering API, integration inputs, addon version, runtime components, hardware and drivers.

- A file found on disk may not be loaded by the game.
- A loaded runtime does not by itself prove that Frame Generation is active.
- A verified signature or matching SHA-256 establishes file integrity/authenticity evidence, not gameplay compatibility.
- Upscaling, Frame Generation and Multi Frame Generation are different capabilities.
- XMX and supported Vulkan workflows remain experimental and may have game-specific limitations.
- NVIDIA App or driver-profile overrides are not activated automatically.

Always consult the [original MFG Unlock documentation](https://github.com/mavismmg/MFGAdaUnlock-RenoDx) before changing a game's runtime configuration.

Respect anti-cheat and online-game restrictions. This utility does not bypass them.

## ReShade requirement

The original MFG Unlock addon requires a compatible **ReShade Full Add-on Support** installation.

v1.2.0 provides a reviewed download, verification and visible official-installer workflow. Follow the official setup instructions and rescan afterward.

Installing the MFG Unlock addon alone does not install ReShade or establish a compatible rendering integration.

## Security and recovery

Review every proposed file operation and maintain your own backups.

Restore requires the corresponding verified transaction, recovery files and local protection data. Missing or modified recovery material, or files changed by another program, can prevent safe automatic restoration.

The release-package signature is separate from Windows Authenticode signing. The application EXE is not Authenticode-signed, so Windows may display an unrecognized publisher warning.

Download application packages only from this repository's official Releases.

NVIDIA/Intel runtime DLLs and ReShade installers are obtained separately; they are not bundled in the application ZIP.

## Source code

The source files currently published in this repository originate from an earlier development release. They do **not** represent the complete source corresponding to the v1.2.0 Windows binary.

Download the application from the separately attached Windows x64 release ZIP.

GitHub-generated **Source code (zip)** and **Source code (tar.gz)** archives reflect the tagged repository contents. They are not the runnable Windows application package.

## Credits

**[ExiZz](https://github.com/ExiZz1)** — developer of the RenoDX MFG Unlocker desktop utility.

**[Mateus Jorge / mavismmg](https://github.com/mavismmg) and contributors** — developers of the original [MFGAdaUnlock-RenoDx](https://github.com/mavismmg/MFGAdaUnlock-RenoDx) addon and its underlying functionality.

Related projects:

- [NVIDIA Streamline](https://github.com/NVIDIA-RTX/Streamline)
- [Intel XeSS](https://github.com/intel/xess)
- [ReShade](https://reshade.me/)
- [.NET](https://dotnet.microsoft.com/)

RenoDX MFG Unlocker is an independent utility, not an official application of the original MFG Unlock project, NVIDIA, Intel or ReShade.

Third-party components remain subject to their respective licenses. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the runtime license files included with the release.

## Releases and updates

Application builds are available on the [GitHub Releases page](https://github.com/ExiZz1/RenoDX-MFG-Unlocker/releases).

Read the release notes for installation instructions, compatibility requirements and known limitations.

---

**RenoDX MFG Unlocker v1.2.0 — by ExiZz.**

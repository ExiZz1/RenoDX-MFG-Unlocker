# RenoDX MFG Unlocker v1.4.0

Windows x64 utility by **ExiZz** for MFG Unlock addon/runtime maintenance, version-aware saved settings, diagnostics and recoverable managed file changes.

Application: https://github.com/ExiZz1/RenoDX-MFG-Unlocker  
Original MFG Unlock: **Mateus Jorge / mavismmg and contributors** — https://github.com/mavismmg/MFGAdaUnlock-RenoDx

This is an independent utility. ExiZz is not the original addon author. NVIDIA, Intel and ReShade do not endorse this application.

## New in v1.4.0

See [release notes](RELEASE_NOTES_v1.4.0.md) for the changes in v1.4.0.

## Introduced in v1.3.0

- MFG Unlock 1.8.5: unchanged source declarations are labelled for **reconfirmation**, not as entirely new settings. Both releases are fully analyzed; each new release still requires explicit approval and keeps its own write scope. Type/range/default/dependency/file/editor changes are not treated as unchanged. Synthetic 1.8.6/1.9.0 names verify the general rule.
- `[RenoDX.MFGUnlock] DynamicMaxMultiplier` is discovered **read-only** in 1.8.5. Its custom C++ validation/DRS hook is outside the finite declaration grammar. Supported upstream values are0(Auto),2,3,4,5,6, default0; game restart required. Configure it in the original addon panel for now. A separately reviewed owner-signed catalog can support it later; no new production catalog is bundled.

- **Discover settings from the official addon source**, without updating this EXE or requiring a new RenoDX catalog for each recognized declaration. Fetch and analysis are explicit actions; inspect original source evidence and approve before adding settings. No INI write occurs during discovery or approval.
- **39 reviewed baseline definitions**, including the unchanged owner-signed Revision 2. New parameters join existing collapsible groups across both INI files. General / Advanced / Other retain pending edits and the shared Save / Discard / Reload workflow.
- **GenericGate, MFG Unlock 1.8.4:** the owner successfully discovered, saved and reloaded this Boolean through dev.5 without an EXE update or new signed catalog. Final nonvisual tests reproduce discovery and byte-exact Save/Restore in isolated fixtures. This is an owner-reported real test, not a new gameplay claim for every configuration.
- **Signed RenoDX catalogs remain a fallback**, separately verified with catalog-domain RSA-PSS/SHA-256. Source approval is locally cached for the Windows user; it does not turn upstream code into an owner-signed catalog.
- **Optional official-addon release notifications**, off by default, metadata only and at most once per 24 hours across launches. Source download/analysis and import always require your action. This is separate from application self-update and catalog checks.
- **Eight live-switchable UI languages:** English, Ukrainian, Polish, German, Spanish, French, Czech and Brazilian Portuguese. Future upstream titles/descriptions stay in their original language with localized provenance when no reviewed translation exists.

### Discovery safety and limits

The analyzer understands a finite reviewed C++ declaration grammar, not arbitrary C++. It never executes downloaded source. Supported explicit Boolean/Integer/Enum declarations can supply metadata; unknown readers, computed defaults, changed editor frameworks, internal/learned state and unresolved dependencies remain read-only. New destination files, types or dependency semantics can still require an EXE update or reviewed signed catalog.

Protected builtin/owner definitions cannot be overwritten by source discovery. Approval rollback and conflicting commits for the same approved release are refused. A recognized source definition is scoped to the reviewed addon release, not automatically certified for all future source revisions. Existing minimum-version capability inheritance from v1.2.1 remains.

## Start or update

1. Extract the complete Windows x64 ZIP to a writable application folder **outside any game folder**. Run `RenoDX MFG Unlocker.exe`. The .NET 8 desktop runtime is included; a separate .NET installation is not required.
2. Choose the installed game and confirm its actual main EXE. Review all proposed paths and file changes. Close the game before installing or changing configuration.
3. Use Home to browse the original MFG Unlock releases, read release notes and install/update the selected `renodx-mfgunlock.addon64` beside the confirmed EXE. Installing this addon does not install ReShade or guarantee runtime compatibility.
4. Use DLL Installation for supported NVIDIA/Intel workflows; inspect Preview before confirming Apply. Use Transactions / Restore for changes recorded by the managed transaction system.

When upgrading manually from v1.1.0 or another older build, extract v1.4.0 into a separate application folder. Keep your existing settings and recovery storage. Do not delete recovery originals or replace game DLLs by hand as part of the application update.

## NVIDIA runtime workflows

- **Verified Update** retains the reviewed coherent Streamline/DLSS-G workflows and their prerequisites. Reviewed pairs include Streamline 2.14.1 + DLSS-G 310.9.1 and Streamline 2.14.16 + DLSS-G 310.9.16. A documented version pair is not a certification of every game.
- **Manual SDK DLL Management** inventories supported production files from a verified official NVIDIA SDK, allows explicit supported file selections and shows Add / Replace / No Change with source, version and hash details. It does not silently approve unknown configurations or invent a new Frame Generation integration.
- Sources, archive/member hashes, native architecture, publisher signatures where applicable, target identity and current file/process state are checked. Critical mismatches remain blocked. Downloaded NVIDIA binaries are not bundled with this application.

## Intel XeMFG / XeLL

The existing Intel installer prepares the pinned, reviewed XeSS-FG / XeLL pair (libxess_fg.dll 1.3.1.78 and libxell.dll 1.3.2.10) with its required notices. SDK version and DLL version are distinct. Official Latest does not mean MFG Unlock Compatible: downloading a newer SDK does not change the Wizard's compatibility catalog.

Intel runtime installation does not require an NVIDIA DLSS-G provider in every supported scenario. It still requires supported addon capabilities, integration inputs, API and an explicitly confirmed destination. Vulkan remains experimental and version/integration gated. No universal game or GPU support is promised.

## ReShade Full Add-on Support

The ReShade page inspects known local proxies and provides a verified download/visible launch workflow for the reviewed official Full Add-on Support installer. SHA-256 and publisher checks repeat before launch. Follow the official installer's warnings and selection steps, then rescan in RenoDX MFG Unlocker.

This version does not use unattended/headless, forced-update or preselected target/API command lines. Unknown proxy conflicts and known unsafe paths remain blocked. External ReShade Setup changes are **not automatically covered by managed Restore**; retain your own recovery plan for those operations. System Vulkan layers are not managed by this utility.

## MFG Settings and experimental XMX

Version-aware MFG Settings supports reviewed addon schemas, including MFG Unlock 1.8.0–1.8.5. General / Advanced / Other share Save / Discard / Reload and retain pending independent edits. Saved, proposed and default values are distinguished; unsupported versions and running-game writes remain blocked. Managed configuration changes preserve unrelated INI sections, keys, comments and encoding, with conflict checks and transactional recovery.

For addon 1.8.0 or newer, the generation selector supports NVIDIA DLSS-G, Intel XeMFG (CUDA) and experimental XeMFG via DLSS-G (XMX). It changes only `[Backend] Selected` and `[DLSSG] XmxGenerator`. HUD stabilization, Vulkan, quality and other options remain independent. XMX has separate runtime/input requirements and NVIDIA-only quality control restrictions. Addon 1.8.3 or newer also permits experimental NVIDIA Dynamic MFG with XMX using the documented runtime stack; this is not a universal compatibility guarantee. Restart the game where indicated; selecting or saving a method is not proof it is active.

## Components, diagnostics and recovery

- Components / SDK separates NVIDIA and Intel releases, selected SDK contents, prepared local files and pinned compatibility catalogs. Missing DLLs do not by themselves mean a broken game.
- Runtime Diagnostics separates files found on disk, runtime-loaded evidence and actual generation activity. Historical log sessions are not reported as current runtime confirmation; unavailable evidence remains Unknown.
- Managed Apply checks for stale previews, running games, unsafe/reparse paths and changed sources/targets. Original files are preserved before replacement; written bytes are checked afterward. Transactions support rollback and Restore, refusing to overwrite unrelated later changes. Keep the original transaction/key/recovery storage: a missing or changed backup/manifest/key can prevent safe Restore.
- The original addon-install workflow remains separate. Do not assume every external/manual operation has a managed transaction or backup.

## Application updates

Check for Updates uses Stable releases from this application's official GitHub repository. Startup checks are optional and off by default. Download Update remains available; Download and Install additionally requires the owner's RSA-PSS/SHA-256 signed manifest and detached signature, matching package/member hashes, supported single-EXE inventory, safe target/process state and your confirmation before restart.

The client contains only the approved **public** key `owner-release-2026`. Users never need the publisher's private key or passphrase. Missing/invalid signed assets cannot install. The updater preserves unrelated files and provides bounded backup/rollback; process creation alone is not a full application-health check. It does not update game files or driver profiles.

## Requirements and compatibility limits

- Windows x64 with the game's own supported graphics API, hardware and driver requirements. Self-contained .NET does not provide missing GPU/game dependencies.
- ReShade Full Add-on Support and a compatible MFG Unlock addon/integration are needed where the selected scenario requires them. Upscaling, Frame Generation and Multi Frame Generation are different features.
- **Copying DLLs does not create a Frame Generation integration or guarantee MFG works in a game.** SDK versions, hashes and signatures establish file evidence, not gameplay compatibility or runtime activation.
- Use mods only where the game permits them. Anti-cheat, protected/online titles and unsupported targets may prohibit modification; this utility does not bypass those restrictions. Follow the game and mod authors' guidance.
- Unknown profiles, unverified replacements, unknown dependencies and unsafe paths can remain blocked. NVIDIA App/Profile Inspector overrides are not activated automatically. No driver DLLs are installed by these workflows.

## Languages and licenses

English, Ukrainian, Polish, German, Spanish, French, Czech and Brazilian Portuguese are supported, with live language switching.

RenoDX MFG Unlocker is **Source Available**. The [license](LICENSE) permits personal, non-commercial use of official builds and permits viewing and auditing the published source. Modification, reuse, redistribution of the covered source or of third-party builds, and commercial use require prior written permission from ExiZz, subject to statutory and platform rights and prior permissions.

Third-party materials retain their own terms, including the [upstream MFG Unlock MIT license](LICENSES/MFGUnlock-Metadata-MIT.txt) and the Microsoft runtime notices. See [Third-party notices](THIRD_PARTY_NOTICES.md). NVIDIA/Intel runtime and ReShade packages are downloaded separately. Applicable upstream notices must remain with separately prepared components.

Official projects: https://github.com/NVIDIA-RTX/Streamline · https://github.com/intel/xess · https://reshade.me/ · https://github.com/crosire/reshade

## Build from source (Windows x64)

These instructions are for maintainers and others whose authorization covers building the application. See the [license](LICENSE) for the terms governing the ExiZz-owned code.

Install .NET SDK 8.0.426 or a compatible 8.0 feature-band SDK allowed by `global.json`, including the Windows Desktop targeting pack.

```powershell
dotnet restore .\RenoDXMfgUnlocker\RenoDXMfgUnlocker.csproj -r win-x64
dotnet publish .\RenoDXMfgUnlocker\RenoDXMfgUnlocker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o .\artifacts\v1.4.0
pwsh -File .\tools\Test-NonVisual.ps1
```

Run the public nonvisual test selection with the command above. It uses a console test harness and does not require unpublished signing tools. See the [build and test guide](BUILDING.md) for its scope and prerequisites.

Release packages use the existing eight-file updater inventory. Only the owner signs the final ZIP locally; never embed private keys or publish synthetic test packages. Source builds are functionally reproducible with the documented toolchain, but bit-identical binaries are not promised across different build paths/toolchains.

# Build and nonvisual checks

These instructions are for maintainers and others whose authorization covers building the application. See the [license](LICENSE) for the terms governing the ExiZz-owned code.

Use Windows x64, .NET SDK 8.0.426 and PowerShell 7.4+. No external application NuGet packages are declared. Restore may need internet for the Windows Desktop/runtime packs. Do not treat a skipped online NuGet advisory check as a successful vulnerability audit.

```powershell
dotnet restore .\RenoDXMfgUnlocker\RenoDXMfgUnlocker.csproj -r win-x64
dotnet clean .\RenoDXMfgUnlocker\RenoDXMfgUnlocker.csproj -c Release -r win-x64
dotnet publish .\RenoDXMfgUnlocker\RenoDXMfgUnlocker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o .\artifacts\v1.4.0
pwsh -File .\tools\Test-NonVisual.ps1
```

The public test command uses the selection in [NonVisualChecks.json](tools/NonVisualChecks.json) and reports passed, failed, and skipped checks. Tests use isolated temporary files. Some real vendor DLL fixtures and privileged symlink checks may be unavailable and are reported as skipped. The unfiltered harness also registers historical UI and owner-signing checks that are outside the public test selection. Use the command above for this archive.

Embedded signed settings catalog JSON/signature and the approved production public key are source inputs, not private keys. Source approval is separate from owner signatures. End users/builders never need the owner's encrypted private key or passphrase. Owner signing tools are separate release-authoring utilities and are not required to compile the app.

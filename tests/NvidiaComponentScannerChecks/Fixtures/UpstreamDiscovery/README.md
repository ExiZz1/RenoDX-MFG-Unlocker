# Upstream source fixtures

Official repository: https://github.com/mavismmg/MFGAdaUnlock-RenoDx

These C++ source text fixtures come from pinned commits in the official repository. Each directory records its exact tag and commit. The tests read the fixture text without compiling or executing C++.

The only generated declarations used by tests are clearly marked synthetic in Dev4UpstreamChecks.cs. The historical legacy-only RenoDX extension model is simulated; no test claims to run an older RenoDX EXE.

Original addon: Mateus Jorge / mavismmg and contributors. Source copyright (c) 2026 ImDreamt, MIT. See the [full original notice](../../../../LICENSES/MFGUnlock-Metadata-MIT.txt).

The 1.8.6 and 1.9.0 fixtures retain the same source subset from the verified release snapshots, for declaration and runtime-log regression checks.

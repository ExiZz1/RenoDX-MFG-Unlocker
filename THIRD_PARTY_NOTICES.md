# Third-party notices and attribution

## Original MFG Unlock project

Original MFG Unlock author: Mateus Jorge / mavismmg and contributors. RenoDX MFG Unlocker uses the public releases from:

- Project/profile: https://github.com/mavismmg
- Source repository: https://github.com/mavismmg/MFGAdaUnlock-RenoDx
- Repository license file: https://github.com/mavismmg/MFGAdaUnlock-RenoDx/blob/main/LICENSE

The upstream project uses the MIT license. This utility retrieves the selected published addon asset at runtime. This source package includes the reviewed advanced_settings.hpp as non-executable parser-contract data and attributed historical source fixtures for nonvisual tests. Their [full MIT notice](LICENSES/MFGUnlock-Metadata-MIT.txt) continues to apply. The About page credits the original project and contributors and identifies RenoDX MFG Unlocker as an independent utility.

## Application developer

RenoDX MFG Unlocker utility developer: ExiZz. Developer profile: https://github.com/ExiZz1

The app developer is not presented as the original addon author.

## Framework and components

The app uses .NET 8 WPF framework/runtime components from Microsoft. No external NuGet package references are declared. Single-file self-contained publishing includes required .NET runtime components in the Windows executable. The public package includes the original Microsoft runtime license and third-party notice files in LICENSES.

The R icon assets in this source project are local application artwork. No external font files or icon packages are used.
Intel runtime is downloaded separately from the official intel/xess pinned commit. The application distribution contains no Intel runtime binaries. Runtime preparation preserves the exact Intel Simplified Software License and third-party notices, and the Intel transaction installs them alongside each DLL pair. The original upstream dependency catalog supplies runtime provenance and license links. No Intel or NVIDIA endorsement is implied.

## ReShade (v1.2 Phase 1)

ReShade by crosire and contributors: https://reshade.me/ and https://github.com/crosire/reshade . Source license: BSD 3-Clause, https://github.com/crosire/reshade/blob/v6.8.0/LICENSE.md . This utility does not bundle ReShade binaries or shaders. It directs downloads to the official Full Add-on Support installer and preserves its original notices/warnings and normal workflow. No endorsement is implied. The application verifies the reviewed installer hash and publisher before launch. External setup changes are outside managed Restore.

## MFG settings metadata (v1.3 Development)

The embedded settings catalog includes descriptions/constraints derived from MFG Unlock advanced_settings.hpp at commit 3b8889f98cd0cf50e4f7d5daca730fc6084ff57d. Upstream is MIT-licensed, copyright (c) 2026 ImDreamt. Full notice: LICENSES/MFGUnlock-Metadata-MIT.txt. Credit to Mateus Jorge / mavismmg and contributors; utility by ExiZz. No addon/runtime DLL is bundled. Original-language descriptions are evidence, not a game-specific recommendation.

## Embedded upstream parser-contract license

MIT License

Copyright (c) 2026 ImDreamt

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.


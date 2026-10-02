# Third-party notices

- Microsoft .NET runtime / WPF is bundled through the official self-contained SDK publish mechanism. The runtime license/notices and Windows Desktop package license are included as `Assets/Runtime-*.TXT` and embedded in the EXE. WPF notices match https://github.com/dotnet/wpf/blob/v10.0.12/THIRD-PARTY-NOTICES.TXT . Upstream runtime notices: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT and https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT .
- Microsoft Windows built-in utilities and PowerShell are invoked from the installed operating system; none are redistributed. Windows remains a user-supplied prerequisite.
- NVIDIA NVAPI is invoked from the installed NVIDIA driver. Tweakly includes an independently written minimal ABI binding, not an NVIDIA DLL, SDK library, Profile Inspector executable or copied EXM profile. API documentation: https://github.com/NVIDIA/nvapi .
- Original artwork, icon and application code are covered by the Tweakly MIT license.
- EXM Free Tweaking Utility V9.3 was examined as a functional reference. Its batch source, copyrighted promotional artwork, private power-plan/profile assets and downloaded third-party binaries are not included. Functional setting names and values are represented as typed data. No affiliation or endorsement is claimed.

Public redistribution should include this notice, the MIT license and the applicable embedded runtime notices. The runtime's license / third-party notice text is available in the app's About page for a single-file distribution.

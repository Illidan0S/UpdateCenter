# WinGet B2 fixture provenance

Official installer metadata fetched on 2026-11-09. No installer payload was downloaded.
SHA256 identifies the downloaded bytes before Git line-ending normalization.
The tests use these complete manifests, not a mocked compatible-installer verdict.

Incident regression fixtures fetched on 2026-11-10 (metadata only):

| Package / exact version | Downloaded SHA256 |
| --- | --- |
| [Ubisoft.Connect 175.1.0.13416](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/u/Ubisoft/Connect/175.1.0.13416/Ubisoft.Connect.installer.yaml) | `1D8B2A2060E0221384B2B6B7ADF5F1CAD263050C25B4926048BDD9B9EBC5062B` |
| [Moonsworth.LunarClient 3.7.28](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Moonsworth/LunarClient/3.7.28/Moonsworth.LunarClient.installer.yaml) | `D351536C4AFC1002DEB0C4DD3862BEC98C4FA274E63509539429C3AFC8198E19` |

| Package / exact version | Downloaded SHA256 |
| --- | --- |
| [Anysphere.Cursor 3.27.12](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Anysphere/Cursor/3.27.12/Anysphere.Cursor.installer.yaml) | `8F411DFC0B129E15CF50F869148D659ADB83573C8D7BAC94526FA343CC527ABD` |
| [AnywhereLabs.DSHDesktop 2.0.22-next](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/AnywhereLabs/DSHDesktop/2.0.22-next/AnywhereLabs.DSHDesktop.installer.yaml) | `9C3C4F65D9A10B9FCD0295DAF22B0CFB79620B9EAEB45121C40CE96C60CECCDB` |
| [ElementLabs.LMStudio 0.4.28+2](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/e/ElementLabs/LMStudio/0.4.28%2B2/ElementLabs.LMStudio.installer.yaml) | `9C3FF0A526790592BF233A8158F4A7FD34C540B9B2C461F3312E5313B6235E64` |
| [Anaconda.Miniconda3 py314_26.9.4-0](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Anaconda/Miniconda3/py314_26.9.4-0/Anaconda.Miniconda3.installer.yaml) | `D9423B51065B59F5D2B04A851226902F93EE6736C44E46CEABB44C3FA436E65A` |
| [Opera.OperaGX 139.0.6053.42](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/o/Opera/OperaGX/139.0.6053.42/Opera.OperaGX.installer.yaml) | `1A600E1775A678CB90F6915AD777FF37C17F736AF09E178EB9A50DEFFAF41FC5` |
| [Python.Python.3.13 3.13.17](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/Python/Python/3/13/3.13.17/Python.Python.3.13.installer.yaml) | `58EBFF63FDD8E89A0F2CFE722C72E724F6C038FFC1AB2497620E4A5735650FAB` |
| [Git.Git 2.56.0.4](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/g/Git/Git/2.56.0.4/Git.Git.installer.yaml) | `661AF7ADECDC1104FF06434E0B697BC8FB5E5F4A389795EA48865A466BCDD954` |
| [Google.Chrome.EXE 157.0.8103.39](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/g/Google/Chrome/EXE/157.0.8103.39/Google.Chrome.EXE.installer.yaml) | `5A1D567CFE35B766634BC2C5987289A6BAC5A4CCCC9F352BD57299D6CB8AD21B` |
| [JRSoftware.InnoSetup.7 7.1.1](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/j/JRSoftware/InnoSetup/7/7.1.1/JRSoftware.InnoSetup.7.installer.yaml) | `95037F1F57655780D66F07638B41F2B0326477328F00FC7634C562C8D4E0C1B5` |
| [Microsoft.VisualStudioCode 1.143.1](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Microsoft/VisualStudioCode/1.143.1/Microsoft.VisualStudioCode.installer.yaml) | `49FA5278BBDEA1FE7175C8446967CCB940CC7A27262CE687C93F3C008245D1A4` |
| [Perplexity.Comet 156.0.8078.132](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/Perplexity/Comet/156.0.8078.132/Perplexity.Comet.installer.yaml) | `C64A89AF2169C8448AD06A00BE7C1B71537B0A301FA8645E3EEECCDACDEF1619` |
| [HypixelStudios.Hytale 2026.10.26-7b63bde](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/h/HypixelStudios/Hytale/2026.10.26-7b63bde/HypixelStudios.Hytale.installer.yaml) | `3F0C09ABAB90890069D1D381276D4E491E8E4C727DBCA04487931DA5977864EF` |
| [Modrinth.ModrinthApp 0.23.1](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Modrinth/ModrinthApp/0.23.1/Modrinth.ModrinthApp.installer.yaml) | `0C4A2054A5E05F4226C9DEAD2EE212303795D7363833C78154D6DD49638CCC2E` |

Dependency coverage fixture (10 November 2026):

| Package / exact version | Local SHA256 (LF) |
| --- | --- |
| [RockstarGames.Launcher 1.0.110.3150](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/r/RockstarGames/Launcher/1.0.110.3150/RockstarGames.Launcher.installer.yaml) | `8C96B383EF78430D86261B91C58882106871450830A7795AD448615CD011BC3E` |

Reused from the R3 research snapshot. The native read-only preflight received
the equivalent CRLF document (`7E8731A7C308DBCD5B9A870D417E1F23B600C180EC128EECE999AA90E7A2D2E8`).
The tests simulate installer dispatch. No payload was downloaded or installed.

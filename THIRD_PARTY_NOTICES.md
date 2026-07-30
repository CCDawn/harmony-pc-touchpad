# Third-party notices

No third-party application source code is incorporated in the repository.
The Windows agent test projects use the NuGet packages xUnit.net,
Microsoft.NET.Test.Sdk, xunit.runner.visualstudio, and coverlet.collector under
their respective licenses; they are development-only dependencies and are not
shipped with the tray application.

The Windows runtime project uses Microsoft's
`System.Security.Cryptography.ProtectedData` package to access Windows DPAPI.

The project architecture and gesture behavior were informed by public
documentation and open-source research, including the MIT-licensed
[Mousedroid](https://github.com/darusc/Mousedroid) project. Its source has not
been copied into the current implementation.

Any future source adaptation must:

1. use a license compatible with this project's intended distribution;
2. preserve the original copyright and license notice;
3. be recorded in this file with the affected paths; and
4. include tests proving that the adapted behavior conforms to this project's
   own protocol and security boundaries.

GPL, AGPL, and non-commercial Creative Commons source must not be copied into
this repository without an explicit licensing decision.

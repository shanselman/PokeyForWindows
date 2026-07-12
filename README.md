# Pokey for Windows

Pokey for Windows is a free and open-source .NET 10 WPF tray app intended to
bring Pokey-style cursor movement to Windows.

Press **Ctrl+Alt+P** to start or stop it. While active, the app follows the
pointer across monitors and escalates its hand-drawn impact bursts as clicks
arrive faster. The tray icon provides status and controls without keeping a
window open.

> [!IMPORTANT]
> Reuse of the Pokey name and any Pokey-derived artwork is provisional pending
> permission from Mikhail Kosenkov, creator of [Pokey](https://www.pokeyapp.com/).
> See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Download

Download the latest signed release from
[GitHub Releases](https://github.com/shanselman/pokeyforwindows/releases/latest).

| Asset | Windows platform |
|---|---|
| `PokeyForWindows-vX.Y.Z-win-x64.zip` | Intel and AMD 64-bit |
| `PokeyForWindows-vX.Y.Z-win-arm64.zip` | ARM64 |

Each ZIP contains a self-contained, single-file executable; a separate .NET
installation is not required.

After its community manifest is accepted, install or update with Windows
Package Manager:

```powershell
winget install Hanselman.PokeyForWindows
winget upgrade Hanselman.PokeyForWindows
```

The portable command alias is `pokey`.

## Use

1. Start `PokeyForWindows.exe`. The pointer is off on first launch.
2. Click the notification-area icon or press **Ctrl+Alt+P** to toggle it.
3. Click normally in any application. Slow clicks produce a small poke; rapid
   clicks escalate through stronger hand motion and larger hand-drawn bursts.
4. Right-click the tray icon for animation, startup, settings, update, and exit
   controls.

The overlay is click-through, so clicking, dragging, selecting, scrolling, and
context menus continue to work in the underlying app. One overlay is maintained
per monitor for mixed-DPI and multi-monitor layouts. While enabled, a companion
watchdog restores the Windows cursor scheme if the main process crashes.

Share or record the **entire display** when you want viewers to see Pokey.
Window-only capture intentionally excludes everything outside that window,
including desktop overlays.

## Build and run

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
on Windows.

```powershell
dotnet restore .\PokeyForWindows.slnx
dotnet build .\PokeyForWindows.slnx -c Release --no-restore
dotnet run --project .\src\PokeyForWindows\PokeyForWindows.csproj
```

Run the interop harness:

```powershell
dotnet run --project .\src\PokeyForWindows.InteropHarness\PokeyForWindows.InteropHarness.csproj -- 10000
```

The repository build wrapper restores, builds, and runs the harness. Add
`-Publish` to publish both supported Windows RIDs:

```powershell
.\build.ps1
.\build.ps1 -Publish -Version 1.0.0
```

Published files are written beneath `.\artifacts\publish\win-x64\` and
`.\artifacts\publish\win-arm64\`.

## Releases and updates

Versions are derived from exact `vX.Y.Z` Git tags. A tagged build runs the
interop harness before publishing x64 and ARM64 self-contained single-file
executables. Azure Trusted Signing signs both executables, and the workflow
verifies each Authenticode signature before creating the ZIPs. Tagged
releases fail rather than publish unsigned binaries.

The release workflow refuses to replace an existing release and verifies that
the tag still points to the triggering commit before creating the GitHub
Release. Treat published tags and assets as immutable: publish a new version
instead of replacing them.

Updates are distributed as new GitHub Releases and through WinGet package
`Hanselman.PokeyForWindows`. The WinGet submission workflow is reusable and
can also be run manually for an existing tag.

### Release configuration

Configure these repository secrets:

- `AZURE_CLIENT_ID`
- `AZURE_CLIENT_SECRET`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_TENANT_ID`
- `WINGET_CREATE_GITHUB_TOKEN` (a token permitted to submit to
  `microsoft\winget-pkgs`)

Configure these repository variables:

- `AZURE_TRUSTED_SIGNING_ENDPOINT`
- `AZURE_TRUSTED_SIGNING_ACCOUNT`
- `AZURE_CERTIFICATE_PROFILE_NAME`

For a release, create and push an exact semantic-version tag:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

Manual workflow runs may explicitly allow unsigned artifacts for testing, but
an unsigned run can never create a tagged release.

## License

The project code is available under the [MIT License](LICENSE). The Pokey name
and artwork are not covered by that license; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

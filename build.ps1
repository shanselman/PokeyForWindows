[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [ValidateRange(1, 1000000)]
    [int] $HarnessIterations = 10000,

    [switch] $Publish,

    [string] $Version = ""
)

$ErrorActionPreference = "Stop"
$project = "src\PokeyForWindows\PokeyForWindows.csproj"
$harness = "src\PokeyForWindows.InteropHarness\PokeyForWindows.InteropHarness.csproj"
$solution = "PokeyForWindows.slnx"

function Invoke-DotNet {
    param([string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location $PSScriptRoot
try {
    Invoke-DotNet -Arguments @("restore", $solution)
    Invoke-DotNet -Arguments @(
        "build", $solution,
        "--configuration", $Configuration,
        "--no-restore"
    )
    Invoke-DotNet -Arguments @(
        "run",
        "--project", $harness,
        "--configuration", $Configuration,
        "--no-build",
        "--", $HarnessIterations.ToString()
    )

    if ($Publish) {
        foreach ($rid in @("win-x64", "win-arm64")) {
            $output = "artifacts\publish\$rid"
            $arguments = @(
                "publish", $project,
                "--configuration", $Configuration,
                "--runtime", $rid,
                "--self-contained", "true",
                "--output", $output,
                "/p:PublishSingleFile=true",
                "/p:IncludeNativeLibrariesForSelfExtract=true",
                "/p:DebugType=None",
                "/p:DebugSymbols=false"
            )

            if (-not [string]::IsNullOrWhiteSpace($Version)) {
                $arguments += "/p:Version=$Version"
            }

            Invoke-DotNet -Arguments $arguments
        }
    }
}
finally {
    Pop-Location
}

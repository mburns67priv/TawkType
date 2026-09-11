<#
.SYNOPSIS
    Publishes TawkType and packs it into a Velopack installer.

.DESCRIPTION
    Produces build/releases/, which contains everything a GitHub Release needs:
      Talk2MeApp-win-Setup.exe  the installer people download - named from packId, not the app
      *-full.nupkg            the payload the installer and the updater read
      *-delta.nupkg           the small diff, when a previous release is present
      RELEASES-win            the feed the app checks for updates

    Delta updates only exist if the previous release's packages are in the output folder first, so CI
    downloads the last release into build/releases before packing. Locally you can skip that; you just
    get a full package.

.PARAMETER Version
    Release version, e.g. 0.2.0. Must climb with each release or the updater ignores it.

.PARAMETER Channel
    Velopack channel. 'win' is the default and the one the app reads.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Version,
    [string] $Channel = 'win'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'build/publish'
$releases = Join-Path $root 'build/releases'

Write-Host "==> publishing TawkType $Version" -ForegroundColor Cyan

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

# Framework-dependent: the .NET 8 Desktop Runtime is a prerequisite, which keeps the download ~90 MB
# rather than ~230 MB self-contained. Velopack's installer prompts for the runtime if it is missing.
dotnet publish (Join-Path $root 'src/Talk2Me.App/Talk2Me.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:Version=$Version `
    -o $publish

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$size = [math]::Round((Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "    payload: $size MB" -ForegroundColor DarkGray

# Guard the thing that once made this 1249 MB. Whisper.net's runtime packages have shipped Linux and
# macOS binaries into Windows builds before; if that regresses, fail here rather than in a release.
$foreign = Get-ChildItem $publish -Recurse -File -Include *.so, *.dylib, *.a -ErrorAction SilentlyContinue
if ($foreign) {
    throw "non-Windows native binaries in the publish output: $($foreign.Name -join ', ')"
}
if ($size -gt 250) {
    throw "publish output is $size MB, which is far larger than expected - check for stray runtimes"
}

Write-Host "==> packing" -ForegroundColor Cyan

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "    installing the vpk tool" -ForegroundColor DarkGray
    dotnet tool install -g vpk
    if ($LASTEXITCODE -ne 0) { throw "could not install vpk" }
}

New-Item -ItemType Directory -Force -Path $releases | Out-Null

# packId is the install folder name: %LOCALAPPDATA%\<packId>. It must NOT be "Talk2Me", because
# builds before the installer kept user data in %LOCALAPPDATA%\Talk2Me, and the installer clears its
# target directory before extracting. Installing over it destroys settings, history and gigabytes of
# downloaded models before the app ever runs to migrate them.
#
# It is also not renamed to match TawkType, for the same reason plus one more: packId is the update
# channel every installed copy already polls. packTitle is the half users actually see - the Start
# Menu shortcut and the Programs and Features entry - so that is the half that carries the new name.
vpk pack `
    --packId Talk2MeApp `
    --packVersion $Version `
    --packDir $publish `
    --mainExe Talk2Me.exe `
    --packTitle 'TawkType' `
    --packAuthors 'Jupitor Studio' `
    --icon (Join-Path $root 'src/Talk2Me.App/Assets/talk2me.ico') `
    --channel $Channel `
    --outputDir $releases

if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Host "==> done" -ForegroundColor Green
Get-ChildItem $releases | Sort-Object Length -Descending |
    Format-Table @{ n = 'size'; e = { '{0,8:N1} MB' -f ($_.Length / 1MB) } }, Name -AutoSize

param(
    [string]$UpdateDll = '',
    [string]$InstalledUpdateDll = '',
    [switch]$ClientOnly,
    [ValidateSet('current', 'installed')][string]$ClientLabel = 'current',
    [string]$ExpectedVersion = '',
    [string]$ExpectedSha256 = '',
    [long]$ExpectedSize = 0,
    [string]$DownloadDirectory = ''
)

# Live, anonymous verification; downloads installers for hashing but never runs
# them. Each DLL is loaded in a fresh .NET Framework process to avoid assembly
# identity unification between the installed and newly built clients.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$repository = 'deepfry666/fourj-pv-toolbox'
$canonical = "https://github.com/$repository/releases/latest/download/stable.json"
$legacy = 'https://github.com/deepfry666/SijiPVToolbox-Releases/releases/latest/download/stable.json'

if ($ClientOnly) {
    if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Client check requires Windows PowerShell / .NET Framework.' }
    $workRoot = Join-Path $projectRoot 'work'
    $downloadRoot = [IO.Path]::GetFullPath($DownloadDirectory)
    if (-not $downloadRoot.StartsWith($workRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) { throw 'Downloads must stay under project work/.' }
    New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
    $assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($UpdateDll))
    $sources = @([StructuralToolbox.Update.UpdateSourceResolver]::ResolveAll())
    $resolved = [StructuralToolbox.Update.UpdateSourceResolver]::Resolve()
    if ($sources.Count -ne 1 -or $sources[0] -ne $resolved -or
        ($resolved -ne $canonical -and ($ClientLabel -ne 'installed' -or $resolved -ne $legacy))) {
        throw "Unexpected client update source: $resolved"
    }
    $service = New-Object StructuralToolbox.Update.UpdateService
    $manifest = $service.FetchManifest([Uri]$resolved)
    $legacyManifest = $service.FetchManifest([Uri]$legacy)
    foreach ($item in @($manifest, $legacyManifest)) {
        if ($item.Version -ne [Version]$ExpectedVersion -or
            $item.Channel -ne 'stable' -or $item.SizeBytes -ne $ExpectedSize -or
            $item.Sha256 -ne $ExpectedSha256 -or $item.PackageUrl.Scheme -ne 'https') {
            throw 'Client manifest does not match the public latest Release.'
        }
        $expectedFile = "FourJ_PV_Toolbox_Setup_v$ExpectedVersion.exe"
        $packagePath = [Uri]::UnescapeDataString($item.PackageUrl.AbsolutePath)
        if ($item.PackageUrl.Host -ne 'github.com' -or
            $packagePath -notmatch ('^/deepfry666/(fourj-pv-toolbox|SijiPVToolbox-Releases)/releases/download/v' +
                [regex]::Escape($ExpectedVersion) + '/' + [regex]::Escape($expectedFile) + '$')) {
            throw 'Installer URL does not correspond to the versioned GitHub Release asset.'
        }
        if ($item.IsNewerThan([Version]$ExpectedVersion) -or
            -not $item.IsNewerThan([Version]'1.0.0')) { throw 'Client version comparison failed.' }
    }
    $download = $service.DownloadInstaller($manifest, $null, $downloadRoot)
    $hash = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $ExpectedSha256 -or (Get-Item -LiteralPath $download).Length -ne $ExpectedSize) {
        throw 'Downloaded installer differs from the Release manifest.'
    }
    [ordered]@{
        client = $ClientLabel; dll = $assembly.Location; dllVersion = $assembly.GetName().Version.ToString()
        dllSha256 = (Get-FileHash -LiteralPath $assembly.Location -Algorithm SHA256).Hash.ToLowerInvariant()
        manifestUrl = $resolved; version = $manifest.Version.ToString(); packageUrl = $manifest.PackageUrl.AbsoluteUri
        size = $ExpectedSize; sha256 = $hash; legacyCompatible = $true; installerExecuted = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $downloadRoot 'result.json') -Encoding UTF8
    Write-Host "UPDATE_CLIENT=$ClientLabel SOURCE=PASS LEGACY_REDIRECT=PASS VERSION=$ExpectedVersion DOWNLOAD=PASS"
    return
}

if ([String]::IsNullOrWhiteSpace($UpdateDll)) {
    $UpdateDll = Join-Path $projectRoot 'bundle/ZhStructureToolbox.bundle/Contents/Windows/R23.1/StructuralToolbox.Update.dll'
}
if (-not (Test-Path -LiteralPath $UpdateDll -PathType Leaf)) { throw 'Build scripts/build.ps1 before running this check.' }
if ([String]::IsNullOrWhiteSpace($InstalledUpdateDll)) {
    $installedRoot = Join-Path $env:ProgramFiles 'Autodesk/ApplicationPlugins/ZhStructureToolbox.bundle'
    $installedCandidates = @(
        (Join-Path $installedRoot 'Contents/Windows/Legacy/R23.1/StructuralToolbox.Update.dll'),
        (Join-Path $installedRoot 'Contents/Windows/R23.1/StructuralToolbox.Update.dll')
    )
    $InstalledUpdateDll = @($installedCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1)
}
$runRoot = Join-Path $projectRoot ('work/github-update-channel-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$headers = @{ 'User-Agent' = 'FourJ-PV-Toolbox-channel-check'; Accept = 'application/vnd.github+json' }
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/releases/latest" -Headers $headers -TimeoutSec 60
if ($release.draft -or $release.prerelease) { throw 'Latest Release is not public/stable.' }
$release | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $runRoot 'latest-release.json') -Encoding UTF8
$manifestPath = Join-Path $runRoot 'stable.json'
Invoke-WebRequest -Uri $canonical -OutFile $manifestPath -TimeoutSec 60
Invoke-WebRequest -Uri $legacy -OutFile (Join-Path $runRoot 'legacy-stable.json') -TimeoutSec 60
if ((Get-FileHash -LiteralPath $manifestPath).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $runRoot 'legacy-stable.json')).Hash) {
    throw 'Old and new anonymous manifest endpoints differ.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($release.tag_name -ne ('v' + $manifest.version) -or $manifest.channel -ne 'stable' -or
    @($manifest.mirrors).Count -ne 0) { throw 'Stable manifest does not match latest Release.' }
$installer = @($release.assets | Where-Object name -eq "FourJ_PV_Toolbox_Setup_v$($manifest.version).exe")
if ($installer.Count -ne 1 -or $installer[0].size -ne $manifest.sizeBytes) {
    throw 'Release installer metadata and stable manifest disagree.'
}
foreach ($name in @('stable.json', 'release.json', 'sha256.txt')) {
    $asset = @($release.assets | Where-Object name -eq $name)
    if ($asset.Count -ne 1) { throw "Missing or duplicate Release asset: $name" }
    if ($name -ne 'stable.json') { Invoke-WebRequest -Uri $asset[0].browser_download_url -OutFile (Join-Path $runRoot $name) -TimeoutSec 60 }
}
$checksum = Get-Content -LiteralPath (Join-Path $runRoot 'sha256.txt') -Raw
$record = Get-Content -LiteralPath (Join-Path $runRoot 'release.json') -Raw | ConvertFrom-Json
if ($record.version -ne $manifest.version -or $record.sha256 -ne $manifest.sha256 -or
    $record.sizeBytes -ne $manifest.sizeBytes -or
    $checksum -notmatch [regex]::Escape($manifest.sha256)) { throw 'Release records/checksums disagree.' }
$windowsPowerShell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
$clients = @([pscustomobject]@{ Label = 'current'; Dll = $UpdateDll })
if (-not [String]::IsNullOrWhiteSpace($InstalledUpdateDll) -and
    (Test-Path -LiteralPath $InstalledUpdateDll -PathType Leaf)) {
    $clients += [pscustomobject]@{ Label = 'installed'; Dll = $InstalledUpdateDll }
}
foreach ($client in $clients) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath,
        '-ClientOnly', '-ClientLabel', $client.Label, '-UpdateDll', $client.Dll,
        '-ExpectedVersion', $manifest.version, '-ExpectedSha256', $manifest.sha256,
        '-ExpectedSize', $manifest.sizeBytes, '-DownloadDirectory', (Join-Path $runRoot $client.Label))
    & $windowsPowerShell @arguments
    if ($LASTEXITCODE -ne 0) { throw "Update client check failed: $($client.Label)" }
}
Write-Host "UPDATE_CHANNEL=PASS RELEASE=$($release.html_url)"
Write-Host "VERIFICATION_DIR=$runRoot"

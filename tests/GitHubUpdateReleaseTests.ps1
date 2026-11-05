$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$projectRoot = Split-Path -Parent $PSScriptRoot
$workRoot = Join-Path $projectRoot ('work\github-update-release-tests-' + [guid]::NewGuid().ToString('N'))
$passed = 0

function Assert-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    $script:passed++
}

try {
    # Use a disposable source repository and versioned PE fixture, never a
    # historical private commit or an installer absent from a clean checkout.
    $fixtureRoot = Join-Path $workRoot 'fixture'
    $sourceFolder = Join-Path $fixtureRoot 'src\StructuralToolbox\Properties'
    $scriptFolder = Join-Path $fixtureRoot 'scripts\update-service'
    New-Item -ItemType Directory -Path $sourceFolder, $scriptFolder -Force | Out-Null
    $assemblyInfo = Join-Path $sourceFolder 'AssemblyInfo.cs'
    '[assembly: System.Reflection.AssemblyVersion("1.0.3.0")]' |
        Set-Content -LiteralPath $assemblyInfo -Encoding UTF8
    $fixtureSource = Join-Path $fixtureRoot 'InstallerFixture.cs'
    @'
[assembly: System.Reflection.AssemblyFileVersion("1.0.3.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.3.0")]
public static class Fixture { public static void Main() {} }
'@ | Set-Content -LiteralPath $fixtureSource -Encoding UTF8
    $installer = Join-Path $fixtureRoot 'installer.exe'
    & 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe "/out:$installer" $fixtureSource
    if ($LASTEXITCODE -ne 0) { throw 'Cannot compile installer metadata fixture.' }
    & git -C $fixtureRoot init -q
    & git -C $fixtureRoot add src
    & git -C $fixtureRoot -c user.name=Test -c user.email=test@example.invalid commit -qm fixture
    if ($LASTEXITCODE -ne 0) { throw 'Cannot commit source fixture.' }
    $prepareScript = Join-Path $scriptFolder 'New-UpdateRelease.ps1'
    Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\update-service\New-UpdateRelease.ps1') -Destination $prepareScript
    $notes = Join-Path $fixtureRoot 'notes.md'
    'Test release preparation only.' | Set-Content -LiteralPath $notes -Encoding UTF8
    $preparedRoot = Join-Path $workRoot 'prepared'
    & $prepareScript `
        -Version 1.0.3 -InstallerPath $installer -SourceRef HEAD `
        -NotesPath $notes -ReleaseDate ([datetime]'2026-09-16') `
        -OutputDirectory $preparedRoot | Out-Null

    $assets = Join-Path $preparedRoot 'github'
    $manifest = Get-Content -LiteralPath (Join-Path $assets 'stable.json') `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    $expectedName = 'FourJ_PV_Toolbox_Setup_v1.0.3.exe'
    $expectedUrl = 'https://github.com/deepfry666/fourj-pv-toolbox/releases/download/v1.0.3/' + $expectedName
    Assert-Condition ($manifest.url -eq $expectedUrl) `
        'stable manifest uses the versioned GitHub release asset'
    Assert-Condition (@($manifest.mirrors).Count -eq 0) `
        'stable manifest has no VPS or other mirrors'
    Assert-Condition (-not (Test-Path -LiteralPath (Join-Path $preparedRoot 'vps'))) `
        'release preparation does not create a VPS site'
    foreach ($file in @($expectedName, 'stable.json', 'sha256.txt', 'release.json', 'RELEASE.md')) {
        Assert-Condition (Test-Path -LiteralPath (Join-Path $assets $file) -PathType Leaf) `
            "GitHub release asset exists: $file"
    }
    $actualHash = (Get-FileHash -LiteralPath (Join-Path $assets $expectedName) -Algorithm SHA256).Hash
    Assert-Condition ($actualHash -eq $manifest.sha256) `
        'manifest SHA-256 matches the GitHub installer asset'
    Assert-Condition ((Get-Item -LiteralPath (Join-Path $assets $expectedName)).Length -eq
        [long]$manifest.sizeBytes) 'manifest size matches the GitHub installer asset'

    $resolver = Get-Content -LiteralPath (Join-Path $projectRoot `
        'src\StructuralToolbox.Update\Core\UpdateSourceResolver.cs') -Raw -Encoding UTF8
    Assert-Condition ($resolver.Contains(
        'https://github.com/deepfry666/fourj-pv-toolbox/releases/latest/download/stable.json')) `
        'client uses the GitHub latest stable manifest'
    Assert-Condition (-not $resolver.Contains('download.imfourj.cn')) `
        'client no longer contains the VPS manifest URL'

    $prepare = Get-Content -LiteralPath (Join-Path $projectRoot `
        'scripts\update-service\New-UpdateRelease.ps1') -Raw -Encoding UTF8
    foreach ($retired in @('VpsBaseUrl', 'New-UpdateSite.ps1', 'download.imfourj.cn')) {
        Assert-Condition (-not $prepare.Contains($retired)) `
            "release preparation does not use retired value: $retired"
    }
    $publish = Get-Content -LiteralPath (Join-Path $projectRoot `
        'scripts\update-service\Publish-UpdateRelease.ps1') -Raw -Encoding UTF8
    foreach ($retired in @('SshHost', 'RemoteRoot', 'Deploy-UpdateSite.ps1')) {
        Assert-Condition (-not $publish.Contains($retired)) `
            "release publication does not use retired value: $retired"
    }

    Write-Host "PASS $passed GitHub-only update release tests"
} finally {
    if (Test-Path -LiteralPath $workRoot -PathType Container) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}

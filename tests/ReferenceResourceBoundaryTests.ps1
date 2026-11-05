$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$tracked = @(& git -C $projectRoot ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the source file inventory.' }
$forbidden = @($tracked | Where-Object { $_ -match '\.(dwg|dxf|dwt|bak|zip|7z|rar|exe|dll|pdb)$' })
if ($forbidden.Count) { throw "Drawings or packaged payloads must not enter source history: $($forbidden -join ', ')" }
foreach ($project in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Recurse -File -Filter '*.csproj') {
    [xml]$xml = Get-Content -LiteralPath $project.FullName -Raw
    foreach ($resource in $xml.SelectNodes('//*[@Include]')) {
        if ($resource.Include -match '\.(dwg|dxf|dwt|zip|7z|rar)$') {
            throw "Project embeds or includes a reference drawing: $($project.FullName)"
        }
    }
}
$moduleRoot = Join-Path $projectRoot 'src\StructuralToolbox.SupportDetail'
foreach ($file in Get-ChildItem -LiteralPath $moduleRoot -Recurse -File -Filter '*.cs') {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    if ($text -match 'ReadDwgFile\(|GetManifestResourceStream\(|SupportDetailFixedDetails') {
        throw "Support-detail module still loads an external reference resource: $($file.FullName)"
    }
}
Write-Host 'REFERENCE_RESOURCE_BOUNDARY=PASS'

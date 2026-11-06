param(
    [string]$AutoCADDir = "D:\cad\AutoCAD 2020",
    [ValidateSet('Blank', 'RailsOnly')][string]$Kind = 'Blank'
)

$ErrorActionPreference = "Stop"
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$runRoot = Join-Path $projectRoot "work\generated-test-drawing"
if ($Kind -ne 'Blank') { $runRoot = Join-Path $runRoot $Kind }
$output = Join-Path $runRoot "blank-mm.dwg"
$receipt = Join-Path $runRoot "receipt.json"
$source = Join-Path $projectRoot "tests\GeneratedTestDrawing.cs"
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if ((Test-Path -LiteralPath $receipt) -and (Test-Path -LiteralPath $output)) {
    $record = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
    if ($record.sourceHash -eq $sourceHash -and $record.drawingHash -eq
        (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash) {
        return $output
    }
}
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$console = Join-Path $AutoCADDir "accoreconsole.exe"
$assembly = Join-Path $runRoot "GeneratedTestDrawing.dll"
foreach ($path in @($compiler, $console)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing tool: $path" }
}
$buildOutput = & $compiler /nologo /target:library /platform:x64 `
    "/out:$assembly" "/reference:$(Join-Path $AutoCADDir 'acmgd.dll')" `
    "/reference:$(Join-Path $AutoCADDir 'acdbmgd.dll')" `
    "/reference:$(Join-Path $AutoCADDir 'accoremgd.dll')" $source 2>&1
if ($LASTEXITCODE -ne 0) { throw "Fixture compilation failed: $buildOutput" }

# A temporary free drive makes the AutoCAD script independent of Unicode paths.
$drive = @('P','R','S','T','U','V','W','X','Y','Z') |
    Where-Object { -not (Test-Path ($_ + ':\')) } | Select-Object -First 1
if (-not $drive) { throw "No free drive is available for the isolated CAD fixture." }
& subst.exe ($drive + ':') $runRoot
if ($LASTEXITCODE -ne 0) { throw "Cannot map the fixture directory." }
$previousOutput = $env:FOURJ_TEST_DRAWING_OUTPUT
$previousKind = $env:FOURJ_TEST_DRAWING_KIND
try {
    $env:FOURJ_TEST_DRAWING_OUTPUT = $output
    $env:FOURJ_TEST_DRAWING_KIND = $Kind
    $script = Join-Path $runRoot "generate.scr"
    [IO.File]::WriteAllLines($script, @(
        '(setvar "SECURELOAD" 0)', '_.NETLOAD',
        ($drive + ':/GeneratedTestDrawing.dll'), 'FOURJGENERATETESTDRAWING',
        '_.QUIT', '_Y', ''
    ), [Text.Encoding]::ASCII)
    $stdout = Join-Path $runRoot "console.log"
    $stderr = Join-Path $runRoot "console-error.log"
    $arguments = '/s "' + $drive + ':/generate.scr" /l en-US /isolate FOURJ_FIXTURE "' + $drive + ':/profile"'
    $process = Start-Process -FilePath $console -ArgumentList $arguments -WindowStyle Hidden `
        -WorkingDirectory $runRoot -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $null = $process.Handle
    if (-not $process.WaitForExit(120000)) {
        $process.Kill()
        throw "CAD fixture generation timed out. See $stdout"
    }
    $log = (Get-Content -LiteralPath $stdout -Raw).Replace([string][char]0, '')
    if ($process.ExitCode -ne 0 -or -not $log.Contains('FOURJ_GENERATED_DRAWING=PASS') -or
        -not (Test-Path -LiteralPath $output -PathType Leaf)) {
        throw "CAD fixture generation failed (exit $($process.ExitCode)). See $stdout and $stderr"
    }
    [ordered]@{ sourceHash = $sourceHash; drawingHash =
        (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash } |
        ConvertTo-Json | Set-Content -LiteralPath $receipt -Encoding UTF8
} finally {
    $env:FOURJ_TEST_DRAWING_OUTPUT = $previousOutput
    $env:FOURJ_TEST_DRAWING_KIND = $previousKind
    & subst.exe ($drive + ':') /D | Out-Null
}
return $output

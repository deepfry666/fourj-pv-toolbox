param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testDirectory = Join-Path $projectRoot 'work/tests'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $testDirectory 'DrawingTranslationLayoutTests.exe'
$sources = @(
    (Join-Path $projectRoot 'src/StructuralToolbox.DrawingTranslation/Core/TranslationLayout.cs'),
    (Join-Path $projectRoot 'tests/DrawingTranslationLayoutTests.cs')
)
& $compiler /nologo /target:exe /optimize+ /utf8output "/out:$output" $sources
if ($LASTEXITCODE -ne 0) { throw 'Translation layout test build failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Translation layout tests failed.' }

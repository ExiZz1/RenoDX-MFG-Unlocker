param([switch]$NoBuild)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$project=Join-Path $root 'tests/NvidiaComponentScannerChecks/NvidiaComponentScannerChecks.csproj'
$assembly=Join-Path $root 'tests/NvidiaComponentScannerChecks/bin/Release/net8.0-windows/NvidiaComponentScannerChecks.dll'
$logs=Join-Path $root 'artifacts/nonvisual-tests'
New-Item -ItemType Directory -Path $logs -Force|Out-Null
Push-Location $root
try{
 if(!$NoBuild){
  & dotnet restore $project -m:1 *> (Join-Path $logs 'restore.log')
  if($LASTEXITCODE -ne 0){throw 'Test restore failed; see artifacts/nonvisual-tests/restore.log.'}
  & dotnet build $project -c Release --no-restore -m:1 *> (Join-Path $logs 'build.log')
  if($LASTEXITCODE -ne 0){throw 'Test build failed; see artifacts/nonvisual-tests/build.log.'}
 }
 & dotnet $assembly --nonvisual *> (Join-Path $logs 'checks.log')
 $exit=$LASTEXITCODE
 $text=Get-Content (Join-Path $logs 'checks.log') -Raw
 $summary=[regex]::Match($text,'Passed (\d+) automated checks; failed (\d+); skipped (\d+)\.')
 if(!$summary.Success){throw 'Missing nonvisual check summary; see artifacts/nonvisual-tests/checks.log.'}
 $result=[ordered]@{Passed=[int]$summary.Groups[1].Value;Failed=[int]$summary.Groups[2].Value;Skipped=[int]$summary.Groups[3].Value;VisualTestsRun=$false;RealGamesModified=$false}
 $result|ConvertTo-Json|Set-Content (Join-Path $logs 'results.json') -Encoding utf8
 Get-Content (Join-Path $logs 'checks.log')|Where-Object {$_ -match '^(FAIL|SKIP) '}
 Write-Output $summary.Value
 exit $exit
}finally{Pop-Location}
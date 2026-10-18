$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$codexCommand = Get-Command codex.exe -ErrorAction SilentlyContinue
if ($codexCommand) { $env:Mentor__CodexExecutable = $codexCommand.Source }
Write-Host 'Mentor: http://127.0.0.1:5178 (ukončení: Ctrl+C)'
dotnet run --project "$PSScriptRoot/src/Mentor/Mentor.csproj" --no-launch-profile
exit $LASTEXITCODE

param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$runId = 'logic-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N')
$dir = Join-Path $root "artifacts\validation\0.2.2\$runId"
New-Item -ItemType Directory -Path $dir -Force | Out-Null
$dll = Join-Path $PSScriptRoot "GarlandForWindows.Tests\bin\$Configuration\net10.0\GarlandForWindows.Tests.dll"
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$start = [DateTimeOffset]::Now
$timer = [Diagnostics.Stopwatch]::StartNew()
$info = [Diagnostics.ProcessStartInfo]::new($dotnet)
$info.ArgumentList.Add($dll)
$info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($info)
$processIdValue = $process.Id
$outTask = $process.StandardOutput.ReadToEndAsync(); $errTask = $process.StandardError.ReadToEndAsync()
if (-not $process.WaitForExit(60000)) { $process.Kill(); $process.WaitForExit(); $code = 1 } else { $code = $process.ExitCode }
$timer.Stop(); $stdout = $outTask.GetAwaiter().GetResult(); $stderr = $errTask.GetAwaiter().GetResult()
[IO.File]::WriteAllText((Join-Path $dir 'stdout.txt'), $stdout)
[IO.File]::WriteAllText((Join-Path $dir 'stderr.txt'), $stderr)
$passes = [regex]::Matches($stdout, '(?m)^PASS ').Count
$source = @{}
Get-ChildItem (Join-Path $root 'src\GarlandForWindows'), (Join-Path $PSScriptRoot 'GarlandForWindows.Tests'), (Join-Path $PSScriptRoot 'GarlandForWindows.DesktopTests') -File | Where-Object { $_.Extension -in '.cs','.csproj' } | ForEach-Object { $source[[IO.Path]::GetRelativePath($root, $_.FullName)] = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash }
$sdk = & $dotnet --version
$runtimes = (& $dotnet --list-runtimes) -join '; '
$summary = [ordered]@{ runId=$runId; started=$start.ToString('o'); ended=[DateTimeOffset]::Now.ToString('o'); durationMs=$timer.ElapsedMilliseconds; os=[Runtime.InteropServices.RuntimeInformation]::OSDescription; runtime=$runtimes; sdk=$sdk; configuration=$Configuration; command="$dotnet $dll"; processId=$processIdValue; testDll=$dll; testDllSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $dll).Hash; sourceHashes=$source; results=@{ logic= $(if ($code -eq 0) {'PASS'} else {'FAIL'}) }; counts=@{ PASS=$passes; FAIL=$(if ($code -eq 0) {0} else {1}) }; exitCode=$code }
$summary | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $dir 'summary.json')
Write-Output "RUN $runId exit=$code PASS=$passes directory=$dir"
exit $code

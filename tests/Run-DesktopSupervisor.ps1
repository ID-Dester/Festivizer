param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$Interactive,
    [ValidateSet('','hang','crash','pass')][string]$SelfTest = '',
    [int]$TestBudgetSeconds = 0,
    [string]$BudgetReason = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$runId = 'desktop-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N')
$dir = Join-Path $root "artifacts\validation\0.2.3\$runId"
$budget = if ($Interactive) { 90 } else { 30 }
if ($TestBudgetSeconds -ne 0) {
    if ($SelfTest -eq '' -or $TestBudgetSeconds -lt 1 -or [string]::IsNullOrWhiteSpace($BudgetReason)) { throw 'A budget override requires a self-test, a positive value and an explicit reason.' }
    $budget = $TestBudgetSeconds
}
New-Item -ItemType Directory -Path $dir | Out-Null
$summaryPath = Join-Path $dir 'supervisor.json'
$state = [ordered]@{ runId=$runId; started=[DateTimeOffset]::Now.ToString('o'); configuration=$Configuration; budgetSeconds=$budget; stopBudgetSeconds=5; diagnosticBudgetSeconds=5; budgetReason=$BudgetReason; status='STARTING'; timeout=$false; childPid=$null; childStart=$null; childExitCode=$null; stopAttempt=$null; stopConfirmed=$false; childWindowsBeforeStop=$null; survivingWindows=$null; lastProgress=$null; cleanup='unknown'; error=$null }
function Save-State {
    $json = $state | ConvertTo-Json -Depth 12
    $write = [IO.File]::WriteAllTextAsync($summaryPath, $json)
    if (-not $write.Wait(5000)) { throw 'Supervisor diagnostic write exceeded 5 seconds' }
    $null = $write.GetAwaiter().GetResult()
}
Save-State
Write-Output "RUN $runId directory=$dir childBudget=${budget}s stopBudget=5s diagnosticBudget=5s"
$process = $null
try {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class SupervisorWindows {
  private delegate bool Callback(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] private static extern bool EnumWindows(Callback f, IntPtr p);
  [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  public static long[] ForPid(uint pid) { var result = new List<long>(); if (!EnumWindows((h,p) => { GetWindowThreadProcessId(h,out uint owner); if (owner == pid) result.Add(h.ToInt64()); return true; }, IntPtr.Zero)) throw new InvalidOperationException("EnumWindows failed"); return result.ToArray(); }
}
'@
    $dll = Join-Path $PSScriptRoot "GarlandForWindows.DesktopTests\bin\$Configuration\net10.0-windows\GarlandForWindows.DesktopTests.dll"
    if (-not (Test-Path -LiteralPath $dll)) { throw "Desktop test DLL missing: $dll" }
    $info = [Diagnostics.ProcessStartInfo]::new('C:\Program Files\dotnet\dotnet.exe')
    $info.ArgumentList.Add($dll)
    if ($Interactive) { $info.ArgumentList.Add('--interactive') }
    if ($SelfTest -ne '') { $info.ArgumentList.Add("--supervisor-test-$SelfTest") }
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.Environment['FESTIVIZER_RUN_ID'] = $runId; $info.Environment['FESTIVIZER_RUN_DIR'] = $dir
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($info)
    if ($null -eq $process) { throw 'Process.Start returned null' }
    $state.childPid = $process.Id; $state.childStart = $process.StartTime.ToUniversalTime().ToString('o'); $state.status = 'RUNNING'; Save-State
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    $remaining = [Math]::Max(0, $budget * 1000 - [int]$clock.ElapsedMilliseconds)
    if (-not $process.WaitForExit($remaining)) {
        $state.timeout = $true; $state.status = 'FAIL'; $state.stopAttempt = 'Kill on captured child process object'
        try { $state.childWindowsBeforeStop = @([SupervisorWindows]::ForPid([uint32]$process.Id)) } catch { $state.error = "window enumeration failed: $($_.Exception.Message)" }
        try { $process.Kill() } catch { $state.stopAttempt += "; error: $($_.Exception.Message)" }
        $state.stopConfirmed = $process.WaitForExit(5000)
        if ($null -ne $state.childWindowsBeforeStop) { $state.survivingWindows = @($state.childWindowsBeforeStop | Where-Object { [SupervisorWindows]::IsWindow([IntPtr]$_) }) }
        $state.cleanup = 'finally and explicit resource release cannot be confirmed after forced termination'
    } else { $state.stopConfirmed = $true; $state.cleanup = 'see child summary' }
    if ($process.HasExited) { $state.childExitCode = $process.ExitCode }
    if ($stdout.Wait(1000)) { [IO.File]::WriteAllText((Join-Path $dir 'supervisor-stdout.txt'), $stdout.Result) }
    if ($stderr.Wait(1000)) { [IO.File]::WriteAllText((Join-Path $dir 'supervisor-stderr.txt'), $stderr.Result) }
    $progress = Join-Path $dir 'progress.json'
    if (Test-Path -LiteralPath $progress) { try { $state.lastProgress = Get-Content -Raw -LiteralPath $progress | ConvertFrom-Json } catch { $state.lastProgress = 'unreadable progress' } }
    $childSummary = Join-Path $dir 'summary.json'
    if (-not $state.timeout -and $state.stopConfirmed -and (Test-Path -LiteralPath $childSummary)) {
        $child = Get-Content -Raw -LiteralPath $childSummary | ConvertFrom-Json
        $required = @('desktop','low_level','foreground','popup','controller','cleanup')
        $resultsValid = $null -ne $child.results
        foreach ($name in $required) { if ($null -eq $child.results.$name) { $resultsValid = $false } }
        $statuses = @($child.results.PSObject.Properties | ForEach-Object { $_.Value })
        if ($state.childExitCode -eq 0 -and ($statuses.Count -lt $required.Count -or @($statuses | Where-Object { $_ -ne 'PASS' }).Count -ne 0)) { $resultsValid = $false }
        if ($resultsValid -and $state.childExitCode -eq $child.exitCode -and $state.childExitCode -in 0,1,2,3) {
            $state.status = @('PASS','FAIL','SKIP','BLOCKED')[$state.childExitCode]
        } else { $state.status = 'FAIL'; $state.error = 'child exit code and summary do not match' }
    } elseif (-not $state.timeout) { $state.status = 'FAIL'; $state.error = 'child summary missing or process exit unconfirmed' }
} catch {
    $state.status = 'FAIL'; $state.error = $_.Exception.ToString()
    if ($null -ne $process) {
        try { if (-not $process.HasExited) { $state.stopAttempt = 'Kill after supervisor error'; $process.Kill(); $state.stopConfirmed = $process.WaitForExit(5000) } } catch { $state.stopAttempt += "; error: $($_.Exception.Message)" }
    }
}
finally {
    $state.ended = [DateTimeOffset]::Now.ToString('o')
    try { Save-State } catch { [Console]::Error.WriteLine("Cannot save supervisor diagnostics: $_"); exit 1 }
    if ($null -ne $process) { $process.Dispose() }
}
Write-Output "RESULT $($state.status) timeout=$($state.timeout) childExit=$($state.childExitCode) directory=$dir"
switch ($state.status) { 'PASS' { exit 0 } 'SKIP' { exit 2 } 'BLOCKED' { exit 3 } default { exit 1 } }

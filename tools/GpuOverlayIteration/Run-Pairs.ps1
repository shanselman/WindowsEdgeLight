param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$Output,
    [ValidateSet('control','gpu')][string]$TreatmentMode = 'gpu',
    [int]$Pairs = 3,
    [int]$Seconds = 12,
    [string]$Lane = 'hover'
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path $Executable).Path
if (Test-Path $Output) { throw "Refusing to overwrite: $Output" }
$Output = (New-Item $Output -ItemType Directory).FullName
$settingsPaths = @(
    (Join-Path $env:APPDATA 'WindowsEdgeLight\settings.json'),
    (Join-Path $env:LOCALAPPDATA 'WindowsEdgeLight\settings.json')
)
function Get-SettingsHashes {
    foreach ($path in $settingsPaths) {
        if (Test-Path $path) { Get-FileHash $path -Algorithm SHA256 | Select-Object Path,Hash }
    }
}
$beforeSettings = @(Get-SettingsHashes)
$beforeSettings | ConvertTo-Json | Set-Content (Join-Path $Output 'settings-before.json')
try {
    for ($pair = 1; $pair -le $Pairs; $pair++) {
        $order = @('control', 'treatment')
        if ($pair % 2 -eq 0) { $order = @('treatment', 'control') }
        foreach ($arm in $order) {
            $mode = 'control'
            if ($arm -eq 'treatment') { $mode = $TreatmentMode }
            $label = ('pair-{0:d2}-{1}' -f $pair,$arm)
            $target = Join-Path $Output $label
            $process = $null
            $external = [Collections.Generic.List[object]]::new()
            try {
                $process = Start-Process $Executable -ArgumentList @("`"$target`"", $mode, $Lane, $Seconds) `
                    -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $Output "$label.stdout.txt") `
                    -RedirectStandardError (Join-Path $Output "$label.stderr.txt")
                $watch = [Diagnostics.Stopwatch]::StartNew()
                $dwmId = (Get-Process dwm | Where-Object SessionId -eq $process.SessionId | Select-Object -First 1).Id
                while (-not $process.HasExited) {
                    if ($watch.Elapsed.TotalSeconds -gt $Seconds + 180) { throw "Benchmark timed out: $label" }
                    try {
                        $counters = Get-Counter '\Process(dwm)\% Processor Time', '\GPU Engine(*)\Utilization Percentage' -MaxSamples 1 -ErrorAction Stop
                        $selected = @($counters.CounterSamples | Where-Object {
                            $_.Path -like '*\process(dwm)\*' -or
                            $_.InstanceName -like "pid_$($process.Id)_*" -or
                            $_.InstanceName -like "pid_$($dwmId)_*"
                        } | Select-Object Path,InstanceName,CookedValue,Status)
                        $external.Add([pscustomobject]@{ utc=$counters.Timestamp.ToUniversalTime().ToString('O'); elapsedSeconds=$watch.Elapsed.TotalSeconds; values=$selected })
                    }
                    catch {
                        $external.Add([pscustomobject]@{
                            utc=[DateTime]::UtcNow.ToString('O'); elapsedSeconds=$watch.Elapsed.TotalSeconds
                            error=$_.Exception.Message
                        })
                        Start-Sleep -Seconds 1
                    }
                    $process.Refresh()
                }
                $process.WaitForExit()
                if ($process.ExitCode -ne 0) { throw "Failed: $label, exit=$($process.ExitCode). See error.txt/stdout/stderr." }
                if (-not (Test-Path (Join-Path $target 'result.json'))) { throw "Missing result: $label" }
                Write-Output "Completed $label ($mode/$Lane)"
            }
            finally {
                if ($process -and -not $process.HasExited) {
                    Stop-Process -Id $process.Id
                    $process.WaitForExit()
                }
                $external | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Output "$label.external.json")
                if ($process) { $process.Dispose() }
            }
        }
    }
}
finally {
    $afterSettings = @(Get-SettingsHashes)
    $afterSettings | ConvertTo-Json | Set-Content (Join-Path $Output 'settings-after.json')
    if ((Compare-Object $beforeSettings $afterSettings -Property Path,Hash)) {
        throw "Settings changed during run; investigate concurrent user action. No automatic restore."
    }
}

# Evaluates JavaScript in YTLite's WebView on a device/emulator (debug builds only) over adb + DevTools.
# Usage: powershell -File android\tools\cdp-eval.ps1 -Serial emulator-5554 -Expr "location.href"
param(
    [Parameter(Mandatory)] [string]$Serial,
    [Parameter(Mandatory)] [string]$Expr,
    [int]$Port = 9333,
    [string]$Adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
)
$ErrorActionPreference = 'Stop'

$appPid = ((& $Adb -s $Serial shell pidof app.ytlite 2>$null) -join '').Trim()
if (-not $appPid -or $appPid -notmatch '^\d+$') {
    # Old Android (4.x) has no pidof: take the PID column from ps.
    $line = (& $Adb -s $Serial shell ps) | Where-Object { $_ -match '\sapp\.ytlite\s*$' } | Select-Object -First 1
    $appPid = if ($line) { ($line -split '\s+')[1] } else { '' }
}
if (-not $appPid) { throw 'YTLite is not running on the device.' }
& $Adb -s $Serial forward "tcp:$Port" "localabstract:webview_devtools_remote_$appPid" | Out-Null

$pages = Invoke-RestMethod -UseBasicParsing "http://127.0.0.1:$Port/json"
$page = @($pages | Where-Object { $_.type -eq 'page' -and $_.attached -ne $false -and $_.webSocketDebuggerUrl })
if ($page.Count -eq 0) { throw 'No inspectable page.' }
# Prefer the visible tab.
$target = ($page | Where-Object { $_.description -match '"visible":true' } | Select-Object -First 1)
if (-not $target) { $target = $page[0] }

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ws.ConnectAsync([Uri]$target.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()
$msg = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $Expr; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Depth 5 -Compress
$bytes = [Text.Encoding]::UTF8.GetBytes($msg)
$ws.SendAsync([ArraySegment[byte]]$bytes, 'Text', $true, [Threading.CancellationToken]::None).Wait()

$buf = New-Object byte[] 1048576; $sb = New-Object Text.StringBuilder
while ($true) {
    $sb.Clear() | Out-Null
    do {
        $r = $ws.ReceiveAsync([ArraySegment[byte]]$buf, [Threading.CancellationToken]::None).Result
        $sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count)) | Out-Null
    } while (-not $r.EndOfMessage)
    $resp = $sb.ToString() | ConvertFrom-Json
    if ($resp.id -eq 1) { break }
}
$ws.Dispose()
if ($resp.result.exceptionDetails) { "JS error: " + $resp.result.exceptionDetails.exception.description }
else { $v = $resp.result.result.value; if ($v -is [string]) { $v } else { $v | ConvertTo-Json -Depth 6 } }

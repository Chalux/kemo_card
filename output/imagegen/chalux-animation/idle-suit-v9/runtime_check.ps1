param([Parameter(Mandatory=$true)][string] $RequestFile, [string] $CaptureFile)
$ErrorActionPreference = 'Stop'
$projectKey = (Resolve-Path -LiteralPath '.').Path.Replace('\','/').ToLowerInvariant().TrimEnd('/')
$entry = Get-ChildItem -LiteralPath "$env:APPDATA/godot-mcp-toolkit/entries" -Filter '*.runtime.json' | ForEach-Object {
    Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
} | Where-Object { $_._key -eq $projectKey } | Select-Object -First 1
if (-not $entry) { throw 'No running game registered for this project.' }
$hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($projectKey))).ToLowerInvariant().Substring(0,12)
$tokenPath = "$env:APPDATA/Godot/app_userdata/kemo_card/addons/godot_mcp_toolkit/project_instance_$hash/mcp_token"
$socket = [System.Net.WebSockets.ClientWebSocket]::new()
$cancel = [Threading.CancellationTokenSource]::new(20000)
function Send-Message($message) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($message | ConvertTo-Json -Depth 20 -Compress))
    $socket.SendAsync([ArraySegment[byte]]::new($bytes), [Net.WebSockets.WebSocketMessageType]::Text, $true, $cancel.Token).GetAwaiter().GetResult()
}
function Read-Message {
    $stream = [IO.MemoryStream]::new()
    $buffer = [byte[]]::new(65536)
    do {
        $received = $socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), $cancel.Token).GetAwaiter().GetResult()
        if ($received.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close) { throw 'Runtime closed the connection.' }
        $stream.Write($buffer,0,$received.Count)
    } while (-not $received.EndOfMessage)
    $message = [Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json -Depth 40
    $stream.Dispose()
    return $message
}
try {
    $socket.ConnectAsync([Uri]"ws://127.0.0.1:$($entry.runtime_port)", $cancel.Token).GetAwaiter().GetResult()
    Send-Message @{auth=(Get-Content -LiteralPath $tokenPath -Raw).Trim()}
    $ack = Read-Message
    $request = Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json -AsHashtable
    $request.id = 1
    $request.jsonrpc = '2.0'
    Send-Message $request
    do { $reply = Read-Message } while ($reply.id -ne 1)
    if ($CaptureFile) {
        $payload = $reply.result
        if ($payload.data) { $payload = $payload.data }
        $imageData = $payload.image_base64
        if (-not $imageData) { $imageData = $payload.base64 }
        if (-not $imageData) { throw ('Missing screenshot payload: ' + ($payload.PSObject.Properties.Name -join ',')) }
        [IO.File]::WriteAllBytes((Join-Path (Get-Location) $CaptureFile), [Convert]::FromBase64String($imageData))
        Write-Output "Saved game screenshot: $CaptureFile"
    } else {
        $reply | ConvertTo-Json -Depth 40 -Compress
    }
} finally {
    $socket.Dispose()
    $cancel.Dispose()
}

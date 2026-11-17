param(
    [Parameter(Mandatory = $true)]
    [string] $GodotPath,
    [string] $ResultsDirectory
)

$ErrorActionPreference = 'Stop'
# 测试会刻意让业务 hook 抛异常，Godot 会把它们写到 stderr；PowerShell 会把原生 stderr
# 转成 NativeCommandError，在 Stop 策略下即使全部通过也会报失败。失败判定只认退出码与汇总行。
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$godotExecutable = (Resolve-Path -LiteralPath $GodotPath).Path
if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path $repoRoot '.godot/ui-headless'
}
$null = New-Item -ItemType Directory -Path $ResultsDirectory -Force
$resultsRoot = (Resolve-Path -LiteralPath $ResultsDirectory).Path
$projectPath = Join-Path $repoRoot 'kemo_card.csproj'
$engineLog = Join-Path $resultsRoot 'godot-headless.log'

Push-Location $repoRoot
try {
    dotnet build $projectPath --no-restore -p:IncludeUiHeadlessTests=true --verbosity quiet *> (Join-Path $resultsRoot 'build-headless.log')
    if ($LASTEXITCODE -ne 0) { throw 'UI headless build failed; see build-headless.log.' }

    # Real-time SpriteFrames checks include complete non-looping actions; retain
    # a bounded watchdog while allowing the expanded suite to finish.
    # Godot 的 stderr 里含刻意注入的异常；先降级 ErrorActionPreference 再调用，
    # 避免原生 stderr 变成终止性 NativeCommandError；成败改由退出码与汇总行判定。
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $godotExecutable --headless --path $repoRoot 'res://Tests/kemo_card.Ui.Headless/UiFrameworkHeadlessTests.tscn' --quit-after 2400 *> $engineLog
        $engineExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    $log = Get-Content -LiteralPath $engineLog -Raw
    $summary = [regex]::Match($log, 'UI_HEADLESS_SUMMARY total=(\d+) failed=(\d+)')
    if ($engineExitCode -ne 0 -or -not $summary.Success -or [int]$summary.Groups[1].Value -eq 0 -or [int]$summary.Groups[2].Value -ne 0) {
        throw "UI headless tests failed (exit $engineExitCode); see godot-headless.log."
    }
    Write-Output $summary.Value
}
finally {
    # 默认构建排除测试节点；即使检查失败也恢复正常游戏程序集。
    try {
        dotnet build $projectPath --no-restore --verbosity quiet *> (Join-Path $resultsRoot 'build-normal.log')
        if ($LASTEXITCODE -ne 0) { throw 'Normal game build failed; see build-normal.log.' }
    }
    finally { Pop-Location }
}

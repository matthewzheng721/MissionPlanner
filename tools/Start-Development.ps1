param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\bin\Debug\net461'),
    [string]$RuntimeDirectory = 'D:\Project\GCS-runtime'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $BuildDirectory).Path
$runtime = [IO.Path]::GetFullPath($RuntimeDirectory)
$app = Join-Path $runtime 'app'
$data = Join-Path $runtime 'userdata'
if (!(Test-Path -LiteralPath (Join-Path $source 'MissionPlanner.exe'))) { throw '请先编译 MissionPlanner。' }
if ($app -eq $source -or $app.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw '独立运行目录不能放在编译输出目录内。'
}
# 复制可运行产物，独立目录内的数据不会因再次复制而删除。
New-Item -ItemType Directory -Path $app,$data -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $app -Recurse -Force
# 日常从此入口启动，避免直接打开 exe 时漏掉独立目录参数。
$launcher = @('@echo off', 'cd /d "%~dp0app"', 'start "" "%~dp0app\MissionPlanner.exe" -datadir "%~dp0userdata"') -join [Environment]::NewLine
Set-Content -LiteralPath (Join-Path $runtime '启动开发版.cmd') -Value $launcher -Encoding ascii
Start-Process -FilePath (Join-Path $app 'MissionPlanner.exe') -ArgumentList ('-datadir "' + $data + '"') -WorkingDirectory $app
Write-Output "开发版程序：$app"
Write-Output "开发版数据：$data"

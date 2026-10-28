# 一键发布到 GitHub
# 用法（先登录一次）：
#   & "C:\Program Files\GitHub CLI\gh.exe" auth login
#   .\publish.ps1                 # 用默认仓库名 InputPolish
#   .\publish.ps1 my-repo-name    # 自定义仓库名
#
# 也可以用 Token 免交互：
#   $env:GH_TOKEN = "ghp_xxx"
#   .\publish.ps1

param(
    [string]$Name = "InputPolish",
    [string]$Description = "敲三下空格，AI 帮你把草稿理顺写回任意 Windows 输入框（WinForms 悬浮球 + 全局钩子 + UIA 读写）",
    [ValidateSet("public", "private")][string]$Visibility = "public"
)

$ErrorActionPreference = "Stop"
$git = "C:\Program Files\Git\cmd\git.exe"
$gh = "C:\Program Files\GitHub CLI\gh.exe"

if (-not (Test-Path $gh)) { throw "找不到 GitHub CLI：$gh（winget install --id GitHub.cli -e）" }
if (-not (Test-Path $git)) { throw "找不到 Git：$git（winget install --id Git.Git -e）" }

Set-Location $PSScriptRoot

$auth = & $gh auth status 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    Write-Host "还没登录 GitHub。请先运行：" -ForegroundColor Yellow
    Write-Host "  & '$gh' auth login" -ForegroundColor Cyan
    Write-Host "或设置环境变量 GH_TOKEN 后重跑本脚本。" -ForegroundColor Yellow
    exit 1
}

Write-Host "创建仓库 $Name（$Visibility）..." -ForegroundColor Cyan
& $gh repo create $Name --$Visibility --description $Description --source . --remote origin --push
if ($LASTEXITCODE -ne 0) { throw "gh repo create 失败（仓库名可能已被占用，换一个名字再试）" }

$url = & $gh repo view --json url -q .url
Write-Host ""
Write-Host "完成：$url" -ForegroundColor Green

@echo off
setlocal EnableDelayedExpansion
cd /d %~dp0

echo ============================================================
echo  FigmaSearch - 打包并发布到 GitLab
echo ============================================================
echo.

:: ── 1. 读取 GitLab Token ──────────────────────────────────────
if not exist "gitlab_token.txt" (
    echo [错误] 找不到 gitlab_token.txt，请在项目根目录创建该文件并填入 GitLab API Token。
    pause & exit /b 1
)
set /p GITLAB_TOKEN=<gitlab_token.txt
set GITLAB_TOKEN=%GITLAB_TOKEN: =%
if "%GITLAB_TOKEN%"=="" (
    echo [错误] gitlab_token.txt 为空。
    pause & exit /b 1
)

:: ── 2. 从 .csproj 提取版本号 ──────────────────────────────────
for /f "tokens=*" %%i in ('powershell -NoProfile -Command "(Select-String -Path src\FigmaSearch\FigmaSearch.csproj -Pattern '<Version>(.*?)</Version>').Matches[0].Groups[1].Value"') do set VERSION=%%i
if "%VERSION%"=="" (
    echo [错误] 无法从 FigmaSearch.csproj 读取版本号。
    pause & exit /b 1
)
echo [信息] 当前版本：%VERSION%
echo.

:: ── 3. 读取 Release Notes ────────────────────────────────────
if not exist "RELEASE_NOTES.md" (
    echo [错误] 找不到 RELEASE_NOTES.md，请创建该文件并填写本次更新内容。
    pause & exit /b 1
)

:: ── 4. dotnet publish ────────────────────────────────────────
echo [步骤 1/4] 编译发布...
dotnet publish src\FigmaSearch\FigmaSearch.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained false ^
    -p:PublishSingleFile=true ^
    -p:Version=%VERSION% ^
    -o installer\publish
if errorlevel 1 (
    echo [错误] dotnet publish 失败。
    pause & exit /b 1
)
echo [完成] 编译成功。
echo.

:: ── 5. NSIS 打包 ─────────────────────────────────────────────
echo [步骤 2/4] NSIS 打包...
if not exist "installer\dist" mkdir installer\dist

set NSIS_EXE=
if exist "C:\Program Files (x86)\NSIS\makensis.exe" set NSIS_EXE=C:\Program Files (x86)\NSIS\makensis.exe
if exist "C:\Program Files\NSIS\makensis.exe" set NSIS_EXE=C:\Program Files\NSIS\makensis.exe
if "%NSIS_EXE%"=="" (
    where makensis >nul 2>&1
    if errorlevel 1 (
        echo [错误] 找不到 NSIS，请先安装 NSIS（https://nsis.sourceforge.io）。
        pause & exit /b 1
    )
    set NSIS_EXE=makensis
)

"%NSIS_EXE%" /DPRODUCT_VERSION=%VERSION% installer\setup.nsi
if errorlevel 1 (
    echo [错误] NSIS 打包失败。
    pause & exit /b 1
)
echo [完成] 打包成功：installer\dist\FigmaSearch_Setup.exe
echo.

:: ── 6. 创建 GitLab Release ───────────────────────────────────
echo [步骤 3/4] 创建 GitLab Release...
set TAG_NAME=v%VERSION%
set GITLAB_PROJECT_ID=36568
set GITLAB_API=https://gitlab.nie.netease.com/api/v4

:: 读取 RELEASE_NOTES.md 并转义为 JSON 字符串
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "$content = Get-Content 'RELEASE_NOTES.md' -Raw -Encoding UTF8; $content = $content -replace '\\', '\\\\' -replace '\"', '\\\"' -replace \"`r`n\", '\n' -replace \"`n\", '\n' -replace \"`r\", '\n'; Write-Output $content"`) do set NOTES_JSON=%%i

:: 先创建或确保 tag 存在（基于当前 HEAD）
powershell -NoProfile -Command ^
    "$headers = @{'PRIVATE-TOKEN'='%GITLAB_TOKEN%'; 'Content-Type'='application/json'};" ^
    "$body = '{\"tag_name\":\"%TAG_NAME%\",\"ref\":\"main\"}';" ^
    "try { Invoke-RestMethod -Uri '%GITLAB_API%/projects/%GITLAB_PROJECT_ID%/repository/tags' -Method Post -Headers $headers -Body $body | Out-Null } catch {}" 2>nul

:: 删除已有同名 Release（幂等）
powershell -NoProfile -Command ^
    "$headers = @{'PRIVATE-TOKEN'='%GITLAB_TOKEN%'};" ^
    "try { Invoke-RestMethod -Uri '%GITLAB_API%/projects/%GITLAB_PROJECT_ID%/releases/%TAG_NAME%' -Method Delete -Headers $headers | Out-Null } catch {}" 2>nul

:: 创建 Release
powershell -NoProfile -Command ^
    "$headers = @{'PRIVATE-TOKEN'='%GITLAB_TOKEN%'; 'Content-Type'='application/json'};" ^
    "$notes = (Get-Content 'RELEASE_NOTES.md' -Raw -Encoding UTF8);" ^
    "$body = @{tag_name='%TAG_NAME%'; name='%TAG_NAME%'; description=$notes} | ConvertTo-Json;" ^
    "$resp = Invoke-RestMethod -Uri '%GITLAB_API%/projects/%GITLAB_PROJECT_ID%/releases' -Method Post -Headers $headers -Body $body;" ^
    "Write-Host '[完成] Release 创建成功：' $resp.tag_name"
if errorlevel 1 (
    echo [错误] 创建 Release 失败。
    pause & exit /b 1
)
echo.

:: ── 7. 上传安装包到 Release ──────────────────────────────────
echo [步骤 4/4] 上传安装包...
powershell -NoProfile -Command ^
    "$headers = @{'PRIVATE-TOKEN'='%GITLAB_TOKEN%'};" ^
    "$uploadResp = Invoke-RestMethod -Uri '%GITLAB_API%/projects/%GITLAB_PROJECT_ID%/uploads' -Method Post -Headers $headers -Form @{file=Get-Item 'installer\dist\FigmaSearch_Setup.exe'};" ^
    "$linkBody = @{name='FigmaSearch_Setup.exe'; url=('https://gitlab.nie.netease.com/joker1/figst' + $uploadResp.full_path); link_type='package'} | ConvertTo-Json;" ^
    "$linkHeaders = @{'PRIVATE-TOKEN'='%GITLAB_TOKEN%'; 'Content-Type'='application/json'};" ^
    "Invoke-RestMethod -Uri '%GITLAB_API%/projects/%GITLAB_PROJECT_ID%/releases/%TAG_NAME%/assets/links' -Method Post -Headers $linkHeaders -Body $linkBody | Out-Null;" ^
    "Write-Host '[完成] 安装包上传成功。'"
if errorlevel 1 (
    echo [错误] 上传安装包失败。
    pause & exit /b 1
)
echo.

:: ── 8. Git commit + push ─────────────────────────────────────
echo [附加] 提交代码并推送到 GitLab...
git add .
git commit -m "release: v%VERSION%"
git push gitlab main
echo.

echo ============================================================
echo  发布完成！
echo  版本：%TAG_NAME%
echo  地址：https://gitlab.nie.netease.com/joker1/figst/-/releases/%TAG_NAME%
echo ============================================================
pause

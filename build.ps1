if (Test-Path "./publish") {
    Remove-Item -Path "./publish" -Recurse -Force
}

$cliProject = "GMS-Patcher/GMS-UTML-Patcher.csproj"
$uiProject = "GMS-Patcher.UI/GMS-Patcher.UI.csproj"

function Publish-Target {
    param(
        [string]$Name,
        [string]$Project,
        [string]$Runtime,
        [string]$OutputDir
    )
    Write-Host "[BUILD] $Name ($Runtime)" -ForegroundColor Cyan
    dotnet publish $Project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true --output $OutputDir
}

# 1. Build GUI Installer
Publish-Target -Name "GUI Windows x64" -Project $uiProject -Runtime "win-x64" -OutputDir "./publish/GMS-Patcher-UI-win-x64"
Publish-Target -Name "GUI MacOS arm64" -Project $uiProject -Runtime "osx-arm64" -OutputDir "./publish/GMS-Patcher-UI-osx-arm64"
Publish-Target -Name "GUI MacOS x64" -Project $uiProject -Runtime "osx-x64" -OutputDir "./publish/GMS-Patcher-UI-osx-x64"
Publish-Target -Name "GUI Linux x64" -Project $uiProject -Runtime "linux-x64" -OutputDir "./publish/GMS-Patcher-UI-linux-x64"

# 2. Build CLI Core
Publish-Target -Name "CLI Windows x64" -Project $cliProject -Runtime "win-x64" -OutputDir "./publish/GMS-UTML-Patcher-CLI-win-x64"
Publish-Target -Name "CLI MacOS arm64" -Project $cliProject -Runtime "osx-arm64" -OutputDir "./publish/GMS-UTML-Patcher-CLI-osx-arm64"
Publish-Target -Name "CLI MacOS x64" -Project $cliProject -Runtime "osx-x64" -OutputDir "./publish/GMS-UTML-Patcher-CLI-osx-x64"
Publish-Target -Name "CLI Linux x64" -Project $cliProject -Runtime "linux-x64" -OutputDir "./publish/GMS-UTML-Patcher-CLI-linux-x64"
Publish-Target -Name "CLI Linux arm" -Project $cliProject -Runtime "linux-arm" -OutputDir "./publish/GMS-UTML-Patcher-CLI-linux-arm"
Publish-Target -Name "CLI Linux arm64" -Project $cliProject -Runtime "linux-arm64" -OutputDir "./publish/GMS-UTML-Patcher-CLI-linux-arm64"

# 3. Create zip archives
$archivesPath = "./publish/archives"
New-Item -ItemType Directory -Force -Path $archivesPath | Out-Null

Get-ChildItem -Path "./publish" -Directory | Where-Object { $_.Name -ne "archives" } | ForEach-Object {
    $sourceFolder = $_.FullName
    $zipName = "$archivesPath/$($_.Name).zip"
    Write-Host "[ARCHIVE] Creating $zipName" -ForegroundColor Green
    Compress-Archive -Path $sourceFolder -DestinationPath $zipName -Force
}

Write-Host "[DONE] All builds and archives completed successfully!" -ForegroundColor Green

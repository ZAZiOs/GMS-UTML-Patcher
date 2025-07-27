rm -r ./publish/
Write-Host "[BUILD] Windows x64" -ForegroundColor Cyan
dotnet publish GMS-Patcher/GMS-UTML-Patcher.csproj -c Release -r win-x64 --self-contained true  -p:PublishSingleFile=true --output ./publish/windows-x64
Write-Host "[BUILD] Linux x64" -ForegroundColor Cyan
dotnet publish GMS-Patcher/GMS-UTML-Patcher.csproj -c Release -r linux-x64 --self-contained true  -p:PublishSingleFile=true --output ./publish/linux-x64
Write-Host "[BUILD] Linux arm" -ForegroundColor Cyan
dotnet publish GMS-Patcher/GMS-UTML-Patcher.csproj -c Release -r linux-arm --self-contained true  -p:PublishSingleFile=true --output ./publish/linux-arm
Write-Host "[BUILD] Linux arm64" -ForegroundColor Cyan
dotnet publish GMS-Patcher/GMS-UTML-Patcher.csproj -c Release -r linux-arm64 --self-contained true  -p:PublishSingleFile=true --output ./publish/linux-arm64
Write-Host "[BUILD] MacOS arm64" -ForegroundColor Cyan
dotnet publish GMS-Patcher/GMS-UTML-Patcher.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true --output ./publish/osx-arm64
Write-Host "[BUILD] MacOS x64" -ForegroundColor Cyan
dotnet publish GMS-Patcher/GMS-UTML-Patcher.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true --output ./publish/osx-x64

$archivesPath = "./publish/archives"
New-Item -ItemType Directory -Force -Path $archivesPath | Out-Null

Get-ChildItem -Path "./publish" -Directory | Where-Object { $_.Name -ne "archives" } | ForEach-Object {
    $sourceFolder = $_.FullName
    $zipName = "$archivesPath/$($_.Name).zip"
    Write-Host "[ARCHIVE] Creating $zipName" -ForegroundColor Cyan

    Compress-Archive -Path $sourceFolder -DestinationPath $zipName -Force
}

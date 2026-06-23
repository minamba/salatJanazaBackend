$ErrorActionPreference = "Stop"

Write-Host "=== Publish QabrWebApp.Api ===" -ForegroundColor Green
dotnet publish .\QabrWebApp\QabrWebApp.Api.csproj `
    -c Release `
    -o .\publish\api

Write-Host "=== Publish QabrWebApp.IdentityServer ===" -ForegroundColor Green
dotnet publish .\QabrWebApp.IdentityServer\QabrWebApp.IdentityServer.csproj `
    -c Release `
    -o .\publish\identity

Write-Host ""
Write-Host "=== Pret pour deploiement ===" -ForegroundColor Cyan
Write-Host "1. Copier les fichiers Docker sur le VPS :"
Write-Host "   scp docker-compose.yml api.Dockerfile identity.Dockerfile USER@YOUR_VPS_IP:/var/www/salatjanaza/"
Write-Host ""
Write-Host "2. Copier les binaires publiés :"
Write-Host "   scp -r .\publish\api\*     USER@YOUR_VPS_IP:/var/www/salatjanaza/app/api/"
Write-Host "   scp -r .\publish\identity\* USER@YOUR_VPS_IP:/var/www/salatjanaza/app/identity/"
Write-Host ""
Write-Host "3. Sur le VPS :"
Write-Host "   cd /var/www/salatjanaza && docker-compose up -d --build"

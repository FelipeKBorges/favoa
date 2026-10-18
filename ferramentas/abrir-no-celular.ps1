<#
  Abre o Fávoa para o celular na mesma Wi-Fi (e no Tailscale, se instalado).

  Primeira vez: rode num PowerShell COMO ADMINISTRADOR (para liberar a porta no firewall):
    powershell -ExecutionPolicy Bypass -File .\ferramentas\abrir-no-celular.ps1
  Das próximas vezes, um PowerShell normal basta.

  O celular pede o PIN definido em Seus dados > Acesso pelo celular (sem PIN, o acesso pela rede fica fechado).
  Mesmo com PIN, use na rede de casa (perfil Privado) ou pelo Tailscale. Não abra a porta no roteador.
#>
param([int]$Porta = 5080)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$nomeRegra = 'Favoa (celular)'

function Escrever($texto, $cor = 'Gray') { Write-Host $texto -ForegroundColor $cor }

# 1. Rede: precisa ser Privada (o Windows bloqueia e o perfil Público é para redes estranhas).
$perfis = @(Get-NetConnectionProfile | Where-Object { $_.IPv4Connectivity -ne 'Disconnected' })
$publicas = @($perfis | Where-Object { $_.NetworkCategory -eq 'Public' })
if ($publicas.Count -gt 0) {
    Escrever "A rede '$($publicas[0].Name)' está como PÚBLICA." Yellow
    Escrever "Se for a Wi-Fi de casa, mude para Privada: Configurações > Rede e Internet > Wi-Fi > (sua rede) > Tipo de perfil de rede: Privado." Yellow
    Escrever "Em rede pública o celular não vai conseguir abrir (e é melhor assim)." Yellow
    Escrever ""
}

# 2. Firewall: libera a porta só para redes privadas.
$regra = Get-NetFirewallRule -DisplayName $nomeRegra -ErrorAction SilentlyContinue
if (-not $regra) {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) {
        Escrever "Falta liberar a porta $Porta no firewall (só uma vez)." Yellow
        Escrever "Abra o PowerShell como Administrador (botão direito > Executar como administrador) e rode este mesmo script." Yellow
        exit 1
    }
    New-NetFirewallRule -DisplayName $nomeRegra -Direction Inbound -Protocol TCP -LocalPort $Porta -Action Allow -Profile Private | Out-Null
    Escrever "Porta $Porta liberada no firewall (só redes privadas)." Green
}

# 3. Endereços para o celular.
$locais = @(Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } |
            ForEach-Object { $_.IPv4Address.IPAddress })
$tailscale = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -like '100.*' } |
               ForEach-Object { $_.IPAddress })

Escrever ""
Escrever "================ ABRA NO CELULAR ================" Cyan
foreach ($ip in $locais) { Escrever "  Mesma Wi-Fi:  http://${ip}:$Porta" Green }
foreach ($ip in $tailscale) { Escrever "  Tailscale:    http://${ip}:$Porta" Green }
if ($locais.Count + $tailscale.Count -eq 0) { Escrever "  Não achei o IP. Rode 'ipconfig' e use o Endereço IPv4: http://<ip>:$Porta" Yellow }
Escrever "  Neste PC:     http://localhost:$Porta"
Escrever "=================================================" Cyan
Escrever "No celular, entre com o PIN de Seus dados > Acesso pelo celular (defina lá, neste PC, se ainda não tiver)."
Escrever "Deixe esta janela aberta. Para parar: Ctrl+C."
Escrever ""

# 4. Sobe o Fávoa aceitando conexões da rede.
dotnet run --project ([IO.Path]::Combine($raiz, 'src', 'Favoa.Web')) --urls "http://*:$Porta"

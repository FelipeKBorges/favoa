<#
  Faz o Fávoa rodar sozinho em segundo plano (sobe quando o PC liga, sem janela), cria o atalho no
  menu Iniciar e, se quiser, publica no Tailscale com HTTPS para instalar como app no celular.

  Uso, na pasta do projeto, num PowerShell COMO ADMINISTRADOR:
    powershell -ExecutionPolicy Bypass -File .\ferramentas\instalar-inicializacao.ps1
    powershell -ExecutionPolicy Bypass -File .\ferramentas\instalar-inicializacao.ps1 -ComTailscale
    powershell -ExecutionPolicy Bypass -File .\ferramentas\instalar-inicializacao.ps1 -Remover
  Para outra família: -ComTailscale -SoPeloTailscale -CadastroAberto (veja docs\COMPARTILHAR.md).

  Para atualizar depois, use o ícone "Atualizar Fávoa" (criado por este script) ou ferramentas\atualizar.ps1.
#>
param(
    [switch]$ComTailscale,
    [switch]$Remover,
    [switch]$CadastroAberto,   # deixa outras famílias criarem conta em /criar-conta (rode de novo sem a opção para fechar)
    [switch]$SoPeloTailscale,  # escuta só em localhost: o acesso de fora passa pelo HTTPS do Tailscale, nunca por HTTP direto
    [int]$Porta = 5080
)
$ErrorActionPreference = 'Stop'
. ([IO.Path]::Combine($PSScriptRoot, 'comum.ps1'))

$raiz      = Split-Path -Parent $PSScriptRoot
$base      = $FavoaBase
$appDir    = $FavoaApp
$logDir    = $FavoaLogs
$dll       = $FavoaDll
$tarefa    = $FavoaTarefa
$atalho    = [IO.Path]::Combine($env:APPDATA, 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'Fávoa.url')

function Achar-Tailscale {
    $cmd = Get-Command tailscale -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $padrao = [IO.Path]::Combine($env:ProgramFiles, 'Tailscale', 'tailscale.exe')
    if (Test-Path $padrao) { return $padrao }
    return $null
}

# ---------------------------------------------------------------- remover
if ($Remover) {
    Passo "Removendo a inicialização automática do Fávoa ..."
    Parar-Favoa
    if (Get-ScheduledTask -TaskName $tarefa -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $tarefa -Confirm:$false
    }
    if (Test-Path $atalho) { Remove-Item $atalho }
    foreach ($pasta in @([Environment]::GetFolderPath('Desktop'), [IO.Path]::Combine($env:APPDATA, 'Microsoft', 'Windows', 'Start Menu', 'Programs'))) {
        $lnk = [IO.Path]::Combine($pasta, 'Atualizar Fávoa.lnk')
        if (Test-Path $lnk) { Remove-Item $lnk }
    }
    $ts = Achar-Tailscale
    if ($ts) { Aviso "O Tailscale não foi mexido. Para tirar o Fávoa de lá: & '$ts' serve --https=443 off" }
    Ok "Pronto. Seus dados continuam em $base (nada foi apagado)."
    exit 0
}

# ---------------------------------------------------------------- 1. publicar
Passo "1/5 Parando o Fávoa (se estiver rodando) ..."
Parar-Favoa

Passo "2/5 Publicando a versão atual em $appDir ..."
$versao = Publicar-Favoa $raiz $appDir
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# ---------------------------------------------------------------- 2. tarefa ao entrar no Windows
Passo "3/5 Criando a tarefa '$tarefa' (segundo plano, sobe com o PC) ..."
$dotnet = (Get-Command dotnet).Source
$log = $FavoaLog

$admin = Eh-Admin
$usuario = "$env:USERDOMAIN\$env:USERNAME"
$dados = $base

function Registrar-Tarefa([string]$modo) {
    # A pasta de dados vai explícita: em segundo plano o Windows pode não carregar o perfil do mesmo jeito.
    $escuta = if ($SoPeloTailscale) { "http://localhost:$Porta" } else { "http://*:$Porta" }
    $extra = if ($CadastroAberto) { ' --Favoa:CadastroAberto=true' } else { '' }
    $comando = "`"`"$dotnet`" `"$dll`" --urls $escuta --Favoa:PastaDados=`"$dados`"$extra > `"$log`" 2>&1`""
    $acao = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/c $comando" -WorkingDirectory $appDir
    $ajustes = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew

    if ($modo -eq 'fundo') {
        # Segundo plano de verdade (S4U): sem janela nem console para fechar, sobe com o PC e não pede senha.
        $gatilhos = @((New-ScheduledTaskTrigger -AtStartup), (New-ScheduledTaskTrigger -AtLogOn -User $usuario))
        $quem = New-ScheduledTaskPrincipal -UserId $usuario -LogonType S4U -RunLevel Limited
    } else {
        # Sem administrador: roda na sua sessão (uma janela de console pode aparecer; não feche).
        $gatilhos = @(New-ScheduledTaskTrigger -AtLogOn -User $usuario)
        $quem = New-ScheduledTaskPrincipal -UserId $usuario -LogonType Interactive -RunLevel Limited
    }

    Register-ScheduledTask -TaskName $tarefa -Action $acao -Trigger $gatilhos -Settings $ajustes -Principal $quem `
        -Description 'Fávoa (painel pessoal) em segundo plano.' -Force -ErrorAction Stop | Out-Null
}



# Sempre tenta o segundo plano (S4U): a checagem de administrador pode errar (ex.: proteção de administrador do
# Windows 11, que eleva com outra conta). Se o Windows recusar, cai para a tarefa na sessão e diz o motivo.
Write-Host "   Usuário da tarefa: $usuario (administrador: $admin)"
$modo = 'fundo'
try {
    Registrar-Tarefa $modo
} catch {
    Aviso "   O Windows não aceitou a tarefa em segundo plano: $($_.Exception.Message)"
    if (-not $admin) { Aviso "   Este PowerShell não está como administrador. Abra como administrador e rode de novo para o segundo plano." }
    Aviso "   Usando o modo na sua sessão (sobe ao entrar no Windows)."
    $modo = 'sessao'
    Registrar-Tarefa $modo
}
$principal = (Get-ScheduledTask -TaskName $tarefa).Principal
Write-Host "   Tarefa registrada como: $($principal.LogonType) para $($principal.UserId)"
Start-ScheduledTask -TaskName $tarefa
if (-not (Esperar-Subir $Porta)) {
    Mostrar-Log
    $info = Get-ScheduledTask -TaskName $tarefa | Get-ScheduledTaskInfo
    throw "O Fávoa não respondeu em http://127.0.0.1:$Porta (resultado da tarefa: $($info.LastTaskResult)). Me mande as linhas acima."
}
Ok "   Fávoa rodando em segundo plano."

# ---------------------------------------------------------------- 3. atalho
Passo "4/5 Criando o atalho 'Fávoa' no menu Iniciar ..."
$icone = [IO.Path]::Combine($appDir, 'wwwroot', 'icones', 'favoa.ico')
New-Item -ItemType Directory -Force -Path (Split-Path $atalho) | Out-Null
Set-Content -Path $atalho -Encoding ASCII -Value @(
    '[InternetShortcut]',
    "URL=http://localhost:$Porta/",
    "IconFile=$icone",
    'IconIndex=0'
)

Criar-Atalho-Atualizar $raiz

# ---------------------------------------------------------------- 4. Tailscale (opcional)
$urlTailscale = $null
if ($ComTailscale) {
    Passo "5/5 Publicando no Tailscale com HTTPS ..."
    $ts = Achar-Tailscale
    if (-not $ts) {
        Aviso "   Não achei o Tailscale. Instale em https://tailscale.com/download, entre na sua conta e rode este script de novo com -ComTailscale."
    } else {
        & $ts serve --bg $Porta
        if ($LASTEXITCODE -ne 0) {
            Aviso "   O Tailscale pediu uma confirmação (ativar HTTPS no seu tailnet). Siga o link que ele mostrou e rode este script de novo."
        } else {
            $status = & $ts status --json | ConvertFrom-Json
            $dns = $status.Self.DNSName.TrimEnd('.')
            if ($dns) { $urlTailscale = "https://$dns/" }
        }
    }
} else {
    Passo "5/5 Tailscale: pulado (use -ComTailscale para instalar como app no celular com HTTPS)."
}

# ---------------------------------------------------------------- resumo
Write-Host ""
Ok "Pronto. O Fávoa agora roda sozinho em segundo plano$(if ($modo -eq 'fundo') { ' (sobe quando o PC liga)' } else { ' (abre ao entrar no Windows)' })."
Write-Host "  Neste PC:   http://localhost:$Porta  (ou o atalho 'Fávoa' no menu Iniciar)"
if ($urlTailscale) {
    Write-Host "  Celular:    $urlTailscale" -ForegroundColor Green
    Write-Host "              Abra no celular (com o Tailscale ligado), entre com o PIN e use 'Instalar app' / 'Adicionar à Tela de Início'."
} else {
    Write-Host "  Celular na Wi-Fi: rode .\ferramentas\abrir-no-celular.ps1 uma vez como administrador para liberar a porta e ver a URL."
}
Write-Host "  Versão:     $versao"
Write-Host "  Atualizar:  ícone 'Atualizar Fávoa' na área de trabalho ou no menu Iniciar"
Write-Host "  Log:        $log"
Write-Host "  O PIN do celular fica em Seus dados > Acesso pelo celular."

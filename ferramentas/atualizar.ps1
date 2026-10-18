<#
  Atualiza o Fávoa com um clique: compila a versão que está na pasta do projeto, troca e religa.

  Normalmente você usa o ícone "Atualizar Fávoa" (área de trabalho ou menu Iniciar).
  Para criar o ícone, rode uma vez na pasta do projeto:
    powershell -ExecutionPolicy Bypass -File .\ferramentas\atualizar.ps1

  O que ele faz:
    1. Compila a versão nova numa pasta separada, com o Fávoa ainda no ar. Se falhar, nada muda.
    2. Para o Fávoa e guarda um backup do banco (Backups\antes-atualizar-...).
    3. Troca a versão e religa. Se a nova não subir, volta sozinho para a anterior.
#>
param(
    [int]$Porta = 5080,
    [switch]$SemPausa
)
$ErrorActionPreference = 'Stop'
. ([IO.Path]::Combine($PSScriptRoot, 'comum.ps1'))

# Precisa de administrador para parar e religar a tarefa em segundo plano: pede uma vez e roda numa janela nova.
if (-not (Eh-Admin)) {
    try {
        $argumentos = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Porta', $Porta)
        if ($SemPausa) { $argumentos += '-SemPausa' }
        Start-Process powershell.exe -Verb RunAs -ArgumentList $argumentos
    } catch {
        Aviso "Atualização cancelada: o Windows precisa da permissão de administrador para religar o Fávoa."
        Start-Sleep -Seconds 5
    }
    exit
}

$raiz        = Split-Path -Parent $PSScriptRoot
$appNovo     = [IO.Path]::Combine($FavoaBase, 'app-novo')
$appAnterior = [IO.Path]::Combine($FavoaBase, 'app-anterior')
$banco       = [IO.Path]::Combine($FavoaBase, 'favoa.db')
$backups     = [IO.Path]::Combine($FavoaBase, 'Backups')

function Remover-Pasta([string]$pasta) {
    if (Test-Path $pasta) { Remove-Item -LiteralPath $pasta -Recurse -Force }
}

$Host.UI.RawUI.WindowTitle = 'Atualizar Fávoa'
$sucesso = $false
New-Item -ItemType Directory -Force -Path $FavoaLogs | Out-Null
$registro = [IO.Path]::Combine($FavoaLogs, 'atualizar.log')
try { Start-Transcript -Path $registro -Force | Out-Null } catch { }
try {
    Write-Host ""
    Write-Host "Atualizando o Fávoa a partir de $raiz" -ForegroundColor White
    Write-Host ""

    # Primeira vez (sem a tarefa): faz a instalação completa.
    if (-not (Get-ScheduledTask -TaskName $FavoaTarefa -ErrorAction SilentlyContinue) -or -not (Test-Path $FavoaDll)) {
        Aviso "O Fávoa ainda não está instalado em segundo plano. Instalando ..."
        & ([IO.Path]::Combine($PSScriptRoot, 'instalar-inicializacao.ps1')) -Porta $Porta
        Criar-Atalho-Atualizar $raiz
        $sucesso = $true
        return
    }

    Passo "1/4 Compilando a versão nova (o Fávoa continua no ar) ..."
    Remover-Pasta $appNovo
    $versao = Publicar-Favoa $raiz $appNovo

    Passo "2/4 Parando o Fávoa e guardando um backup do banco ..."
    Parar-Favoa
    if (Test-Path $banco) {
        New-Item -ItemType Directory -Force -Path $backups | Out-Null
        $copia = [IO.Path]::Combine($backups, "antes-atualizar-$(Get-Date -Format 'yyyy-MM-dd-HHmmss').db")
        Copy-Item -LiteralPath $banco -Destination $copia
        Get-ChildItem $backups -Filter 'antes-atualizar-*.db' | Sort-Object Name -Descending | Select-Object -Skip 5 |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
        Write-Host "   Backup: $copia"
    }

    Passo "3/4 Trocando para a versão $versao ..."
    Copiar-Pasta $FavoaApp $appAnterior
    Copiar-Pasta $appNovo $FavoaApp

    Passo "4/4 Ligando de novo ..."
    New-Item -ItemType Directory -Force -Path $FavoaLogs | Out-Null
    Start-ScheduledTask -TaskName $FavoaTarefa
    if (-not (Esperar-Subir $Porta)) {
        Mostrar-Log
        if (Test-Path $FavoaLog) { Copy-Item -LiteralPath $FavoaLog -Destination ([IO.Path]::Combine($FavoaLogs, 'falha-atualizacao.log')) -Force }
        Aviso "   A versão nova não respondeu. Voltando para a anterior ..."
        Parar-Favoa
        Copiar-Pasta $appAnterior $FavoaApp
        Start-ScheduledTask -TaskName $FavoaTarefa
        $voltou = Esperar-Subir $Porta
        throw "A atualização foi desfeita$(if ($voltou) { ' e a versão anterior está no ar' } else { ', mas o Fávoa não respondeu' }). O log da tentativa ficou em logs\falha-atualizacao.log."
    }

    Criar-Atalho-Atualizar $raiz
    Write-Host ""
    Ok "Pronto: Fávoa atualizado para a versão $versao."
    Write-Host "  Recarregue a página no PC e no celular para ver as novidades."
    $sucesso = $true
}
catch {
    Write-Host ""
    Write-Host "Não deu para atualizar: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "O registro completo está em $registro"
}
finally {
    try { Remover-Pasta $appNovo } catch { }
    try { Stop-Transcript | Out-Null } catch { }
    if (-not $SemPausa) {
        Write-Host ""
        if ($sucesso) { Write-Host "Esta janela fecha sozinha em 10 segundos ..."; Start-Sleep -Seconds 10 }
        else { Read-Host "Pressione Enter para fechar" | Out-Null }
    }
}

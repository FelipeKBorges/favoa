<#
  Funções usadas pelos scripts de instalação e atualização do Fávoa.
  Não rode este arquivo direto: ele é carregado por instalar-inicializacao.ps1 e atualizar.ps1.
#>

$FavoaBase    = [IO.Path]::Combine($env:LOCALAPPDATA, 'Favoa')
$FavoaApp     = [IO.Path]::Combine($FavoaBase, 'app')
$FavoaLogs    = [IO.Path]::Combine($FavoaBase, 'logs')
$FavoaLog     = [IO.Path]::Combine($FavoaLogs, 'favoa.log')
$FavoaDll     = [IO.Path]::Combine($FavoaApp, 'Favoa.Web.dll')
$FavoaTarefa  = 'Favoa'

function Passo($texto) { Write-Host $texto -ForegroundColor Cyan }
function Ok($texto) { Write-Host $texto -ForegroundColor Green }
function Aviso($texto) { Write-Host $texto -ForegroundColor Yellow }

function Eh-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Processos-Favoa {
    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe' OR Name = 'cmd.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine -like "*Favoa.Web.dll*" -and $_.CommandLine -like "*$FavoaBase*" }
}

function Parar-Favoa {
    if (Get-ScheduledTask -TaskName $FavoaTarefa -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $FavoaTarefa -ErrorAction SilentlyContinue
    }
    # Garante que nenhum Fávoa publicado ficou rodando (senão os arquivos ficam presos).
    # O cmd.exe da tarefa também sai: ele usa a pasta do app como pasta de trabalho.
    foreach ($i in 1..15) {
        $vivos = @(Processos-Favoa)
        if ($vivos.Count -eq 0) { break }
        $vivos | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 1
    }
    if (@(Processos-Favoa).Count -gt 0) { throw "Não consegui parar o Fávoa (o processo dotnet continua aberto). Reinicie o PC e tente de novo." }
    Start-Sleep -Seconds 1
}

function Copiar-Pasta([string]$de, [string]$para) {
    # Espelha a pasta arquivo por arquivo (em vez de renomear a pasta inteira, que falha se o antivírus
    # ou o Explorer estiver com ela aberta) e tenta de novo arquivos presos por alguns segundos.
    $saida = & robocopy $de $para /MIR /R:15 /W:2 /NFL /NDL /NJH /NJS /NP
    $codigo = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if ($codigo -ge 8) {
        $saida | Select-Object -Last 10 | ForEach-Object { Write-Host "     $_" }
        throw "Falha ao copiar os arquivos para $para (robocopy $codigo)."
    }
}

function Esperar-Subir([int]$Porta) {
    # 127.0.0.1 direto: "localhost" pode ir para o IPv6 (::1) e enganar a checagem.
    # HttpClient sem seguir redirecionamento: com login, "/" responde 302 para /entrar, e isso já prova que o Fávoa está de pé.
    Add-Type -AssemblyName System.Net.Http
    $http = New-Object System.Net.Http.HttpClientHandler
    $http.AllowAutoRedirect = $false
    $cli = New-Object System.Net.Http.HttpClient($http)
    $cli.Timeout = [TimeSpan]::FromSeconds(10)
    try {
        foreach ($i in 1..60) {
            Start-Sleep -Seconds 1
            try {
                $r = $cli.GetAsync("http://127.0.0.1:$Porta/").GetAwaiter().GetResult()
                if ([int]$r.StatusCode -lt 500) { return $true }
            } catch { }
        }
    } finally { $cli.Dispose() }
    return $false
}

function Mostrar-Log {
    if (Test-Path $FavoaLog) {
        Aviso "   Últimas linhas do log ($FavoaLog):"
        Get-Content $FavoaLog -Tail 15 | ForEach-Object { Write-Host "     $_" }
    }
}

function Versao-Do-Codigo([string]$raiz) {
    # Commit atual (ex.: 7593695); sem git, a data e hora da compilação.
    if (Get-Command git -ErrorAction SilentlyContinue) {
        try {
            # safe.directory: rodando como administrador, o git recusa pastas de outro dono ("dubious ownership").
            $hash = & git -c "safe.directory=*" -C $raiz rev-parse --short HEAD 2>$null
            if ($LASTEXITCODE -eq 0 -and $hash) { return ([string]$hash).Trim() }
        } catch { }
    }
    return (Get-Date -Format 'yyyyMMdd.HHmm')
}

function Publicar-Favoa([string]$raiz, [string]$destino) {
    $versao = Versao-Do-Codigo $raiz
    $projeto = [IO.Path]::Combine($raiz, 'src', 'Favoa.Web', 'Favoa.Web.csproj')
    # Out-Host: a saída do dotnet não pode virar o valor de retorno da função.
    # force-evaluate: um assets.json velho não pode deixar o Web numa versão de pacote e o Data noutra.
    dotnet restore $projeto --force-evaluate --nologo -v quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Falha ao restaurar os pacotes." }
    # MSB3277 vira erro: conflito de assembly aborta aqui, antes de parar o Fávoa que está no ar.
    dotnet publish $projeto -c Release -o $destino --no-restore --nologo -v quiet -warnaserror:MSB3277 "-p:SourceRevisionId=$versao" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar. Confira se 'dotnet build' funciona na pasta do projeto." }
    return $versao
}

function Criar-Atalho-Atualizar([string]$raiz) {
    # "Atualizar Fávoa" na área de trabalho e no menu Iniciar: um clique publica a versão da pasta do projeto.
    $alvo = [IO.Path]::Combine($raiz, 'ferramentas', 'atualizar.ps1')
    $icone = [IO.Path]::Combine($FavoaApp, 'wwwroot', 'icones', 'favoa.ico')
    $powershell = [IO.Path]::Combine($env:SystemRoot, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe')
    $shell = New-Object -ComObject WScript.Shell
    $pastas = @(
        [Environment]::GetFolderPath('Desktop'),
        [IO.Path]::Combine($env:APPDATA, 'Microsoft', 'Windows', 'Start Menu', 'Programs')
    )
    foreach ($pasta in $pastas) {
        if (-not $pasta -or -not (Test-Path $pasta)) { continue }
        $lnk = $shell.CreateShortcut([IO.Path]::Combine($pasta, 'Atualizar Fávoa.lnk'))
        $lnk.TargetPath = $powershell
        $lnk.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$alvo`""
        $lnk.WorkingDirectory = $raiz
        if (Test-Path $icone) { $lnk.IconLocation = "$icone,0" }
        $lnk.Description = 'Publica a versão do Fávoa que está na pasta do projeto e religa.'
        $lnk.Save()
    }
}

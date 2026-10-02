<#
  ATUALIZAR-PORTAL.ps1 — instala ou atualiza o Portal Ordem MSS como serviço Windows.

  Uso (PowerShell como Administrador, na pasta do repositório, ex. C:\_dev\ordem):
      powershell -ExecutionPolicy Bypass -File tools\ATUALIZAR-PORTAL.ps1
  Opções:
      -SemGitPull        não faz git pull (publica o que está na pasta)
      -AbrirFirewall     cria a regra de firewall para a porta (acesso a partir da rede)
      -Porta 5090        porta usada para confirmar o /health

  O que faz:
    1. git pull
    2. dotnet publish para C:\Program Files\MSS\PortalOrdemMss\versions\<data-commit>
    3. pára o serviço MssPortalOrdem (se existir)
    4. aponta a junção "current" para a versão nova
    5. cria o serviço se ainda não existir (arranque automático atrasado + reinício em falha)
    6. arranca o serviço e espera que /health responda

  Voltar atrás: parar o serviço, apontar "current" para a pasta da versão anterior em
  "versions" e arrancar de novo (ver README, "Pôr em produção").
  As versões antigas não são apagadas. A configuração (settings.json) fica em
  %ProgramData%\MSS\PortalOrdemMss e nunca é tocada por este script.

  Nota: guardar este ficheiro em UTF-8 com BOM (o Windows PowerShell 5.1 lê mal os acentos sem BOM).
#>
param(
    [switch]$SemGitPull,
    [switch]$AbrirFirewall,
    [int]$Porta = 5090,
    [string]$Raiz = "C:\Program Files\MSS\PortalOrdemMss",
    [string]$ServiceName = "MssPortalOrdem"
)

$ErrorActionPreference = "Stop"

function Passo($texto) { Write-Host ""; Write-Host "==> $texto" -ForegroundColor Cyan }

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    throw "Abre o PowerShell como Administrador (botão direito > Executar como administrador) e corre de novo."
}

$repo = Split-Path -Parent $PSScriptRoot
$projeto = Join-Path $repo "src\PortalOrdemMss.Web"
if (-not (Test-Path (Join-Path $projeto "PortalOrdemMss.Web.csproj"))) {
    throw "Não encontrei o projeto em $projeto. Corre o script a partir da pasta do repositório."
}

if (-not $SemGitPull) {
    Passo "A atualizar o código (git pull)"
    git -C $repo pull
    if ($LASTEXITCODE -ne 0) {
        $seguro = $repo.Replace("\", "/")
        throw "git pull falhou. Se a mensagem fala em 'dubious ownership', corre: git config --global --add safe.directory $seguro"
    }
}

$commit = (git -C $repo rev-parse --short HEAD).Trim()
$versao = "{0}-{1}" -f (Get-Date -Format "yyyyMMdd-HHmmss"), $commit
$destino = Join-Path $Raiz "versions\$versao"

Passo "A publicar a versão $versao"
dotnet publish $projeto -c Release -r win-x64 --self-contained false -o $destino
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou. O serviço atual não foi tocado." }
$exe = Join-Path $destino "PortalOrdemMss.Web.exe"
if (-not (Test-Path $exe)) { throw "Não encontrei $exe depois do publish." }

$servico = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

# Uma janela com "dotnet run" na mesma porta impede o serviço de arrancar.
if ($null -eq $servico -or $servico.Status -ne "Running") {
    $ocupada = Get-NetTCPConnection -LocalPort $Porta -State Listen -ErrorAction SilentlyContinue
    if ($ocupada) {
        throw "A porta $Porta já está em uso (provavelmente a janela com 'dotnet run'). Fecha essa janela (Ctrl+C) e corre o script de novo."
    }
}

if ($servico -and $servico.Status -ne "Stopped") {
    Passo "A parar o serviço $ServiceName"
    Stop-Service -Name $ServiceName -Force
    (Get-Service -Name $ServiceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}

Passo "A apontar 'current' para a versão nova"
$current = Join-Path $Raiz "current"
if (Test-Path $current) {
    # Remove só a junção, não a pasta para onde aponta.
    cmd /c rmdir "$current" | Out-Null
    if (Test-Path $current) { throw "Não consegui remover a junção $current." }
}
New-Item -ItemType Junction -Path $current -Target $destino | Out-Null

if ($null -eq $servico) {
    Passo "A criar o serviço $ServiceName"
    $binario = '"' + (Join-Path $current "PortalOrdemMss.Web.exe") + '"'
    New-Service -Name $ServiceName -BinaryPathName $binario -DisplayName "MSS Portal Ordem" `
        -Description "Catálogo de artigos e ordem CDU_MSS_ORDEM (Portal Ordem MSS)" -StartupType Automatic | Out-Null
    # Arranque atrasado: dá tempo à rede (pasta das fotos em UNC) depois de um reboot.
    sc.exe config $ServiceName start= delayed-auto | Out-Null
    sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/60000 | Out-Null
}

if ($AbrirFirewall) {
    $regra = "Portal Ordem MSS ($Porta)"
    if (-not (Get-NetFirewallRule -DisplayName $regra -ErrorAction SilentlyContinue)) {
        Passo "A abrir a porta $Porta na firewall (só rede de domínio/privada)"
        New-NetFirewallRule -DisplayName $regra -Direction Inbound -Protocol TCP -LocalPort $Porta `
            -Action Allow -Profile Domain,Private | Out-Null
    }
}

Passo "A arrancar o serviço"
Start-Service -Name $ServiceName

$url = "http://127.0.0.1:$Porta/health"
$ok = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $ok = $true; break }
    } catch { }
}

if (-not $ok) {
    Write-Host ""
    Write-Host "O serviço arrancou mas $url não respondeu em 60 s." -ForegroundColor Yellow
    Write-Host "Vê o Visualizador de Eventos (Aplicação) e confirma Portal:ListenUrl no settings.json." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "Pronto: versão $versao a correr como serviço $ServiceName." -ForegroundColor Green
Write-Host "Abre http://127.0.0.1:$Porta (já não precisas de deixar nenhuma janela aberta)." -ForegroundColor Green

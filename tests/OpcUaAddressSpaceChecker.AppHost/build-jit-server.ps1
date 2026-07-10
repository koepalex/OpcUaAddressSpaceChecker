<#
.SYNOPSIS
    Hosts a local Docker registry, builds the umati Industrial Joining Technologies (IJT/JIT)
    OPC UA server image from the UA-for-Industrial-Joining-Technologies Release2 sources, and
    pushes it into the local registry so the Aspire AppHost can reference it by name.

.DESCRIPTION
    1. Ensures a local `registry:2` container is running on localhost:<RegistryPort> (idempotent).
    2. Short-circuits when the image tag is already present in the registry (unless -Force).
    3. Fetches the minimal build context (Dockerfile, .dockerignore, and the mandatory
       OPC_UA_IJT_Server_Simulator_Linux.zip payload COPYed by the Dockerfile) via raw download,
       falling back to a sparse git checkout.
    4. Builds, tags and pushes localhost:<RegistryPort>/<ImageName>:<Tag>.

    Build context source:
    https://github.com/umati/UA-for-Industrial-Joining-Technologies (OPC_UA_Servers/Release2).

.NOTES
    OPCUA_HOSTNAME is intentionally NOT baked into the image; it is supplied at container runtime
    by the AppHost (WithEnvironment("OPCUA_HOSTNAME","localhost")), matching the Dockerfile's
    env-override entrypoint. localhost:<RegistryPort> is treated as an insecure registry, which
    Docker permits by default for localhost, so no daemon configuration change is required.
#>
[CmdletBinding()]
param(
    [int]$RegistryPort = 5000,
    [string]$RegistryName = "opcua-checker-registry",
    [string]$ImageName = "opcua-ijt-server",
    [string]$Tag = "latest",
    [string]$Ref = "main",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[build-jit-server] $Message"
}

function Test-ImagePresent {
    param([int]$Port, [string]$Image, [string]$Tag)
    try {
        $tagsUri = "http://localhost:$Port/v2/$Image/tags/list"
        $response = Invoke-RestMethod -Uri $tagsUri -TimeoutSec 5 -ErrorAction Stop
        return ($null -ne $response.tags) -and ($response.tags -contains $Tag)
    }
    catch {
        return $false
    }
}

function Ensure-Registry {
    param([string]$Name, [int]$Port)

    # Detect container existence/state without brittle string parsing.
    $existing = docker ps -a --filter "name=^/$Name$" --format "{{.Names}}|{{.State}}" 2>$null
    if ($existing) {
        $parts = $existing.Split('|')
        $state = if ($parts.Length -gt 1) { $parts[1] } else { "" }
        if ($state -eq "running") {
            Write-Step "Reusing running registry container '$Name'."
            return
        }
        Write-Step "Starting existing registry container '$Name'."
        docker start $Name | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to start existing registry container '$Name'." }
        return
    }

    Write-Step "Creating local registry container '$Name' on port $Port."
    docker run -d --restart=always -p "${Port}:5000" --name $Name registry:2 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to start registry container '$Name' on port $Port." }
}

function Get-BuildContext {
    param([string]$Ref, [string]$Destination)

    $baseRaw = "https://raw.githubusercontent.com/umati/UA-for-Industrial-Joining-Technologies/$Ref/OPC_UA_Servers/Release2"
    $files = @("Dockerfile", ".dockerignore", "OPC_UA_IJT_Server_Simulator_Linux.zip")

    try {
        foreach ($file in $files) {
            $target = Join-Path $Destination $file
            Write-Step "Downloading $file ..."
            Invoke-WebRequest -Uri "$baseRaw/$file" -OutFile $target -ErrorAction Stop
        }
    }
    catch {
        Write-Step "Raw download failed ($($_.Exception.Message)); falling back to sparse git checkout."
        $cloneDir = Join-Path ([System.IO.Path]::GetTempPath()) ("jit-clone-" + [System.Guid]::NewGuid().ToString("N"))
        try {
            git clone --filter=blob:none --sparse "https://github.com/umati/UA-for-Industrial-Joining-Technologies" $cloneDir 2>&1 | Write-Host
            if ($LASTEXITCODE -ne 0) { throw "git clone failed." }
            Push-Location $cloneDir
            try {
                git checkout $Ref 2>&1 | Write-Host
                git sparse-checkout set "OPC_UA_Servers/Release2" 2>&1 | Write-Host
                if ($LASTEXITCODE -ne 0) { throw "git sparse-checkout failed." }
            }
            finally {
                Pop-Location
            }
            $release2 = Join-Path $cloneDir "OPC_UA_Servers/Release2"
            if (-not (Test-Path $release2)) { throw "Sparse checkout did not produce OPC_UA_Servers/Release2." }
            Copy-Item -Path (Join-Path $release2 "*") -Destination $Destination -Recurse -Force
        }
        finally {
            if (Test-Path $cloneDir) { Remove-Item -Path $cloneDir -Recurse -Force -ErrorAction SilentlyContinue }
        }
    }

    # The zip payload is mandatory: the Dockerfile COPYs it into the image.
    $zipPath = Join-Path $Destination "OPC_UA_IJT_Server_Simulator_Linux.zip"
    if (-not (Test-Path $zipPath)) {
        throw "Build context is missing OPC_UA_IJT_Server_Simulator_Linux.zip (required by the Dockerfile)."
    }
}

$context = $null
try {
    Ensure-Registry -Name $RegistryName -Port $RegistryPort

    if (-not $Force -and (Test-ImagePresent -Port $RegistryPort -Image $ImageName -Tag $Tag)) {
        Write-Step "Image '$ImageName`:$Tag' already present in localhost:$RegistryPort; nothing to do (use -Force to rebuild)."
        exit 0
    }

    $context = Join-Path ([System.IO.Path]::GetTempPath()) ("jit-build-" + [System.Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $context -Force | Out-Null

    Get-BuildContext -Ref $Ref -Destination $context

    $localRef = "${ImageName}:${Tag}"
    $registryRef = "localhost:${RegistryPort}/${ImageName}:${Tag}"

    Write-Step "Building image '$localRef' from $context ..."
    docker build -t $localRef $context
    if ($LASTEXITCODE -ne 0) { throw "docker build failed." }

    Write-Step "Tagging '$localRef' as '$registryRef'."
    docker tag $localRef $registryRef
    if ($LASTEXITCODE -ne 0) { throw "docker tag failed." }

    Write-Step "Pushing '$registryRef' ..."
    docker push $registryRef
    if ($LASTEXITCODE -ne 0) { throw "docker push failed." }

    Write-Step "Done. '$registryRef' is available in the local registry."
    exit 0
}
catch {
    Write-Error "[build-jit-server] $($_.Exception.Message)"
    exit 1
}
finally {
    if ($context -and (Test-Path $context)) {
        Remove-Item -Path $context -Recurse -Force -ErrorAction SilentlyContinue
    }
}

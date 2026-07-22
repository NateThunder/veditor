[CmdletBinding()]
param(
    [ValidateSet("CPU", "CUDA")]
    [string]$Mode = "CPU",
    [switch]$Repair
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

#== installation constants ===================================================
$PythonVersion = "3.12.7"
$PythonArchiveHash = "0D57BB6CB078B74D23DBFE91F77D6780D45BED328911609F1F7EE2BA1606BF44"
$RembgVersion = "2.0.77"
$SourceDirectory = Split-Path -Parent $PSCommandPath
$VeditorRoot = Join-Path $env:LOCALAPPDATA "Veditor"
$RuntimeDirectory = Join-Path $VeditorRoot "BackgroundRemoval"
$ModelsDirectory = Join-Path $VeditorRoot "Models\Rembg"
$DownloadCacheDirectory = Join-Path $VeditorRoot "Downloads\BackgroundRemoval"
$PipCacheDirectory = Join-Path $DownloadCacheDirectory "pip"
$PythonDirectory = Join-Path $RuntimeDirectory "python"
$PythonExecutable = Join-Path $PythonDirectory "python.exe"
$WorkerPath = Join-Path $RuntimeDirectory "worker.py"
$MarkerPath = Join-Path $RuntimeDirectory "installation.json"
#==============================================================================


#== logging ==================================================================
function Write-InstallerStatus {
    param([string]$Stage, [string]$Message)
    Write-Output "[$Stage] $Message"
}

function Write-InstallerResult {
    param([bool]$Success, [string]$SelectedMode, [string]$Message)
    [ordered]@{
        type = "installer_result"
        success = $Success
        mode = $SelectedMode
        complete = $Success
        message = $Message
    } | ConvertTo-Json -Compress | Write-Output
}

function Write-InstallerProgress {
    param(
        [double]$Percent,
        [string]$Stage,
        [string]$Message,
        $BytesReceived = $null,
        $BytesTotal = $null
    )
    $result = [ordered]@{
        type = "installer_progress"
        percent = [Math]::Max(0, [Math]::Min(100, $Percent))
        stage = $Stage
        message = $Message
    }
    if ($null -ne $BytesReceived) {
        $result.bytesReceived = [long]$BytesReceived
    }
    if ($null -ne $BytesTotal) {
        $result.bytesTotal = [long]$BytesTotal
    }
    $result | ConvertTo-Json -Compress | Write-Output
}
#==============================================================================


#== precondition checks =======================================================
function Assert-InstallationPaths {
    if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
        throw "Background Removal currently supports only x64 Intel/AMD Windows."
    }

    $expectedParent = [IO.Path]::GetFullPath($VeditorRoot).TrimEnd('\') + '\'
    $resolvedRuntime = [IO.Path]::GetFullPath($RuntimeDirectory).TrimEnd('\') + '\'
    if (-not $resolvedRuntime.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The runtime directory resolved outside the expected Veditor LocalAppData root."
    }

    foreach ($requiredFile in @("veditor_background_worker.py", "requirements-windows-cpu.lock", "requirements-windows-cuda.lock")) {
        if (-not (Test-Path -LiteralPath (Join-Path $SourceDirectory $requiredFile) -PathType Leaf)) {
            throw "Installer source file is missing: $requiredFile"
        }
    }
}
#==============================================================================


#== downloads and Python process execution ===================================
function Test-FileHash {
    param([string]$Path, [string]$ExpectedHash)
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and
        ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $ExpectedHash)
}

function Invoke-VerifiedDownload {
    param(
        [string]$Url,
        [string]$Destination,
        [string]$ExpectedHash,
        [double]$StartPercent,
        [double]$EndPercent,
        [string]$Stage,
        [string]$Description
    )

    if (Test-FileHash $Destination $ExpectedHash) {
        $existingLength = (Get-Item -LiteralPath $Destination).Length
        Write-InstallerProgress $EndPercent $Stage "Using verified cached $Description." $existingLength $existingLength
        return
    }

    Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
    $partialPath = "$Destination.partial"
    Remove-Item -LiteralPath $partialPath -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null

    Add-Type -AssemblyName System.Net.Http
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd("Veditor/1.0")
    $response = $null
    $source = $null
    $output = $null
    try {
        $response = $client.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $response.EnsureSuccessStatusCode()
        $total = $response.Content.Headers.ContentLength
        $source = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $output = [IO.File]::Open($partialPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $buffer = New-Object byte[] (1024 * 1024)
        [long]$received = 0
        $lastReportedPercent = -1
        while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $output.Write($buffer, 0, $read)
            $received += $read
            $fraction = if ($null -ne $total -and $total -gt 0) { [Math]::Min(1, $received / $total) } else { 0 }
            $percent = $StartPercent + (($EndPercent - $StartPercent) * $fraction)
            $roundedPercent = [int][Math]::Floor($percent)
            if ($roundedPercent -ne $lastReportedPercent) {
                $receivedMb = $received / 1MB
                $message = if ($null -ne $total -and $total -gt 0) {
                    "Downloading ${Description}: $($receivedMb.ToString('0.0')) of $(($total / 1MB).ToString('0.0')) MB"
                } else {
                    "Downloading ${Description}: $($receivedMb.ToString('0.0')) MB"
                }
                Write-InstallerProgress $percent $Stage $message $received $total
                $lastReportedPercent = $roundedPercent
            }
        }
        $output.Dispose()
        $output = $null

        if (-not (Test-FileHash $partialPath $ExpectedHash)) {
            throw "$Description verification failed."
        }
        Move-Item -LiteralPath $partialPath -Destination $Destination -Force
        Write-InstallerProgress $EndPercent $Stage "$Description download verified." $received $received
    }
    catch {
        Remove-Item -LiteralPath $partialPath -Force -ErrorAction SilentlyContinue
        throw
    }
    finally {
        if ($null -ne $output) { $output.Dispose() }
        if ($null -ne $source) { $source.Dispose() }
        if ($null -ne $response) { $response.Dispose() }
        $client.Dispose()
        $handler.Dispose()
    }
}

function Invoke-PythonChecked {
    param([string[]]$Arguments, [string]$FailureMessage)
    & $PythonExecutable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)."
    }
}
#==============================================================================


#== portable Python installation =============================================
function Install-PortablePython {
    if ((Test-Path -LiteralPath $PythonExecutable -PathType Leaf) -and -not $Repair) {
        Write-InstallerStatus "python" "Portable Python is already present."
        Write-InstallerProgress 15 "python" "Portable Python is ready."
        return
    }

    Write-InstallerStatus "python" "Downloading portable Python $PythonVersion."
    $archive = Join-Path $DownloadCacheDirectory "python-$PythonVersion-embed-amd64.zip"
    Invoke-VerifiedDownload `
        "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip" `
        $archive `
        $PythonArchiveHash `
        1 `
        12 `
        "python" `
        "portable Python $PythonVersion"

    Write-InstallerProgress 13 "python" "Extracting portable Python $PythonVersion."
    if (Test-Path -LiteralPath $PythonDirectory) {
        Remove-Item -LiteralPath $PythonDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PythonDirectory -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $PythonDirectory -Force

    $pthFile = Join-Path $PythonDirectory "python312._pth"
    $pthContent = Get-Content -LiteralPath $pthFile | ForEach-Object {
        if ($_ -eq "#import site") { "import site" } else { $_ }
    }
    if ($pthContent -notcontains "Lib\site-packages") {
        $pthContent += "Lib\site-packages"
    }
    Set-Content -LiteralPath $pthFile -Value $pthContent -Encoding Ascii
    New-Item -ItemType Directory -Path (Join-Path $PythonDirectory "Lib\site-packages") -Force | Out-Null

    Write-InstallerStatus "python" "Bootstrapping Python packaging tools."
    Write-InstallerProgress 15 "packaging" "Downloading Python packaging bootstrap."
    $getPip = Join-Path $env:TEMP "veditor-background-get-pip.py"
    Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $getPip -UseBasicParsing
    Write-InstallerProgress 17 "packaging" "Installing Python packaging bootstrap."
    & $PythonExecutable $getPip --no-warn-script-location
    $pipExitCode = $LASTEXITCODE
    Remove-Item -LiteralPath $getPip -Force -ErrorAction SilentlyContinue
    if ($pipExitCode -ne 0) {
        throw "pip bootstrap failed (exit code $pipExitCode)."
    }
    Write-InstallerProgress 20 "packaging" "Installing pinned Python packaging tools."
    Invoke-PythonChecked @("-m", "pip", "install", "--upgrade", "pip==24.3.1", "setuptools==75.6.0", "wheel==0.45.1", "--cache-dir", $PipCacheDirectory, "--no-warn-script-location", "--progress-bar", "off") "Python packaging tools could not be installed"
    Write-InstallerProgress 27 "packaging" "Python packaging tools are ready."
}
#==============================================================================


#== dependency installation ==================================================
function Install-Dependencies {
    param([string]$SelectedMode)
    $lockName = if ($SelectedMode -eq "CUDA") { "requirements-windows-cuda.lock" } else { "requirements-windows-cpu.lock" }
    $runtimeLock = Join-Path $RuntimeDirectory $lockName
    Copy-Item -LiteralPath (Join-Path $SourceDirectory $lockName) -Destination $runtimeLock -Force
    Write-InstallerStatus "dependencies" "Installing pinned rembg $RembgVersion $SelectedMode dependencies."
    Write-InstallerProgress 28 "dependencies" "Resolving pinned rembg $RembgVersion $SelectedMode dependencies."
    Invoke-PythonChecked @("-m", "pip", "install", "--requirement", $runtimeLock, "--cache-dir", $PipCacheDirectory, "--no-warn-script-location", "--progress-bar", "off") "Pinned dependencies could not be installed"
    Write-InstallerProgress 53 "dependencies" "Pinned rembg $SelectedMode dependencies are installed."

    if ($SelectedMode -eq "CUDA") {
        $cudaCheck = "import onnxruntime as ort; assert 'CUDAExecutionProvider' in ort.get_available_providers(), 'CUDA Execution Provider is unavailable'"
        & $PythonExecutable -c $cudaCheck
        if ($LASTEXITCODE -ne 0) {
            Write-InstallerStatus "fallback" "CUDA is incompatible or unavailable. Continuing automatically with the CPU runtime."
            Invoke-PythonChecked @("-m", "pip", "uninstall", "--yes", "onnxruntime-gpu") "The incompatible CUDA package could not be removed"
            Install-Dependencies "CPU"
            $script:selectedMode = "CPU"
            return
        }
    }
    $script:selectedMode = $SelectedMode
}
#==============================================================================


#== worker, models, and marker ================================================
function Install-WorkerAndModels {
    Copy-Item -LiteralPath (Join-Path $SourceDirectory "veditor_background_worker.py") -Destination $WorkerPath -Force
    $env:U2NET_HOME = $ModelsDirectory
    $env:PYTHONUNBUFFERED = "1"
    Write-InstallerStatus "models" "Downloading Fast, Balanced, and Best Quality models."
    Write-InstallerProgress 55 "models" "Preparing Fast, Balanced, and Best Quality model downloads."
    Invoke-PythonChecked @($WorkerPath, "--download-models") "Background-removal model download failed"
}

function Write-InstallationMarker {
    param([string]$SelectedMode)
    [ordered]@{
        schemaVersion = 1
        complete = $true
        mode = $SelectedMode
        pythonVersion = $PythonVersion
        rembgVersion = $RembgVersion
        models = @("u2netp", "u2net", "birefnet-general")
        installedAtUtc = [DateTime]::UtcNow.ToString("o")
    } | ConvertTo-Json | Set-Content -LiteralPath $MarkerPath -Encoding UTF8
}
#==============================================================================


#== entry point ===============================================================
$selectedMode = $Mode
try {
    Assert-InstallationPaths
    Write-InstallerProgress 0 "preparation" "Preparing the Background Removal runtime installation."
    if ($Repair -and (Test-Path -LiteralPath $RuntimeDirectory)) {
        Write-InstallerStatus "repair" "Rebuilding the runtime while preserving downloaded models."
        Remove-Item -LiteralPath $RuntimeDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $RuntimeDirectory, $ModelsDirectory, $DownloadCacheDirectory, $PipCacheDirectory, (Join-Path $RuntimeDirectory "temp") -Force | Out-Null
    Install-PortablePython
    Install-Dependencies $selectedMode
    Install-WorkerAndModels
    Write-InstallerProgress 97 "verification" "Writing the verified runtime installation record."
    Write-InstallationMarker $selectedMode

    Write-InstallerProgress 98 "verification" "Verifying the completed Background Removal runtime."
    & $PythonExecutable $WorkerPath --check
    if ($LASTEXITCODE -ne 0) {
        throw "Final background-removal runtime verification failed."
    }
    Write-InstallerProgress 100 "completed" "Background Removal runtime installation completed."
    Write-InstallerResult $true $selectedMode "Background Removal installation completed successfully."
    exit 0
}
catch {
    Remove-Item -LiteralPath $MarkerPath -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $ModelsDirectory, $DownloadCacheDirectory -Filter "*.partial" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Write-Error $_.Exception.Message
    Write-InstallerResult $false $selectedMode $_.Exception.Message
    exit 1
}
#==============================================================================

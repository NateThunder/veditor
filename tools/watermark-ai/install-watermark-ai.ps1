[CmdletBinding()]
param(
    [ValidateSet("Auto", "CPU", "CUDA")]
    [string]$Mode = "Auto",
    [switch]$Repair,
    [switch]$SkipModelDownload,
    [string]$FfmpegPath
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

#== installation constants ===================================================
$PythonVersion = "3.12.7"
$PythonArchiveHash = "0D57BB6CB078B74D23DBFE91F77D6780D45BED328911609F1F7EE2BA1606BF44"
$FlorenceModelId = "florence-community/Florence-2-large"
$LamaUrl = "https://github.com/Sanster/models/releases/download/add_big_lama/big-lama.pt"
$LamaSha256 = "344C77BBCB158F17DD143070D1E789F38A66C04202311AE3A258EF66667A9EA9"
$MinimumCudaVersion = [Version]"12.4"

$SourceDirectory = Split-Path -Parent $PSCommandPath
$VeditorRoot = Join-Path $env:LOCALAPPDATA "Veditor"
$RuntimeDirectory = Join-Path $VeditorRoot "WatermarkAI"
$ModelsDirectory = Join-Path $VeditorRoot "Models"
$HuggingFaceDirectory = Join-Path $ModelsDirectory "HuggingFace"
$TorchDirectory = Join-Path $ModelsDirectory "Torch"
$PythonDirectory = Join-Path $RuntimeDirectory "python"
$PythonExecutable = Join-Path $PythonDirectory "python.exe"
$MarkerPath = Join-Path $RuntimeDirectory "installation.json"
$LogDirectory = Join-Path $RuntimeDirectory "logs"
$TempDirectory = Join-Path $RuntimeDirectory "temp"
#==============================================================================


#== logging ===================================================================
function Write-InstallerStatus {
    param([string]$Stage, [string]$Message)

    Write-Output "[$Stage] $Message"
}

function Write-InstallerResult {
    param([bool]$Success, [string]$SelectedMode, [bool]$Complete, [string]$Message)

    [ordered]@{
        type = "installer_result"
        success = $Success
        mode = $SelectedMode
        complete = $Complete
        message = $Message
    } | ConvertTo-Json -Compress | Write-Output
}
#==============================================================================


#== precondition checks =======================================================
function Assert-X64Windows {
    if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
        throw "WatermarkAI currently supports only x64 Intel/AMD Windows."
    }
}

function Assert-SourceFiles {
    $requiredFiles = @(
        "veditor_worker.py",
        "check-watermark-runtime.py",
        "requirements-windows-cpu.lock",
        "requirements-windows-cuda.lock"
    )

    foreach ($file in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $SourceDirectory $file) -PathType Leaf)) {
            throw "Installer source file is missing: $file"
        }
    }
}

function Assert-RuntimePath {
    $expectedParent = [IO.Path]::GetFullPath($VeditorRoot).TrimEnd('\') + '\'
    $resolvedRuntime = [IO.Path]::GetFullPath($RuntimeDirectory).TrimEnd('\') + '\'
    if (-not $resolvedRuntime.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The runtime directory resolved outside the expected Veditor LocalAppData root."
    }
}

function Resolve-Ffmpeg {
    if ($FfmpegPath -and (Test-Path -LiteralPath $FfmpegPath -PathType Leaf)) {
        return [IO.Path]::GetFullPath($FfmpegPath)
    }

    $command = Get-Command "ffmpeg.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw "FFmpeg was not found. Supply -FfmpegPath or place ffmpeg.exe on PATH."
}
#==============================================================================


#== installation mode selection ==============================================
function Get-CompatibleNvidiaGpu {
    $nvidiaSmi = Get-Command "nvidia-smi.exe" -ErrorAction SilentlyContinue
    if (-not $nvidiaSmi) {
        return $null
    }

    try {
        $summary = (& $nvidiaSmi.Source 2>$null | Out-String)
        $cudaMatch = [regex]::Match($summary, "CUDA Version:\s*(\d+(?:\.\d+)?)")
        if (-not $cudaMatch.Success -or [Version]$cudaMatch.Groups[1].Value -lt $MinimumCudaVersion) {
            return $null
        }

        $gpuName = (& $nvidiaSmi.Source --query-gpu=name --format=csv,noheader 2>$null | Select-Object -First 1).Trim()
        if ([string]::IsNullOrWhiteSpace($gpuName)) {
            return $null
        }

        return [pscustomobject]@{
            Name = $gpuName
            CudaVersion = $cudaMatch.Groups[1].Value
        }
    }
    catch {
        return $null
    }
}

function Select-InstallationMode {
    if ($Mode -eq "CPU") {
        return $Mode
    }

    $gpu = Get-CompatibleNvidiaGpu
    if ($Mode -eq "CUDA") {
        if (-not $gpu) {
            throw "CUDA mode was requested, but no compatible NVIDIA GPU/driver (CUDA 12.4 or newer) was detected."
        }

        Write-InstallerStatus "mode" "Compatible NVIDIA GPU detected: $($gpu.Name) (CUDA $($gpu.CudaVersion))."
        return "CUDA"
    }

    if ($gpu) {
        Write-InstallerStatus "mode" "Compatible NVIDIA GPU detected: $($gpu.Name) (CUDA $($gpu.CudaVersion))."
        return "CUDA"
    }

    Write-InstallerStatus "mode" "No compatible CUDA device detected; selecting CPU mode."
    return "CPU"
}
#==============================================================================


#== process execution =========================================================
function Invoke-PythonChecked {
    param([string[]]$Arguments, [string]$FailureMessage)

    & $PythonExecutable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)."
    }
}

function Invoke-CurlDownload {
    param([string]$Url, [string]$Destination)

    $curl = Get-Command "curl.exe" -ErrorAction SilentlyContinue
    if ($curl) {
        & $curl.Source --fail --location --retry 3 --continue-at - --output $Destination $Url
        if ($LASTEXITCODE -ne 0) {
            throw "Download failed for $Url (curl exit code $LASTEXITCODE)."
        }
        return
    }

    Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing
}
#==============================================================================


#== portable Python installation =============================================
function Install-PortablePython {
    if ((Test-Path -LiteralPath $PythonExecutable -PathType Leaf) -and -not $Repair) {
        Write-InstallerStatus "python" "Portable Python is already present."
        return
    }

    Write-InstallerStatus "python" "Downloading portable Python $PythonVersion."
    $archive = Join-Path $env:TEMP "veditor-python-$PythonVersion-embed-amd64.zip"
    $pythonUrl = "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip"
    Invoke-CurlDownload $pythonUrl $archive

    $actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    if ($actualHash -ne $PythonArchiveHash) {
        Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
        throw "Portable Python archive verification failed."
    }

    if (Test-Path -LiteralPath $PythonDirectory) {
        Remove-Item -LiteralPath $PythonDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PythonDirectory -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $PythonDirectory -Force
    Remove-Item -LiteralPath $archive -Force

    $pthFile = Join-Path $PythonDirectory "python312._pth"
    $pthContent = Get-Content -LiteralPath $pthFile
    $pthContent = $pthContent | ForEach-Object {
        if ($_ -eq "#import site") { "import site" } else { $_ }
    }
    if ($pthContent -notcontains "Lib\site-packages") {
        $pthContent += "Lib\site-packages"
    }
    Set-Content -LiteralPath $pthFile -Value $pthContent -Encoding Ascii
    New-Item -ItemType Directory -Path (Join-Path $PythonDirectory "Lib\site-packages") -Force | Out-Null

    Write-InstallerStatus "python" "Bootstrapping pip."
    $getPip = Join-Path $env:TEMP "veditor-get-pip.py"
    Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $getPip -UseBasicParsing
    & $PythonExecutable $getPip --no-warn-script-location
    $pipExitCode = $LASTEXITCODE
    Remove-Item -LiteralPath $getPip -Force -ErrorAction SilentlyContinue
    if ($pipExitCode -ne 0) {
        throw "pip bootstrap failed (exit code $pipExitCode)."
    }

    Invoke-PythonChecked @("-m", "pip", "install", "--upgrade", "pip==24.3.1", "setuptools==75.6.0", "wheel==0.45.1", "--no-warn-script-location") "Python packaging tools could not be installed"
}
#==============================================================================


#== dependency installation ===================================================
function Install-Dependencies {
    param([string]$SelectedMode)

    $lockName = if ($SelectedMode -eq "CUDA") { "requirements-windows-cuda.lock" } else { "requirements-windows-cpu.lock" }
    $sourceLock = Join-Path $SourceDirectory $lockName
    $runtimeLock = Join-Path $RuntimeDirectory $lockName
    Copy-Item -LiteralPath $sourceLock -Destination $runtimeLock -Force

    Write-InstallerStatus "dependencies" "Installing pinned $SelectedMode dependencies."
    Invoke-PythonChecked @("-m", "pip", "install", "--requirement", $runtimeLock, "--no-warn-script-location") "Pinned dependencies could not be installed"

    $verificationCode = "import cv2, diffusers, huggingface_hub, numpy, PIL, pydantic, torch, torchvision, transformers; print('IMPORTS_OK')"
    Invoke-PythonChecked @("-c", $verificationCode) "Dependency import verification failed"

    if ($SelectedMode -eq "CUDA") {
        $cudaCode = "import json, torch; assert torch.version.cuda, 'CPU-only PyTorch build installed'; assert torch.cuda.is_available(), 'CUDA unavailable'; print(json.dumps({'gpuName': torch.cuda.get_device_name(0), 'cudaVersion': torch.version.cuda}))"
        Invoke-PythonChecked @("-c", $cudaCode) "CUDA verification failed"
    }
}
#==============================================================================


#== worker and model preparation =============================================
function Install-WorkerFiles {
    Copy-Item -LiteralPath (Join-Path $SourceDirectory "veditor_worker.py") -Destination (Join-Path $RuntimeDirectory "worker.py") -Force
    Copy-Item -LiteralPath (Join-Path $SourceDirectory "check-watermark-runtime.py") -Destination (Join-Path $RuntimeDirectory "check-watermark-runtime.py") -Force
}

function Install-Models {
    $lamaDirectory = Join-Path $TorchDirectory "hub\checkpoints"
    $lamaPath = Join-Path $lamaDirectory "big-lama.pt"
    New-Item -ItemType Directory -Path $lamaDirectory -Force | Out-Null

    $lamaValid = (Test-Path -LiteralPath $lamaPath -PathType Leaf) -and
        ((Get-FileHash -LiteralPath $lamaPath -Algorithm SHA256).Hash -eq $LamaSha256)
    if (-not $lamaValid) {
        Write-InstallerStatus "models" "Downloading LaMA (~196 MB)."
        Remove-Item -LiteralPath $lamaPath -Force -ErrorAction SilentlyContinue
        $partialLamaPath = "$lamaPath.partial"
        Invoke-CurlDownload $LamaUrl $partialLamaPath
        if ((Get-FileHash -LiteralPath $partialLamaPath -Algorithm SHA256).Hash -ne $LamaSha256) {
            Remove-Item -LiteralPath $partialLamaPath -Force -ErrorAction SilentlyContinue
            throw "LaMA model integrity verification failed."
        }
        Move-Item -LiteralPath $partialLamaPath -Destination $lamaPath -Force
    }
    else {
        Write-InstallerStatus "models" "LaMA is already available."
    }

    Write-InstallerStatus "models" "Preparing Florence-2-large (~1.5 GB, resumable)."
    # A single worker avoids a Windows race in Hugging Face's symlink capability
    # probe. Standard users then receive the supported copy-based cache layout.
    $downloadCode = "from huggingface_hub import snapshot_download; snapshot_download('$FlorenceModelId', max_workers=1); print('FLORENCE_OK')"
    Invoke-PythonChecked @("-c", $downloadCode) "Florence-2 download failed"
}
#==============================================================================


#== installation marker =======================================================
function Write-InstallationMarker {
    param([string]$SelectedMode, [bool]$Complete)

    $torchVersion = (& $PythonExecutable -c "import torch; print(torch.__version__)" | Select-Object -Last 1).Trim()
    $transformersVersion = (& $PythonExecutable -c "import transformers; print(transformers.__version__)" | Select-Object -Last 1).Trim()
    [ordered]@{
        schemaVersion = 2
        complete = $Complete
        mode = $SelectedMode
        pythonVersion = $PythonVersion
        torchVersion = $torchVersion
        transformersVersion = $transformersVersion
        florenceModel = $FlorenceModelId
        lamaModel = "big-lama.pt"
        installedAtUtc = [DateTime]::UtcNow.ToString("o")
    } | ConvertTo-Json | Set-Content -LiteralPath $MarkerPath -Encoding UTF8
}
#==============================================================================


#== entry point ===============================================================
$selectedMode = $Mode
$installationStarted = $false
try {
    Assert-X64Windows
    Assert-SourceFiles
    Assert-RuntimePath
    $resolvedFfmpeg = Resolve-Ffmpeg
    $selectedMode = Select-InstallationMode
    $installationStarted = $true

    if ($Repair -and (Test-Path -LiteralPath $RuntimeDirectory)) {
        Write-InstallerStatus "repair" "Rebuilding the local runtime while preserving downloaded models."
        Remove-Item -LiteralPath $RuntimeDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $RuntimeDirectory, $HuggingFaceDirectory, $TorchDirectory, $LogDirectory, $TempDirectory -Force | Out-Null
    $env:HF_HOME = $HuggingFaceDirectory
    $env:HF_HUB_DISABLE_SYMLINKS_WARNING = "1"
    $env:TORCH_HOME = $TorchDirectory
    $env:VEDITOR_FFMPEG_PATH = $resolvedFfmpeg
    $env:PYTHONUNBUFFERED = "1"

    Install-PortablePython
    Install-Dependencies $selectedMode
    Install-WorkerFiles

    $complete = -not $SkipModelDownload
    if ($complete) {
        Install-Models
    }
    else {
        Write-InstallerStatus "models" "Model download skipped; runtime will remain incomplete."
    }

    Write-InstallationMarker $selectedMode $complete
    $checkArguments = @((Join-Path $RuntimeDirectory "check-watermark-runtime.py"), "--ffmpeg-path", $resolvedFfmpeg)
    & $PythonExecutable @checkArguments
    $checkExitCode = $LASTEXITCODE

    if ($complete -and $checkExitCode -ne 0) {
        throw "Final runtime verification failed."
    }

    if ($complete) {
        Write-InstallerResult $true $selectedMode $true "WatermarkAI installation completed successfully."
    }
    else {
        Write-InstallerResult $true $selectedMode $false "Runtime dependencies installed; models were intentionally skipped."
    }
    exit 0
}
catch {
    if ($installationStarted) {
        Remove-Item -LiteralPath $MarkerPath -Force -ErrorAction SilentlyContinue
    }
    Write-Error $_.Exception.Message
    Write-InstallerResult $false $selectedMode $false $_.Exception.Message
    exit 1
}
#==============================================================================

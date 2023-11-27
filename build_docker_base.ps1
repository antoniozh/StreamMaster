param (
    [switch]$buildProd,
    [switch]$printCommands
)

$env:DOCKER_BUILDKIT = 1
$env:COMPOSE_DOCKER_CLI_BUILD = 1

# Define the base image name
$imageName = "docker.io/senexcrenshaw/streammaster_base"

$gitVersion = "dotnet-gitversion"
&$gitVersion /updateAssemblyInfo | Out-Null

$json = &$gitVersion /output json | Out-String
$obj = $json | ConvertFrom-Json 
$buildMetaDataPadded = $obj.AssemblySemVer

# Set the tag based on the build type
if ($buildProd) {
    $tag = "latest"
}
else {
    $tag = "$buildMetaDataPadded"
}

# Define the docker build commands for each architecture
$amd64Command = "docker buildx build --platform linux/amd64 -f Dockerfile.amd64 -t ""${imageName}:${tag}-amd64"" --load ."
$arm64Command = "docker buildx build --platform linux/arm64 -f Dockerfile.arm64 -t ""${imageName}:${tag}-arm64"" --load ."

# Load necessary assemblies for runspaces
Add-Type -AssemblyName System.Management.Automation

# Function to create and start a runspace
function ExecuteOrPrint {
    param([string]$command)

    write-host $command 
    if ($printCommands) {
        return $null
    }

    # Create a PowerShell instance and add the script
    $powershell = [powershell]::Create().AddScript({
            param($cmd)
            Invoke-Expression $cmd 2>&1 | ForEach-Object { Write-Host $_ }
        }).AddArgument($command)

    # Create the runspace and begin execution
    $runspace = [runspacefactory]::CreateRunspace()
    $powershell.Runspace = $runspace
    $runspace.Open()
    $asyncResult = $powershell.BeginInvoke()
    return @{ PowerShell = $powershell; AsyncResult = $asyncResult }
}


# Execute, print, or run the commands in parallel
$amd64Job = ExecuteOrPrint -command $amd64Command
$arm64Job = ExecuteOrPrint -command $arm64Command

# Wait for the jobs to complete and output their results
if (-not $printCommands) {
    $amd64Job.PowerShell.EndInvoke($amd64Job.AsyncResult)
    $arm64Job.PowerShell.EndInvoke($arm64Job.AsyncResult)

    $amd64Job.PowerShell.Runspace.Close()
    $arm64Job.PowerShell.Runspace.Close()

    $amd64Job.PowerShell.Dispose()
    $arm64Job.PowerShell.Dispose()
}
# Push the images (These commands are not run in parallel)
$pushCommand1 = "docker push ""${imageName}:${tag}-amd64"""
$pushCommand2 = "docker push ""${imageName}:${tag}-arm64"""

# Push the images using runspaces
$pushJob1 = ExecuteOrPrint -command $pushCommand1
$pushJob2 = ExecuteOrPrint -command $pushCommand2

if (-not $printCommands) {
    $pushJob1.PowerShell.EndInvoke($pushJob1.AsyncResult)
    $pushJob2.PowerShell.EndInvoke($pushJob2.AsyncResult)

    $pushJob1.PowerShell.Runspace.Close()
    $pushJob2.PowerShell.Runspace.Close()

    $pushJob1.PowerShell.Dispose()
    $pushJob2.PowerShell.Dispose()
}

# Create and push the manifest
$rmManifestCommand1 = "docker manifest rm ""${imageName}:${tag}"""
$manifestCommand1 = "docker manifest create ""${imageName}:${tag}"" --amend ""${imageName}:${tag}-amd64"" --amend ""${imageName}:${tag}-arm64"""
$manifestCommand2 = "docker manifest push ""${imageName}:${tag}"""

$rmManifestJob = ExecuteOrPrint -command $rmManifestCommand1
$manifestJob1 = ExecuteOrPrint -command $manifestCommand1
$manifestJob2 = ExecuteOrPrint -command $manifestCommand2

if (-not $printCommands) {
    $rmManifestJob.PowerShell.EndInvoke($rmManifestJob.AsyncResult)
    $rmManifestJob.PowerShell.Runspace.Close()
    $rmManifestJob.PowerShell.Dispose()

    $manifestJob1.PowerShell.EndInvoke($manifestJob1.AsyncResult)
    $manifestJob2.PowerShell.EndInvoke($manifestJob2.AsyncResult)

    $manifestJob1.PowerShell.Runspace.Close()
    $manifestJob2.PowerShell.Runspace.Close()

    $manifestJob1.PowerShell.Dispose()
    $manifestJob2.PowerShell.Dispose()
}
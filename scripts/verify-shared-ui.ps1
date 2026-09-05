param(
  [Parameter(Mandatory=$true)][string]$Directory,
  [Parameter(Mandatory=$true)][ValidateSet("MacOS", "Windows")][string]$Platform,
  [string]$Manifest = (Join-Path $PSScriptRoot "../packages/foundry-ui-manifest.json")
)
$ErrorActionPreference = "Stop"
$data = Get-Content $Manifest -Raw | ConvertFrom-Json
foreach ($property in $data.files.PSObject.Properties) {
  $name = $property.Name
  $path = Join-Path $Directory $name
  if ($name -eq "RhinoFoundry.UI.MacOS.dll" -and $Platform -eq "Windows") {
    if (Test-Path $path) { throw "Windows bundle contains the Mac adapter: $path" }
    continue
  }
  if (-not (Test-Path $path)) { throw "Missing shared binary: $path" }
  $actual = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actual -ne $property.Value) { throw "Shared binary hash mismatch: $path" }
}
Write-Host "Shared UI $($data.version): exact $Platform package binaries verified"

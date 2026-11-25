Import-Module Pester -MinimumVersion 5.0.0 -Force
$result = Invoke-Pester -Path $PSScriptRoot -PassThru -Output Normal
if ($result.FailedCount -gt 0) { exit 1 }
exit 0

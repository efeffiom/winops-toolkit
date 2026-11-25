BeforeAll {
    $ModulePath = Join-Path $PSScriptRoot '..\PowerShell\WinOps.Snapshot.psm1'
    Import-Module $ModulePath -Force
}

Describe 'WinOps.Snapshot' {
    It 'collects a real read-only system snapshot' {
        $snapshot = Get-WinOpsSnapshot -ComputerName 'TEST-PC'

        $snapshot.ComputerName | Should -Be 'TEST-PC'
        $snapshot.OsCaption | Should -Not -BeNullOrEmpty
        $snapshot.OsVersion | Should -Not -BeNullOrEmpty
        $snapshot.MemoryGb | Should -BeGreaterThan 0
        $snapshot.CollectedAt | Should -BeOfType [datetime]
    }

    It 'exports a snapshot to JSON' {
        $path = Join-Path ([System.IO.Path]::GetTempPath()) ("winops-pester-{0}.json" -f [guid]::NewGuid().ToString('N'))
        try {
            $snapshot = Get-WinOpsSnapshot -ComputerName 'EXPORT-PC'
            $file = $snapshot | Export-WinOpsSnapshot -Path $path -Format Json

            $file.Exists | Should -BeTrue
            $file.Length | Should -BeGreaterThan 0
            $content = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            $content.ComputerName | Should -Be 'EXPORT-PC'
        }
        finally {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }

    It 'exports a snapshot to CSV' {
        $path = Join-Path ([System.IO.Path]::GetTempPath()) ("winops-pester-{0}.csv" -f [guid]::NewGuid().ToString('N'))
        try {
            $snapshot = Get-WinOpsSnapshot -ComputerName 'CSV-PC'
            $file = $snapshot | Export-WinOpsSnapshot -Path $path -Format Csv

            $file.Exists | Should -BeTrue
            $rows = Import-Csv -LiteralPath $path
            $rows[0].ComputerName | Should -Be 'CSV-PC'
        }
        finally {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }
}

param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'html'))
$archivePath = Join-Path $PSScriptRoot 'OpenSim.Continuum.WebUI/WhiteCoreWebUI.zip'
$temporaryArchive = $archivePath + '.tmp'
try {
    $stream = [IO.File]::Open($temporaryArchive, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $assetRoot -Recurse -File | Sort-Object FullName)) {
            if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Asset links are not supported' }
            $relative = $file.FullName.Substring($assetRoot.Length + 1).Replace('\', '/')
            $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $inputStream = $file.OpenRead()
            $outputStream = $entry.Open()
            try { $inputStream.CopyTo($outputStream) }
            finally { $outputStream.Dispose(); $inputStream.Dispose() }
        }
    }
    finally { $archive.Dispose(); $stream.Dispose() }
    Move-Item -LiteralPath $temporaryArchive -Destination $archivePath -Force
    Write-Output "Updated $archivePath"
}
finally { if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive } }

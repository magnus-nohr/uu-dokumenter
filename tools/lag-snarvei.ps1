# Lager snarveien «UU-dokumenter» på skrivebordet (kalles av installer.bat).
# Snarveien kjører start.bat minimert og bruker ikonet i assets\uudok.ico.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$icon = Join-Path $root 'assets\uudok.ico'

if (-not (Test-Path $icon)) {
    # Tegn ikonet (blå, avrundet firkant med «UU» og et dokument) og lagre som ICO med PNG-innhold.
    Add-Type -AssemblyName System.Drawing
    $size = 256
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 48; $rect = New-Object System.Drawing.Rectangle 8, 8, ($size - 16), ($size - 16)
    $path.AddArc($rect.X, $rect.Y, $r, $r, 180, 90); $path.AddArc($rect.Right - $r, $rect.Y, $r, $r, 270, 90)
    $path.AddArc($rect.Right - $r, $rect.Bottom - $r, $r, $r, 0, 90); $path.AddArc($rect.X, $rect.Bottom - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 11, 79, 156))), $path)
    $g.FillRectangle([System.Drawing.Brushes]::White, 150, 40, 70, 90)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 11, 79, 156)), 6
    foreach ($y in 62, 82, 102) { $g.DrawLine($pen, 162, $y, 208, $y) }
    $font = New-Object System.Drawing.Font 'Segoe UI', 92, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $g.DrawString('UU', $font, [System.Drawing.Brushes]::White, 22, 108)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $png = $ms.ToArray(); $bmp.Dispose()
    New-Item -ItemType Directory -Force (Split-Path $icon) | Out-Null
    $fs = [System.IO.File]::Create($icon); $w = New-Object System.IO.BinaryWriter $fs
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]1)                 # ICONDIR
    $w.Write([Byte]0); $w.Write([Byte]0); $w.Write([Byte]0); $w.Write([Byte]0)    # 256x256, ingen palett
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$png.Length); $w.Write([UInt32]22)
    $w.Write($png); $w.Close()
}

$desktop = [Environment]::GetFolderPath('Desktop')
$lnk = Join-Path $desktop 'UU-dokumenter.lnk'
$shell = New-Object -ComObject WScript.Shell
$s = $shell.CreateShortcut($lnk)
$s.TargetPath = Join-Path $root 'start.bat'
$s.WorkingDirectory = $root
$s.IconLocation = "$icon,0"
$s.WindowStyle = 7                                   # minimert
$s.Description = 'Start UU-dokumenter (forbedre universell utforming i Word og PowerPoint)'
$s.Save()
Write-Output "Snarvei laget: $lnk"

#requires -Version 5.1
<#
.SYNOPSIS
  生成 ES Luncher 图标：纯黑 / 纯白背景，中部偏上大写 ES，下方小写 luncher。
.DESCRIPTION
  输出 1024 与 256 两种尺寸 PNG，以及 PNG 编码的 256x256 ICO（Windows 可直接用）。
  本机只有 Windows PowerShell 5.1，脚本按 5.1 写，文件必须 UTF-8 带 BOM。
#>
[CmdletBinding()]
param(
    [string]$OutDir
)

if ([string]::IsNullOrEmpty($OutDir)) {
    $scriptPath = $MyInvocation.MyCommand.Path
    if ([string]::IsNullOrEmpty($scriptPath)) { $scriptPath = $PSCommandPath }
    $OutDir = Join-Path (Split-Path -Parent (Split-Path -Parent $scriptPath)) 'assets\icon'
}

Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function New-IconPng {
    param(
        [int]$Size,
        [System.Drawing.Color]$Background,
        [System.Drawing.Color]$Foreground,
        [string]$Path
    )

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $graphics.Clear($Background)

    # ES 用黑体，位置略高于中心
    $esFontSize = [single]($Size * 0.40)
    $esFont = New-Object System.Drawing.Font('Segoe UI Black', $esFontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $esBrush = New-Object System.Drawing.SolidBrush($Foreground)
    $esText = 'ES'
    $esSize = $graphics.MeasureString($esText, $esFont)

    # 收紧字距：ES 两个字母拆开画，间距约 -0.06em
    $letterWidth0 = $graphics.MeasureString('E', $esFont).Width
    $letterWidth1 = $graphics.MeasureString('S', $esFont).Width
    $gap = [single]($Size * -0.004)
    $esWidth = $letterWidth0 + $letterWidth1 + $gap
    $esLeft = [single](($Size - $esWidth) / 2)
    $esTop = [single](($Size * 0.5) - ($esSize.Height / 2) - ($Size * 0.03))
    $graphics.DrawString('E', $esFont, $esBrush, $esLeft, $esTop)
    $graphics.DrawString('S', $esFont, $esBrush, [single]($esLeft + $letterWidth0 + $gap), $esTop)

    # luncher：小写、宽松字距
    $labelSize = [single]($Size * 0.105)
    $labelFont = New-Object System.Drawing.Font('Segoe UI', $labelSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $labelText = 'luncher'
    $letterSpacing = [single]($Size * 0.012)
    $labelWidth = 0.0
    for ($i = 0; $i -lt $labelText.Length; $i++) {
        $labelWidth += $graphics.MeasureString([string]$labelText[$i], $labelFont).Width
    }
    $labelWidth += $letterSpacing * ($labelText.Length - 1)
    $labelLeft = [single](($Size - $labelWidth) / 2)
    $labelTop = [single]($esTop + $esSize.Height + ($Size * 0.015))
    $x = $labelLeft
    for ($i = 0; $i -lt $labelText.Length; $i++) {
        $ch = [string]$labelText[$i]
        $graphics.DrawString($ch, $labelFont, $esBrush, $x, $labelTop)
        $x += $graphics.MeasureString($ch, $labelFont).Width + $letterSpacing
    }

    $graphics.Dispose()
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    $esFont.Dispose()
    $labelFont.Dispose()
    $esBrush.Dispose()
}

function New-Ico {
    param([string]$PngPath, [string]$IcoPath)
    $png = [System.IO.File]::ReadAllBytes($PngPath)
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([UInt16]0)          # reserved
    $writer.Write([UInt16]1)          # type = icon
    $writer.Write([UInt16]1)          # count
    $writer.Write([Byte]0)            # width 0 = 256
    $writer.Write([Byte]0)            # height 0 = 256
    $writer.Write([Byte]0)            # colors
    $writer.Write([Byte]0)            # reserved
    $writer.Write([UInt16]1)          # planes
    $writer.Write([UInt16]32)         # bitcount
    $writer.Write([UInt32]$png.Length)
    $writer.Write([UInt32]22)         # offset
    $writer.Write($png)
    $writer.Flush()
    [System.IO.File]::WriteAllBytes($IcoPath, $stream.ToArray())
    $writer.Dispose()
    $stream.Dispose()
}

$black = [System.Drawing.Color]::FromArgb(0, 0, 0)
$white = [System.Drawing.Color]::FromArgb(255, 255, 255)

foreach ($variant in @(
    @{ Name = 'dark'; Bg = $black; Fg = $white },
    @{ Name = 'light'; Bg = $white; Fg = $black }
)) {
    New-IconPng -Size 1024 -Background $variant.Bg -Foreground $variant.Fg -Path (Join-Path $OutDir "es-luncher-$($variant.Name)-1024.png")
    New-IconPng -Size 256 -Background $variant.Bg -Foreground $variant.Fg -Path (Join-Path $OutDir "es-luncher-$($variant.Name)-256.png")
}

New-Ico -PngPath (Join-Path $OutDir 'es-luncher-dark-256.png') -IcoPath (Join-Path $OutDir 'es-luncher-dark.ico')
New-Ico -PngPath (Join-Path $OutDir 'es-luncher-light-256.png') -IcoPath (Join-Path $OutDir 'es-luncher-light.ico')

Get-ChildItem $OutDir | Select-Object Name, Length

#requires -Version 7.0
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$shotRoot = Join-Path $PSScriptRoot 'gallery/screenshots'
$originalRoot = Join-Path $shotRoot 'originals'
$pngRoot = Join-Path $shotRoot 'png'
$jpgRoot = Join-Path $shotRoot 'upload'
New-Item -ItemType Directory -Path $pngRoot,$jpgRoot -Force | Out-Null
$script:ink = [Drawing.ColorTranslator]::FromHtml('#e6dfcf')
$script:muted = [Drawing.ColorTranslator]::FromHtml('#b3a9b5')
$script:gold = [Drawing.ColorTranslator]::FromHtml('#efcb7c')
$script:teal = [Drawing.ColorTranslator]::FromHtml('#9fc5c0')
$script:fontFamily = 'Microsoft YaHei UI'

function Text([Drawing.Graphics]$g,[string]$value,[float]$x,[float]$y,[float]$width,[float]$height,[float]$size,[Drawing.Color]$color,[bool]$bold=$false) {
    $style = if ($bold) { [Drawing.FontStyle]::Bold } else { [Drawing.FontStyle]::Regular }
    $font = [Drawing.Font]::new($script:fontFamily,$size,$style,[Drawing.GraphicsUnit]::Pixel)
    $brush = [Drawing.SolidBrush]::new($color)
    $format = [Drawing.StringFormat]::new()
    $format.Trimming = [Drawing.StringTrimming]::None
    $format.FormatFlags = [Drawing.StringFormatFlags]::LineLimit
    try { $g.DrawString($value,$font,$brush,[Drawing.RectangleF]::new($x,$y,$width,$height),$format) }
    finally { $format.Dispose(); $brush.Dispose(); $font.Dispose() }
}

function Rule([Drawing.Graphics]$g,[float]$x,[float]$y,[float]$width,[Drawing.Color]$color) {
    $pen = [Drawing.Pen]::new($color,1)
    try { $g.DrawLine($pen,$x,$y,$x+$width,$y) } finally { $pen.Dispose() }
}

function FrameImage([Drawing.Graphics]$g,[Drawing.Image]$image,[Drawing.Rectangle]$crop,[float]$x,[float]$y,[float]$width,[float]$height) {
    $scale = [Math]::Min($width/$crop.Width,$height/$crop.Height)
    $drawWidth = [float]($crop.Width*$scale)
    $drawHeight = [float]($crop.Height*$scale)
    $left = [float]($x+($width-$drawWidth)/2)
    $top = [float]($y+($height-$drawHeight)/2)
    $shadow = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(120,0,0,0))
    $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#625665'),2)
    try {
        $g.FillRectangle($shadow,$left+9,$top+10,$drawWidth,$drawHeight)
        $g.DrawImage($image,[Drawing.RectangleF]::new($left,$top,$drawWidth,$drawHeight),[Drawing.RectangleF]::new($crop.X,$crop.Y,$crop.Width,$crop.Height),[Drawing.GraphicsUnit]::Pixel)
        $g.DrawRectangle($pen,$left-1,$top-1,$drawWidth+2,$drawHeight+2)
    } finally { $pen.Dispose(); $shadow.Dispose() }
}

function Header([Drawing.Graphics]$g,[string]$title,[string]$subtitle,[string]$number,[bool]$excerpt=$false) {
    $accent = [Drawing.SolidBrush]::new($script:teal)
    try { $g.FillRectangle($accent,48,42,7,83) } finally { $accent.Dispose() }
    Text $g $title 78 29 1650 75 51 $script:gold $true
    Text $g $subtitle 82 101 1690 42 25 $script:muted
    Text $g $number 1760 48 125 50 26 $script:teal $true
    Rule $g 48 151 1824 ([Drawing.ColorTranslator]::FromHtml('#554b59'))
    Rule $g 48 1159 1824 ([Drawing.ColorTranslator]::FromHtml('#554b59'))
    Text $g '垃圾场拓展 / SCRAPYARD EXPANSION' 48 1169 1120 30 18 $script:muted
    $caption = if ($excerpt) { '实机截图节选 · Cycbloom' } else { '实机截图 · Cycbloom' }
    Text $g $caption 1585 1169 300 30 18 $script:muted
}

function SaveCard([Drawing.Bitmap]$bitmap,[string]$stem) {
    $bitmap.Save((Join-Path $pngRoot ($stem+'.png')),[Drawing.Imaging.ImageFormat]::Png)
    $codec = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -EQ 'image/jpeg'
    $encoding = [Drawing.Imaging.EncoderParameters]::new(1)
    $encoding.Param[0] = [Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality,[long]94)
    try { $bitmap.Save((Join-Path $jpgRoot ($stem+'.jpg')),$codec,$encoding) }
    finally { $encoding.Dispose() }
}

$cards = @(
    @{ source='01-map-cn.png'; stem='01-eight-area-map'; title='八处地标，按需拾荒'; subtitle='材料、工具、医疗、电子、水源，各有去处'; mode='map' },
    @{ source='02-reputation-cn.png'; stem='02-reputation-progression'; title='积累声望，解锁更多可能'; subtitle='新地点逐步开放，旧地点的基础收益也会同步成长'; mode='reputation' },
    @{ source='03-loot-expectation-cn.png'; stem='03-loot-probability-and-value'; title='掉落概率与收益，一目了然'; subtitle='地点告示牌实时展示物品概率和期望收益拆分'; mode='loot' },
    @{ source='04-map-en.png'; stem='04-english-interface'; title='Explore the scrapyard in English'; subtitle='Select an area, inspect loot chances, and confirm your destination.'; mode='english' }
)

for ($i=0; $i -lt $cards.Count; ++$i) {
    $card = $cards[$i]
    $inputImage = [Drawing.Image]::FromFile((Join-Path $originalRoot $card.source))
    $canvas = [Drawing.Bitmap]::new(1920,1200,[Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [Drawing.Graphics]::FromImage($canvas)
    try {
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $background = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(0,0),[Drawing.Point]::new(1920,1200),
            [Drawing.ColorTranslator]::FromHtml('#302732'),[Drawing.ColorTranslator]::FromHtml('#1b1922'))
        try { $g.FillRectangle($background,0,0,1920,1200) } finally { $background.Dispose() }
        Header $g $card.title $card.subtitle ('0'+($i+1)+' / 04') ($card.mode -ne 'english')
        switch ($card.mode) {
            'map' {
                FrameImage $g $inputImage ([Drawing.Rectangle]::new(8,112,1846,990)) 48 171 1824 970
            }
            'reputation' {
                FrameImage $g $inputImage ([Drawing.Rectangle]::new(1684,80,316,850)) 120 183 355 750
                Text $g '从生手，到「自己人」' 615 216 1130 86 55 $script:ink $true
                Text $g '按需求选择地点，已开放区域会随声望同步成长。' 620 312 1110 66 28 $script:muted
                Rule $g 620 408 1100 ([Drawing.ColorTranslator]::FromHtml('#655260'))
                Text $g '20 / 40 / 60' 620 447 1050 82 61 $script:gold $true
                Text $g '逐批开放地点 · 基础收益同步成长' 624 536 1090 56 32 $script:ink
                Text $g '80' 620 637 190 80 56 $script:teal $true
                Text $g '开放贵重品掉落' 850 650 850 60 34 $script:ink
                Text $g '100' 620 749 190 80 56 $script:teal $true
                Text $g '开放箱卡与次日同行服务' 850 762 850 60 34 $script:ink
                Text $g '游戏内奖励说明节选' 60 937 700 28 18 $script:muted
                FrameImage $g $inputImage ([Drawing.Rectangle]::new(96,985,1912,188)) 60 975 1800 177
            }
            'loot' {
                FrameImage $g $inputImage ([Drawing.Rectangle]::new(544,258,824,854)) 70 174 885 960
                Text $g '把每一项收益拆开看' 1050 246 790 90 48 $script:ink $true
                $items = @(
                    @('01','基础 + 贵重增益 + 箱卡增益','主掉落收益分项列出'),
                    @('02','今日事件增益','活动对收益的影响单独展示'),
                    @('03','大奖 / 小奖期望','奖励与主掉落分别计算'),
                    @('04','逐项物品概率','查看当前地点的实际掉落分布')
                )
                for ($n=0; $n -lt $items.Count; ++$n) {
                    $rowY = 392+$n*155
                    Text $g $items[$n][0] 1052 $rowY 80 50 26 $script:teal $true
                    Text $g $items[$n][1] 1150 ($rowY-5) 680 63 31 $script:gold $true
                    Text $g $items[$n][2] 1153 ($rowY+55) 670 46 24 $script:muted
                    Rule $g 1055 ($rowY+118) 760 ([Drawing.ColorTranslator]::FromHtml('#514754'))
                }
                Text $g '截图示例：入口空地，声望 10；概率与收益随设置、声望和事件变化。' 1055 1055 770 73 21 $script:muted
            }
            'english' {
                FrameImage $g $inputImage ([Drawing.Rectangle]::new(0,0,$inputImage.Width,$inputImage.Height)) 48 171 1824 974
            }
        }
        SaveCard $canvas $card.stem
    } finally { $g.Dispose(); $canvas.Dispose(); $inputImage.Dispose() }
}

# A contact sheet lets the author review the four exported cards together.
$contact = [Drawing.Bitmap]::new(1920,1200,[Drawing.Imaging.PixelFormat]::Format24bppRgb)
$contactGraphics = [Drawing.Graphics]::FromImage($contact)
try {
    $contactGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    for ($i=0; $i -lt $cards.Count; ++$i) {
        $cardImage = [Drawing.Image]::FromFile((Join-Path $pngRoot ($cards[$i].stem+'.png')))
        try { $contactGraphics.DrawImage($cardImage,[Drawing.Rectangle]::new(($i%2)*960,[int][Math]::Floor($i/2)*600,960,600)) }
        finally { $cardImage.Dispose() }
    }
    $contact.Save((Join-Path $shotRoot 'contact-sheet.jpg'),[Drawing.Imaging.ImageFormat]::Jpeg)
} finally { $contactGraphics.Dispose(); $contact.Dispose() }

$archive = Join-Path $shotRoot ('Workshop-Screenshots-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.zip')
Compress-Archive -LiteralPath @(Get-ChildItem -LiteralPath $jpgRoot -Filter '*.jpg' -File | ForEach-Object FullName) -DestinationPath $archive
Set-Content -LiteralPath (Join-Path $shotRoot 'latest-archive.txt') -Value $archive -Encoding utf8NoBOM
Write-Output "Prepared 4 gallery images at 1920x1200. Original screenshot pixels were cropped/resampled, never regenerated."
Write-Output $archive
Get-ChildItem -LiteralPath $jpgRoot -Filter '*.jpg' -File | ForEach-Object { Write-Output ($_.Name+' '+$_.Length+' bytes') }

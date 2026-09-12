param(
  [string]$HotFolder = "D:\hot",
  [int]$SmallCount = 1000,
  [int]$SmallKb = 100
)

New-Item -ItemType Directory -Force -Path $HotFolder | Out-Null

for ($i = 0; $i -lt $SmallCount; $i++) {
  $path = Join-Path $HotFolder ("small-{0:D4}.jpg" -f $i)
  $bytes = New-Object byte[] ($SmallKb * 1024)
  [System.IO.File]::WriteAllBytes($path, $bytes)
}

Write-Host "Generated $SmallCount files in $HotFolder"

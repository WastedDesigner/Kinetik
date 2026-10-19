# Draws icon.ico (and icon.png) in the Kinetik apps' "Graphite glass" style. The drawing lives in the shared
# Kinetik Shared\make-icons.py, so every app's icon matches; this runs it for KinetikStats only. Needs Python and Pillow.
$ErrorActionPreference = 'Stop'
python (Join-Path $PSScriptRoot '..\..\Kinetik Shared\make-icons.py') KinetikStats

param(
    [Parameter(Mandatory = $true)][string]$DataDir,
    [switch]$Demo
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

try {
    Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml, System.Web.Extensions, System.Core, System.Drawing
    $refs = @(
        [System.Windows.Window].Assembly.Location,
        [System.Windows.Media.Brush].Assembly.Location,
        [System.Windows.Threading.Dispatcher].Assembly.Location,
        [System.Xaml.XamlReader].Assembly.Location,
        [System.Web.Script.Serialization.JavaScriptSerializer].Assembly.Location,
        [System.Linq.Enumerable].Assembly.Location,
        [System.Drawing.Font].Assembly.Location
    ) | Select-Object -Unique

    $files = @(
        (Join-Path $here 'Overlay.cs'),
        (Join-Path $here 'OverlayController.cs')
    )
    Add-Type -Path $files -ReferencedAssemblies $refs
    [ANLyrics.OverlayApp]::Run($DataDir, [bool]$Demo)
}
catch {
    $message = $_.Exception.ToString()
    try {
        New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
        Set-Content -LiteralPath (Join-Path $DataDir 'launcher-error.txt') -Value $message -Encoding UTF8
    } catch { }
    try {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show($message, 'ANLyrics launcher error') | Out-Null
    } catch { }
    exit 1
}

# First-install launcher for Branch Merger.
# Shows a folder picker pre-filled with the recommended location, then hands off to
# Velopack's Setup.exe with --installto so the app installs where you chose. Updates
# afterward are applied in-app by Velopack, always in that same location.
#
# Uses the Windows built-in .NET Framework (WinForms) — no runtime to bundle, so this
# stays tiny. Run it via "Install Branch Merger.cmd" (handles the STA + policy flags).

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$default = Join-Path $env:LOCALAPPDATA 'BranchMerger'
$setup   = Join-Path $PSScriptRoot 'BranchMerger-win-Setup.exe'

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Install Branch Merger'
$form.FormBorderStyle = 'FixedDialog'
$form.StartPosition = 'CenterScreen'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.ClientSize = New-Object System.Drawing.Size(480, 176)

$lbl = New-Object System.Windows.Forms.Label
$lbl.Text = "Branch Merger will be installed in the folder below." + [Environment]::NewLine +
            "Keep the recommended location, or choose a different one:"
$lbl.SetBounds(16, 16, 448, 40)

$box = New-Object System.Windows.Forms.TextBox
$box.SetBounds(16, 64, 356, 24)
$box.Text = $default

$browse = New-Object System.Windows.Forms.Button
$browse.Text = 'Browse...'
$browse.SetBounds(380, 62, 84, 26)
$browse.Add_Click({
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = 'Select the install folder for Branch Merger'
    $d.UseDescriptionForTitle = $true
    $d.ShowNewFolderButton = $true
    $cur = $box.Text.Trim()
    if (Test-Path $cur) { $d.SelectedPath = $cur }
    elseif ($cur -and (Test-Path (Split-Path $cur))) { $d.SelectedPath = (Split-Path $cur) }
    if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $box.Text = Join-Path $d.SelectedPath 'BranchMerger'
    }
})

$install = New-Object System.Windows.Forms.Button
$install.Text = 'Install'
$install.SetBounds(292, 128, 88, 28)
$install.DialogResult = [System.Windows.Forms.DialogResult]::OK

$cancel = New-Object System.Windows.Forms.Button
$cancel.Text = 'Cancel'
$cancel.SetBounds(384, 128, 80, 28)
$cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel

$form.Controls.AddRange(@($lbl, $box, $browse, $install, $cancel))
$form.AcceptButton = $install
$form.CancelButton = $cancel

if ($form.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }

$dest = $box.Text.Trim()
if ([string]::IsNullOrWhiteSpace($dest)) { return }

if (-not (Test-Path $setup)) {
    [System.Windows.Forms.MessageBox]::Show(
        "Setup file not found:`r`n$setup`r`n`r`nMake sure BranchMerger-win-Setup.exe is in the same folder as this launcher.",
        'Branch Merger', 'OK', 'Error') | Out-Null
    return
}

Start-Process -FilePath $setup -ArgumentList @('--installto', $dest)

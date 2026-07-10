# Connect-PPEStorage.ps1
# Run as the ordinary logged-in Windows user, not as Administrator.

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
$Gateway    = "staff.ph.ed.ac.uk"
$RemotePath = "/storage/datastore-group/PPE"
$Drive      = "S:"
$SshfsExe   = "C:\Program Files\SSHFS-Win\bin\sshfs.exe"

# ---------------------------------------------------------------------------
# State
# ---------------------------------------------------------------------------
$script:SshfsProcess = $null
$script:StdoutTask   = $null
$script:StderrTask   = $null
$script:IsConnected  = $false
$script:IsClosing    = $false

function Test-DrivePresent {
    try {
        return [System.IO.Directory]::Exists("$Drive\")
    }
    catch {
        return $false
    }
}

function Test-DriveLetterInUse {
    try {
        return [Environment]::GetLogicalDrives() -contains "$Drive\"
    }
    catch {
        return $false
    }
}

function Get-TaskText {
    param($Task)

    if ($null -eq $Task) {
        return ""
    }

    try {
        return $Task.GetAwaiter().GetResult()
    }
    catch {
        return ""
    }
}

function Set-DisconnectedUi {
    param([string]$Message = "Not connected.")

    if ($form.IsDisposed) {
        return
    }

    $usernameBox.Enabled      = $true
    $passwordBox.Enabled      = $true
    $connectButton.Enabled    = $true
    $openButton.Enabled       = $false
    $disconnectButton.Enabled = $false
    $statusLabel.Text         = $Message
    $passwordBox.Clear()
}

function Set-ConnectedUi {
    param([string]$Username)

    if ($form.IsDisposed) {
        return
    }

    $usernameBox.Enabled      = $false
    $passwordBox.Enabled      = $false
    $connectButton.Enabled    = $false
    $openButton.Enabled       = $true
    $disconnectButton.Enabled = $true
    $statusLabel.Text         = "Connected as $Username on $Drive"
    $passwordBox.Clear()
}

function Stop-PPEMount {
    param([bool]$UpdateUi = $true)

    $process = $script:SshfsProcess

    $script:SshfsProcess = $null
    $script:IsConnected  = $false

    if ($null -ne $process) {
        try {
            $process.Refresh()

            if (-not $process.HasExited) {
                # Kill sshfs.exe and its child ssh.exe.
                $killInfo = New-Object System.Diagnostics.ProcessStartInfo
                $killInfo.FileName = "$env:SystemRoot\System32\taskkill.exe"
                $killInfo.Arguments = "/PID $($process.Id) /T /F"
                $killInfo.UseShellExecute = $false
                $killInfo.CreateNoWindow = $true
                $killInfo.RedirectStandardOutput = $true
                $killInfo.RedirectStandardError = $true

                $killer = [System.Diagnostics.Process]::Start($killInfo)

                if ($null -ne $killer) {
                    $null = $killer.StandardOutput.ReadToEnd()
                    $null = $killer.StandardError.ReadToEnd()
                    $null = $killer.WaitForExit(5000)
                    $killer.Dispose()
                }

                try {
                    $null = $process.WaitForExit(5000)
                }
                catch {
                    # Best effort.
                }
            }
        }
        catch {
            try {
                if (-not $process.HasExited) {
                    $process.Kill()
                    $null = $process.WaitForExit(3000)
                }
            }
            catch {
                # It may already have exited.
            }
        }
        finally {
            try {
                $process.Dispose()
            }
            catch {
                # Nothing further to clean up.
            }
        }
    }

    $script:StdoutTask = $null
    $script:StderrTask = $null

    # Give WinFsp a moment to remove the drive letter.
    $deadline = [DateTime]::UtcNow.AddSeconds(5)

    while ((Test-DrivePresent) -and ([DateTime]::UtcNow -lt $deadline)) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 100
    }

    if ($UpdateUi -and -not $script:IsClosing) {
        Set-DisconnectedUi -Message "Disconnected."
    }
}

function Show-ConnectionError {
    param(
        [int]$ExitCode,
        [string]$Stdout,
        [string]$Stderr
    )

    $message = (($Stderr + "`r`n" + $Stdout).Trim())

    if ([string]::IsNullOrWhiteSpace($message)) {
        $message = "SSHFS produced no diagnostic output."
    }

    if ($message.Length -gt 1800) {
        $message = $message.Substring($message.Length - 1800)
        $message = "[Earlier output omitted]`r`n`r`n$message"
    }

    [System.Windows.Forms.MessageBox]::Show(
        $form,
        "The storage could not be mounted.`r`n`r`n" +
        "SSHFS exit code: $ExitCode`r`n`r`n" +
        $message,
        "PPE Storage",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    ) | Out-Null
}

function Start-PPEMount {
    $username = $usernameBox.Text.Trim()
    $password = $passwordBox.Text

    if ([string]::IsNullOrWhiteSpace($username)) {
        [System.Windows.Forms.MessageBox]::Show(
            $form,
            "Enter your university username.",
            "PPE Storage",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        ) | Out-Null

        $usernameBox.Focus()
        return
    }

    if ($username -notmatch '^[A-Za-z0-9._-]+$') {
        [System.Windows.Forms.MessageBox]::Show(
            $form,
            "The username contains unsupported characters.",
            "PPE Storage",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        ) | Out-Null

        $usernameBox.Focus()
        return
    }

    if ([string]::IsNullOrEmpty($password)) {
        [System.Windows.Forms.MessageBox]::Show(
            $form,
            "Enter your university password.",
            "PPE Storage",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        ) | Out-Null

        $passwordBox.Focus()
        return
    }

    if (-not (Test-Path -LiteralPath $SshfsExe -PathType Leaf)) {
        [System.Windows.Forms.MessageBox]::Show(
            $form,
            "SSHFS-Win was not found at:`r`n$SshfsExe",
            "PPE Storage",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error
        ) | Out-Null
        return
    }

    if (Test-DriveLetterInUse) {
        [System.Windows.Forms.MessageBox]::Show(
            $form,
            "$Drive is already in use.",
            "PPE Storage",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        ) | Out-Null
        return
    }

    $usernameBox.Enabled   = $false
    $passwordBox.Enabled   = $false
    $connectButton.Enabled = $false
    $statusLabel.Text      = "Connecting..."
    $form.Refresh()

    $remote = "${username}@${Gateway}:${RemotePath}"

    # Password is never placed on the command line.
    #
    # StrictHostKeyChecking=accept-new:
    #   - automatically stores the host key on first use;
    #   - refuses a changed host key later;
    #   - uses the current user's normal ~/.ssh/known_hosts file.
    $arguments = @(
        $remote
        $Drive
        "-f"
        "-o", "password_stdin"
        "-o", "uid=-1,gid=-1"
        "-o", "ssh_command=/usr/bin/ssh.exe"
        "-o", "PreferredAuthentications=password"
        "-o", "PubkeyAuthentication=no"
        "-o", "NumberOfPasswordPrompts=1"
        "-o", "ConnectTimeout=10"
        "-o", "StrictHostKeyChecking=accept-new"
        "-o", "reconnect"
        "-o", "ServerAliveInterval=30"
        "-o", "ServerAliveCountMax=3"
    ) -join " "

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $SshfsExe
    $startInfo.Arguments = $arguments
    $startInfo.WorkingDirectory = Split-Path -Parent $SshfsExe
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.EnvironmentVariables["CYGFUSE"] = "WinFsp"

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo

    try {
        if (-not $process.Start()) {
            throw "sshfs.exe did not start."
        }

        $script:SshfsProcess = $process

        # Drain output immediately so neither pipe can block SSHFS.
        $script:StdoutTask = $process.StandardOutput.ReadToEndAsync()
        $script:StderrTask = $process.StandardError.ReadToEndAsync()

        # Send password via standard input only.
        $process.StandardInput.WriteLine($password)
        $process.StandardInput.Flush()
        $process.StandardInput.Close()

        $passwordBox.Clear()
        $password = $null

        $ready = $false
        $deadline = [DateTime]::UtcNow.AddSeconds(20)

        while ([DateTime]::UtcNow -lt $deadline) {
            if ($script:IsClosing) {
                return
            }

            $process.Refresh()

            if ($process.HasExited) {
                break
            }

            if (Test-DrivePresent) {
                $ready = $true
                break
            }

            [System.Windows.Forms.Application]::DoEvents()
            Start-Sleep -Milliseconds 200
        }

        if (-not $ready) {
            if (-not $process.HasExited) {
                try {
                    $process.Kill()
                    $null = $process.WaitForExit(3000)
                }
                catch {
                    # Best effort.
                }
            }

            $exitCode = 1

            try {
                $exitCode = $process.ExitCode
            }
            catch {
                # Keep fallback value.
            }

            $stdout = Get-TaskText $script:StdoutTask
            $stderr = Get-TaskText $script:StderrTask

            Stop-PPEMount -UpdateUi $false
            Set-DisconnectedUi -Message "Connection failed."
            Show-ConnectionError `
                -ExitCode $exitCode `
                -Stdout $stdout `
                -Stderr $stderr

            $passwordBox.Focus()
            return
        }

        $script:IsConnected = $true
        Set-ConnectedUi -Username $username
        Start-Process -FilePath "explorer.exe" -ArgumentList "$Drive\"
    }
    catch {
        $stdout = Get-TaskText $script:StdoutTask
        $stderr = Get-TaskText $script:StderrTask

        Stop-PPEMount -UpdateUi $false
        Set-DisconnectedUi -Message "Connection failed."

        $detail = $_.Exception.Message

        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            $detail += "`r`n`r`n$stderr"
        }

        [System.Windows.Forms.MessageBox]::Show(
            $form,
            "The storage could not be mounted.`r`n`r`n$detail",
            "PPE Storage",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error
        ) | Out-Null
    }
}

# ---------------------------------------------------------------------------
# User interface
# ---------------------------------------------------------------------------
$form = New-Object System.Windows.Forms.Form
$form.Text = "PPE Storage"
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $true
$form.ClientSize = New-Object System.Drawing.Size(430, 225)
$form.Font = New-Object System.Drawing.Font("Segoe UI", 9)

$usernameLabel = New-Object System.Windows.Forms.Label
$usernameLabel.Text = "University username"
$usernameLabel.Location = New-Object System.Drawing.Point(20, 20)
$usernameLabel.AutoSize = $true

$usernameBox = New-Object System.Windows.Forms.TextBox
$usernameBox.Location = New-Object System.Drawing.Point(20, 42)
$usernameBox.Size = New-Object System.Drawing.Size(390, 24)

$passwordLabel = New-Object System.Windows.Forms.Label
$passwordLabel.Text = "University password"
$passwordLabel.Location = New-Object System.Drawing.Point(20, 78)
$passwordLabel.AutoSize = $true

$passwordBox = New-Object System.Windows.Forms.TextBox
$passwordBox.Location = New-Object System.Drawing.Point(20, 100)
$passwordBox.Size = New-Object System.Drawing.Size(390, 24)
$passwordBox.UseSystemPasswordChar = $true

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Text = "Not connected."
$statusLabel.Location = New-Object System.Drawing.Point(20, 138)
$statusLabel.Size = New-Object System.Drawing.Size(390, 24)

$connectButton = New-Object System.Windows.Forms.Button
$connectButton.Text = "Connect"
$connectButton.Location = New-Object System.Drawing.Point(20, 174)
$connectButton.Size = New-Object System.Drawing.Size(120, 32)

$openButton = New-Object System.Windows.Forms.Button
$openButton.Text = "Open S:"
$openButton.Location = New-Object System.Drawing.Point(155, 174)
$openButton.Size = New-Object System.Drawing.Size(120, 32)
$openButton.Enabled = $false

$disconnectButton = New-Object System.Windows.Forms.Button
$disconnectButton.Text = "Disconnect"
$disconnectButton.Location = New-Object System.Drawing.Point(290, 174)
$disconnectButton.Size = New-Object System.Drawing.Size(120, 32)
$disconnectButton.Enabled = $false

$form.Controls.AddRange(@(
    $usernameLabel,
    $usernameBox,
    $passwordLabel,
    $passwordBox,
    $statusLabel,
    $connectButton,
    $openButton,
    $disconnectButton
))

$form.AcceptButton = $connectButton

$connectButton.Add_Click({
    Start-PPEMount
})

$openButton.Add_Click({
    if (Test-DrivePresent) {
        Start-Process -FilePath "explorer.exe" -ArgumentList "$Drive\"
    }
    else {
        Stop-PPEMount -UpdateUi $false
        Set-DisconnectedUi -Message "Connection was lost."
    }
})

$disconnectButton.Add_Click({
    Stop-PPEMount
})

$form.Add_Shown({
    $usernameBox.Focus()
})

$form.Add_FormClosing({
    param($sender, $eventArgs)

    if ($script:IsConnected) {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            $form,
            "Closing this window will disconnect $Drive.`r`n`r`n" +
            "Close files opened from the drive before continuing.",
            "Disconnect PPE Storage?",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning,
            [System.Windows.Forms.MessageBoxDefaultButton]::Button2
        )

        if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
            $eventArgs.Cancel = $true
            return
        }
    }

    $script:IsClosing = $true
    Stop-PPEMount -UpdateUi $false
})

try {
    [void]$form.ShowDialog()
}
finally {
    $script:IsClosing = $true
    Stop-PPEMount -UpdateUi $false

    if (-not $form.IsDisposed) {
        $form.Dispose()
    }
}

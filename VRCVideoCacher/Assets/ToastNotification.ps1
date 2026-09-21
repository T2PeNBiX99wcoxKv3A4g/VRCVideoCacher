param(
    [Parameter(Mandatory = $false)]
    [string]$AppId = "VRCVideoCacher",

    [Parameter(Mandatory = $false)]
    [string]$Title = "VRCVideoCacher",

    [Parameter(Mandatory = $false)]
    [string]$Message = "",

    [Parameter(Mandatory = $false)]
    [string]$IconUri = ""
)

try {
    $regPath = "HKCU:\SOFTWARE\Classes\AppUserModelId\$AppId"
    if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
    }
    Set-ItemProperty -Path $regPath -Name "DisplayName" -Value "VRCVideoCacher" -Force | Out-Null
    if (-not [string]::IsNullOrEmpty($IconUri) -and (Test-Path $IconUri)) {
        Set-ItemProperty -Path $regPath -Name "IconUri" -Value $IconUri -Force | Out-Null
    }
} catch {
}

try {
    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
    [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null

    $escapedTitle = if ([string]::IsNullOrEmpty($Title)) { "" } else { [System.Security.SecurityElement]::Escape($Title) }
    $escapedMessage = if ([string]::IsNullOrEmpty($Message)) { "" } else { [System.Security.SecurityElement]::Escape($Message) }

    $template = @"
<toast>
    <visual>
        <binding template="ToastGeneric">
            <text>$escapedTitle</text>
            <text>$escapedMessage</text>
        </binding>
    </visual>
</toast>
"@

    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
    $xml.LoadXml($template)
    $toast = New-Object Windows.UI.Notifications.ToastNotification $xml
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($AppId).Show($toast)
} catch {
}

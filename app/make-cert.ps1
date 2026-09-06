# Creates a self-signed code-signing certificate for sideloading the XRPL Vanity MSIX.
# Run once per machine that builds packages. Trust the exported .cer on every machine that installs them.
# The subject must match the Publisher in Platforms/Windows/Package.appxmanifest exactly.
param([string] $Subject = "CN=StaticBit Vanity Dev", [string] $Password = "xrpl-vanity-dev")

$cert = New-SelfSignedCertificate -Type Custom -Subject $Subject -KeyUsage DigitalSignature `
    -FriendlyName "XRPL Vanity sideload" -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
$secure = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "$PSScriptRoot\XrplVanityDev.pfx" -Password $secure | Out-Null
Export-Certificate -Cert $cert -FilePath "$PSScriptRoot\XrplVanityDev.cer" | Out-Null
Write-Host "Created $($cert.Thumbprint). Trust XrplVanityDev.cer under Local Machine \ Trusted People before installing the MSIX."

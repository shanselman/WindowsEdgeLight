# Code Signing with Azure Trusted Signing

This document explains how WindowsEdgeLight is signed using Azure Trusted Signing to prevent Microsoft Defender SmartScreen warnings.

## Overview

WindowsEdgeLight uses Azure Trusted Signing to digitally sign executables, which:
- Eliminates SmartScreen warnings for users
- Verifies the authenticity of the application
- Establishes trust with Windows security features

## Azure Setup

### Resources
- **Subscription ID**: `<your-subscription-id>`
- **Resource Group**: `<your-resource-group>`
- **Code Signing Account**: `<your-account-name>`
- **Certificate Profile**: `<your-certificate-profile-name>`
- **Endpoint**: `https://wus2.codesigning.azure.net/` (West US 2)
- **Region**: West US 2

### Service Principal for GitHub Actions
A service principal is configured with the "Trusted Signing Certificate Profile Signer" role on the Code Signing Account.

## Local Signing

### Prerequisites
1. Azure CLI installed and logged in with the correct scope:
   ```powershell
   az logout
   az login --use-device-code --scope "https://codesigning.azure.net/.default"
   ```

2. User account needs "Trusted Signing Certificate Profile Signer" role assigned on the Code Signing Account

3. Download `sign.exe` from [dotnet/sign releases](https://github.com/dotnet/sign/releases)

### Sign Executables Locally
```powershell
# Place executables in publish folder
.\sign.exe code trusted-signing `
  -b <path-to-publish-folder> `
  -tse "https://wus2.codesigning.azure.net" `
  -tscp <certificate-profile-name> `
  -tsa <code-signing-account-name> `
  *.exe `
  -v Trace
```

### Verify Signature
```powershell
Get-AuthenticodeSignature .\publish\WindowsEdgeLight-v1.0.0-win-x64.exe

# Should show:
# SignerCertificate: CN=Scott Hanselman, O=Scott Hanselman, ...
# Status: Valid
# StatusMessage: Signature verified
```

## GitHub Actions Signing

### GitHub Secrets Required
Repository secrets configured in Settings → Secrets and variables → Actions:
- `AZURE_CLIENT_ID` - Service principal client ID
- `AZURE_CLIENT_SECRET` - Service principal secret
- `AZURE_TENANT_ID` - Azure tenant ID
- `AZURE_SUBSCRIPTION_ID` - Azure subscription ID

### Workflow Integration
The `.github/workflows/build.yml` workflow automatically signs executables on release:

```yaml
- name: Azure Login
  uses: azure/login@v2
  with:
    creds: '{"clientId":"${{ secrets.AZURE_CLIENT_ID }}","clientSecret":"${{ secrets.AZURE_CLIENT_SECRET }}","subscriptionId":"${{ secrets.AZURE_SUBSCRIPTION_ID }}","tenantId":"${{ secrets.AZURE_TENANT_ID }}"}'

- name: Sign executables with Trusted Signing
  uses: azure/trusted-signing-action@v0
  with:
    azure-tenant-id: ${{ secrets.AZURE_TENANT_ID }}
    azure-client-id: ${{ secrets.AZURE_CLIENT_ID }}
    azure-client-secret: ${{ secrets.AZURE_CLIENT_SECRET }}
    endpoint: https://wus2.codesigning.azure.net/
    trusted-signing-account-name: <your-account-name>
    certificate-profile-name: <your-certificate-profile-name>
    files-folder: ${{ github.workspace }}\WindowsEdgeLight\bin\Release\net10.0-windows
    files-folder-filter: exe
    files-folder-recurse: true
    file-digest: SHA256
    timestamp-rfc3161: http://timestamp.acs.microsoft.com
    timestamp-digest: SHA256
```

### Triggering a Signed Release
```powershell
# Create and push a version tag
git tag v1.10.1
git push origin v1.10.1

# Or use the build script
.\build.ps1 -Version "1.10.1"
git tag v1.10.1
git push origin v1.10.1
```

## Troubleshooting

### Common Issues

#### 403 Forbidden Error
- **Cause**: Missing permissions or wrong endpoint
- **Solution**: 
  - Verify user/service principal has "Trusted Signing Certificate Profile Signer" role
  - Use correct regional endpoint (West US 2: `https://wus2.codesigning.azure.net/`)
  - Check account name matches exactly (case-sensitive)

#### Authentication Failed - "Please run 'az login'"
- **Cause**: Not logged in with correct scope
- **Solution**: 
  ```powershell
  az logout
  az login --use-device-code --scope "https://codesigning.azure.net/.default"
  ```

#### "User account does not exist in tenant 'Microsoft Services'"
- **Cause**: Azure CLI credential trying to use Visual Studio credential which fails
- **Solution**: Login with device code flow (see above)

#### GitHub Actions: 403 Error
- **Cause**: Service principal missing permissions or secrets misconfigured
- **Solution**: 
  - Verify all 4 GitHub secrets are set correctly
  - Ensure service principal has "Trusted Signing Certificate Profile Signer" role
  - Check subscription ID matches the resource location

### Certificate Validity
Azure Trusted Signing certificates are short-lived (typically 3 days). The timestamp ensures signatures remain valid after certificate expiration.

## Cost
Azure Trusted Signing pricing (as of 2025):
- **Public Trust Certificate Profile**: ~$9.99/month
- **Signing Operations**: First 5,000 operations/month included
- For occasional releases: ~$10-15/month

## Resources
- [Azure Trusted Signing Documentation](https://learn.microsoft.com/en-us/azure/trusted-signing/)
- [azure/trusted-signing-action](https://github.com/Azure/trusted-signing-action)
- [dotnet/sign Tool](https://github.com/dotnet/sign)
- [Issue #11 - SmartScreen Warning](https://github.com/shanselman/WindowsEdgeLight/issues/11)

## Notes
- Certificates expire in 3 days but timestamped signatures remain valid indefinitely
- Sign operations are automatic on tagged releases via GitHub Actions
- Local signing requires Azure CLI authentication with codesigning scope
- Both x64 and ARM64 executables are signed

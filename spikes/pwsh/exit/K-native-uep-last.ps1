$PSNativeCommandUseErrorActionPreference = $true
Write-Output x
sh -c 'exit 3'

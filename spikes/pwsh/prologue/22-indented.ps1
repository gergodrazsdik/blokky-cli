
    Write-Output a
try { throw 'x' } catch { "line=$($_.InvocationInfo.ScriptLineNumber)" }
"eap=$ErrorActionPreference"
throw 'end'

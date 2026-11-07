using namespace System.IO; [File]::Exists('/x') | Out-Null
try { throw 'x' } catch { "line=$($_.InvocationInfo.ScriptLineNumber)" }
"eap=$ErrorActionPreference"
throw 'end'

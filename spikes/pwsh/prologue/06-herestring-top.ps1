@'
# not a comment
using namespace X
'@ | Out-Null
try { throw 'x' } catch { "line=$($_.InvocationInfo.ScriptLineNumber)" }
"eap=$ErrorActionPreference"
throw 'end'

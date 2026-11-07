begin { 'b' }
process { 'p' }
end { try { throw 'x' } catch { "line=$($_.InvocationInfo.ScriptLineNumber)" }
"eap=$ErrorActionPreference"
throw 'end' }

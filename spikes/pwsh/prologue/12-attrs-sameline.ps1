[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '')]
[CmdletBinding()] <# x #> param()
try { throw 'x' } catch { "line=$($_.InvocationInfo.ScriptLineNumber)" }
"eap=$ErrorActionPreference"
throw 'end'

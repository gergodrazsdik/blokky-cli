# conf comment

using namespace System.IO
$text = [File]::Exists('/x')
try { throw 'x' } catch { "line=$($_.InvocationInfo.ScriptLineNumber)" }
"eap=$ErrorActionPreference"
throw 'end'

# conf comment

using namespace System.IO
$text = [File]::ReadAllText($PSCommandPath)
try { throw 'x' } catch { $line = $_.InvocationInfo.ScriptLineNumber }
[Console]::Out.Write($text + "line=$line")

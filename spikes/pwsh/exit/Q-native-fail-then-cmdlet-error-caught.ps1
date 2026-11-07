sh -c 'exit 3'
try { Get-Item ./missing } catch { }

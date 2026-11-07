# conf é
[Console]::Out.Write(([IO.File]::ReadAllBytes($PSCommandPath)[0..2]) -join ',')

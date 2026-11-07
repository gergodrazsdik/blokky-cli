#!/bin/bash
# shellcheck disable=SC2016  # $0 in the generated scripts is meant literally
# Spike 6: executable resolution and argv[0] (Linux).
set -u
here=$(cd "$(dirname "$0")" && pwd)
bk=$here/out/PathsDelete
T=$(mktemp -d "${TMPDIR:-/tmp}/spike6.XXXXXX")
mkdir -p "$T/bin1" "$T/bin2" "$T/cwd" "$T/w" "$T/other/sub" "$T/scripts"
cp "$here/c/showme" "$T/scripts/showme"
ln -s "$T/scripts/showme" "$T/bin1/hello"
ln -s "$T/scripts/showme" "$T/bin1/mytool"
printf '#!/bin/sh\necho "DECOY bin2 $0"\n' >"$T/bin2/mytool"
printf '#!/bin/sh\necho "DECOY cwd $0"\n' >"$T/cwd/mytool"
printf '#!/bin/sh\necho "script 0=$0"\n' >"$T/scripts/real.sh"
ln -s "$T/scripts/real.sh" "$T/bin1/myscript"
printf 'echo no shebang\n' >"$T/bin1/noshebang"
printf '#!/bin/sh\necho W\n' >"$T/w/tool"
printf '#!/bin/sh\necho OTHER\n' >"$T/other/tool"
ln -s "$T/other/sub" "$T/w/ld"
chmod +x "$T"/bin2/* "$T"/cwd/* "$T"/scripts/* "$T"/bin1/noshebang "$T"/w/tool "$T"/other/tool
export PATH="$T/bin1::.:$T/bin2:/usr/bin:/bin"
run() { echo "\$ $*"; "$@"; echo; }
cd "$T/cwd" || exit 1
echo "== resolution (cwd holds a decoy mytool; PATH has empty and '.' entries)"
run "$bk" which mytool
run "$bk" which myscript
echo "== posix_spawn: absolute program, argv[0] as written"
run "$bk" spawn mytool a b
run "$bk" spawn hello
run "$bk" spawn myscript
echo "== .NET Process with the absolute program"
run "$bk" proc hello
run "$bk" proc myscript
echo "== .NET Process with a bare name (its own search)"
run "$bk" procbare mytool
echo "== bash for comparison"
run bash -c 'mytool; hello x'
echo "== no further search: absolute path that doesn't exist (bin2 has mytool)"
run "$bk" startabs spawn "$T/bin1/gone" mytool
run "$bk" startabs process "$T/bin1/gone" mytool
echo "== shebang-less executable (ENOEXEC: no /bin/sh fallback?)"
run "$bk" spawn noshebang
run "$bk" proc noshebang
echo "== relative path with .. through a symlinked dir (SH-26)"
cd "$T/w" || exit 1
run "$bk" fullpath ld/../tool
run "$bk" spawn ld/../tool
run "$bk" spawn ./tool
run "$bk" proc ld/../tool
rm -rf "$T"

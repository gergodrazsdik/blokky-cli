# /// script
# requires-python = ">=3.12"
# ///
"""Minimal LSP client: drives yaml-language-server over stdio against the Blokky plan schema.

Usage: uv run lsp_probe.py SCHEMA YAML_VERSION OUT.json FILE... [--complete FILE:LINE:COL ...] [--hover FILE:LINE:COL ...]
"""

import json
import pathlib
import subprocess
import sys
import threading
import time

args = sys.argv[1:]
schema, version, out = pathlib.Path(args[0]).resolve(), args[1], args[2]
files, completes, hovers, mode = [], [], [], None
for a in args[3:]:
    if a in ("--complete", "--hover"):
        mode = a
    elif mode == "--complete":
        completes.append(a)
    elif mode == "--hover":
        hovers.append(a)
    else:
        files.append(pathlib.Path(a).resolve())

GLOB = json.loads(__import__("os").environ.get("GLOB", '"*.blokky.yaml"'))
settings = {
    "yaml": {
        "validate": True,
        "hover": True,
        "completion": True,
        "format": {"enable": False},
        "schemaStore": {"enable": False},
        "kubernetesCRDStore": {"enable": False},
        "yamlVersion": version,
        "schemas": {schema.as_uri(): GLOB},
    }
}

proc = subprocess.Popen(
    ["yaml-language-server", "--stdio"],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.DEVNULL,
)
lock = threading.Lock()
pending: dict[int, dict] = {}
diags: dict[str, list] = {}
log: list = []
next_id = 0


def send(msg: dict) -> None:
    body = json.dumps(msg).encode()
    with lock:
        proc.stdin.write(b"Content-Length: %d\r\n\r\n" % len(body) + body)
        proc.stdin.flush()


def request(method: str, params: dict, timeout: float = 20) -> dict:
    global next_id
    next_id += 1
    rid = next_id
    send({"jsonrpc": "2.0", "id": rid, "method": method, "params": params})
    end = time.time() + timeout
    while rid not in pending and time.time() < end:
        time.sleep(0.02)
    return pending.pop(rid, {"error": "timeout"})


def notify(method: str, params: dict) -> None:
    send({"jsonrpc": "2.0", "method": method, "params": params})


def reader() -> None:
    while True:
        headers = {}
        while True:
            line = proc.stdout.readline()
            if not line:
                return
            line = line.strip()
            if not line:
                break
            k, v = line.decode().split(":", 1)
            headers[k.lower()] = v.strip()
        msg = json.loads(proc.stdout.read(int(headers["content-length"])))
        if "method" in msg:
            log.append(msg["method"] + " " + json.dumps(msg.get("params"))[:300])
        if "id" in msg and "method" in msg:  # server -> client request
            result = None
            if msg["method"] == "workspace/configuration":
                result = [
                    settings.get(i.get("section", ""), None)
                    for i in msg["params"]["items"]
                ]
            send({"jsonrpc": "2.0", "id": msg["id"], "result": result})
        elif "id" in msg:
            pending[msg["id"]] = msg
        elif msg.get("method") in ("window/logMessage", "window/showMessage"):
            log.append(msg["params"].get("message"))
        elif msg.get("method") == "textDocument/publishDiagnostics":
            diags[msg["params"]["uri"]] = msg["params"]["diagnostics"]


threading.Thread(target=reader, daemon=True).start()
root = files[0].parent
request(
    "initialize",
    {
        "processId": None,
        "rootUri": root.as_uri(),
        "capabilities": {
            "workspace": {
                "configuration": True,
                "didChangeConfiguration": {"dynamicRegistration": True},
            },
            "textDocument": {
                "publishDiagnostics": {},
                "hover": {"contentFormat": ["markdown", "plaintext"]},
                "completion": {
                    "completionItem": {
                        "documentationFormat": ["markdown", "plaintext"],
                        "snippetSupport": True,
                    }
                },
            },
        },
    },
)
notify("initialized", {})
notify("workspace/didChangeConfiguration", {"settings": settings})
time.sleep(0.5)

result: dict = {
    "log": log,
    "yamlVersion": version,
    "diagnostics": {},
    "completion": {},
    "hover": {},
}
for f in files:
    uri = f.as_uri()
    notify(
        "textDocument/didOpen",
        {
            "textDocument": {
                "uri": uri,
                "languageId": "yaml",
                "version": 1,
                "text": f.read_text(),
            }
        },
    )
    end = time.time() + 10
    while uri not in diags and time.time() < end:
        time.sleep(0.02)
    time.sleep(2)  # let a schema-backed re-validation land
    result["diagnostics"][f.name] = [
        {
            "line": d["range"]["start"]["line"] + 1,
            "col": d["range"]["start"]["character"] + 1,
            "sev": d.get("severity"),
            "msg": d["message"],
        }
        for d in diags.get(uri, [])
    ]


def pos(spec: str) -> tuple[str, dict]:
    name, line, col = spec.rsplit(":", 2)
    p = pathlib.Path(name).resolve()
    return p, {
        "textDocument": {"uri": p.as_uri()},
        "position": {"line": int(line) - 1, "character": int(col) - 1},
    }


for spec in completes:
    p, params = pos(spec)
    r = request("textDocument/completion", params).get("result") or {}
    items = r.get("items", r) if isinstance(r, dict) else r
    result["completion"][spec] = [
        {
            "label": i["label"],
            "doc": (i.get("documentation") or {}).get("value", i.get("documentation"))
            if isinstance(i.get("documentation"), dict)
            else i.get("documentation"),
        }
        for i in items or []
    ]
for spec in hovers:
    p, params = pos(spec)
    r = request("textDocument/hover", params).get("result")
    result["hover"][spec] = r["contents"] if r else None

request("shutdown", {})
notify("exit", {})
pathlib.Path(out).write_text(json.dumps(result, indent=1))

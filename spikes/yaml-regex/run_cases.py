# /// script
# requires-python = ">=3.12"
# dependencies = ["pyyaml"]
# ///
"""Run the spike binary over conformance cases (spikes 3 and 9).

usage: python3 -I run_cases.py <spike-binary> <blokky-specs dir> <scratch dir>
"""

import base64
import pathlib
import subprocess
import sys

import yaml

BIN, SPECS, SCRATCH = sys.argv[1], pathlib.Path(sys.argv[2]), pathlib.Path(sys.argv[3])
CASES = SPECS / "conformance" / "cases"
YAML_LAYER = {
    "BK_PLAN_ENCODING",
    "BK_PLAN_CHARACTER",
    "BK_PLAN_TOO_LARGE",
    "BK_PLAN_YAML",
    "BK_PLAN_DUPLICATE_KEY",
}


def plan_bytes(case):
    if "plan_base64" in case:
        return base64.b64decode(case["plan_base64"])
    return case["plan"].encode("utf-8")


def run_plan(path_id, data):
    f = SCRATCH / (path_id.replace("/", "__") + ".plan")
    f.write_bytes(data)
    out = subprocess.run([BIN, "plan", str(f)], capture_output=True, text=True, check=False)
    errs, warns = [], []
    for line in out.stdout.splitlines():
        kind, code, ln, col, *rest = line.split(" ", 4)
        item = {"code": code, "line": int(ln), "column": int(col), "detail": rest[0] if rest else ""}
        (errs if kind == "E" else warns).append(item)
    return errs, warns


def detail_ok(exp_detail, got):
    for k, v in (exp_detail or {}).items():
        if isinstance(v, dict):  # matchers such as $absent: not checked here
            continue
        if f"{k}={v}" not in got["detail"].split(" "):
            return False
    return True


def matches(exp, got):
    return (
        exp["code"] == got["code"]
        and exp.get("line", got["line"]) == got["line"]
        and exp.get("column", got["column"]) == got["column"]
        and detail_ok(exp.get("detail"), got)
    )


def check_case(path):
    cid = str(path.relative_to(CASES).with_suffix(""))
    case = yaml.safe_load(path.read_text())
    expect = case["expect"]
    errs, warns = run_plan(cid, plan_bytes(case))
    if "plan_errors" in expect:
        exp = expect["plan_errors"]
        layer = "yaml" if all(
            e["code"] in YAML_LAYER or (e.get("detail") or {}).get("rule") == "SY-20" for e in exp
        ) else "schema"
        left = list(errs)
        missing = []
        for e in exp:
            hit = next((g for g in left if matches(e, g)), None)
            if hit is None:
                missing.append(e)
            else:
                left.remove(hit)
        ok = not missing and (not expect.get("exhaustive") or not left)
        why = f"missing {missing} extra {left if expect.get('exhaustive') else []} got {errs}"
    else:
        layer = "run"
        ok = not errs
        why = f"unexpected errors {errs}"
    return cid, layer, ok, ("" if ok else why), warns


def esc(v):
    return v.replace("\\", "\\\\").replace("\t", "\\t").replace("\n", "\\n").replace("\r", "\\r")


REGEX_CASES = [
    "syntax/grammars/regex-subset-valid",
    "conditions/operators/matches-anchors-whole-value",
    "conditions/operators/matches-dot-and-ascii-classes",
    "conditions/operators/matches-dot-matches-cr",
    "conditions/operators/matches-lazy-quantifiers",
    "conditions/operators/matches-unanchored-search",
    "conditions/operators/ignore-case-simple-uppercase",
    "conditions/operators/ignore-case-operators",
    "execution-model/kinds/assert-false-message-and-code",
    "reference/rules/input-default-search",
    "syntax/inputs/all-keys-valid",
    "syntax/inputs/default-fails-matches",
    "syntax/inputs/default-matches-unanchored",
]
CAPTURED = {"WITH_CR": "a\rb"}  # testkit print --out-hex 610d62


def regex_checks():
    """(pattern, ignore_case, value, expected, label) from the suite's run outcomes."""
    rows = []
    for cid in REGEX_CASES:
        case = yaml.safe_load((CASES / (cid + ".yaml")).read_text())
        plan = yaml.load(case["plan"], Loader=yaml.BaseLoader)
        exp = case["expect"]
        nodes = exp.get("nodes", {})
        vars_ = dict(plan.get("vars", {}), **CAPTURED)
        for i, node in enumerate(plan.get("do", [])):
            cond = node.get("when") or node.get("assert")
            if not isinstance(cond, dict) or "var" not in cond:
                continue
            op = "matches" if "matches" in cond else "not_matches" if "not_matches" in cond else None
            if op is None:
                continue
            status = nodes.get(f"/do/{i}", {}).get("status")
            if status is None:
                continue
            truth = status == "succeeded"
            want = truth if op == "matches" else not truth
            ci = "1" if cond.get("ignore_case") == "true" else "0"
            rows.append((cond[op], ci, vars_[cond["var"]], "1" if want else "0", f"{cid} /do/{i}"))
        supplied = dict(x.split("=", 1) for x in case.get("inputs", []))
        for name, decl in (plan.get("inputs") or {}).items():
            if not isinstance(decl, dict) or "matches" not in decl or "in" in decl:
                continue
            value = supplied.get(name, decl.get("default"))
            want = "0" if "plan_errors" in exp else "1"
            rows.append((decl["matches"], "0", value, want, f"{cid} inputs.{name}"))
    stderr = "conditions/subjects/output-stderr-matches-end"
    rows.append(("port \\d+$", "0", "listening on port 8080", "1", stderr + " (trimmed)"))
    rows.append(("port \\d+$", "0", "listening on port 8080\r\n", "0", stderr + " (untrimmed)"))
    return rows


def main():
    SCRATCH.mkdir(parents=True, exist_ok=True)
    groups = {
        "spike3": sorted((CASES / "syntax" / "text").glob("*.yaml"))
        + sorted((CASES / "syntax" / "yaml").glob("*.yaml")),
        "spike9": sorted((CASES / "syntax" / "grammars").glob("regex-*.yaml"))
        + sorted((CASES / "conditions" / "operators").glob("matches-*.yaml"))
        + sorted((CASES / "conditions" / "operators").glob("ignore-case-simple-uppercase.yaml")),
    }
    total_fail = 0
    for name, paths in groups.items():
        tally = {}
        for p in paths:
            cid, layer, ok, why, warns = check_case(p)
            t = tally.setdefault(layer, [0, 0])
            t[0 if ok else 1] += 1
            mark = "PASS" if ok else "FAIL"
            w = f"  warnings: {[(x['code'], x['line'], x['column']) for x in warns]}" if warns else ""
            print(f"{mark} [{layer}] {cid}{(' ' + why) if why else ''}{w}")
            total_fail += 0 if ok else 1
        print(f"== {name}: " + ", ".join(f"{k} {v[0]} pass / {v[1]} fail" for k, v in tally.items()))
    rows = regex_checks()
    lines = ["\t".join([esc(r[0]), r[1], esc(r[2]), r[3], r[4]]) for r in rows]
    tsv = SCRATCH / "suite-regex.tsv"
    tsv.write_text("\n".join(lines) + "\n")
    print(f"== spike9 suite regex evaluations: {len(rows)} extracted")
    out = subprocess.run([BIN, "regex-check", str(tsv)], capture_output=True, text=True, check=False)
    print(out.stdout, end="")
    total_fail += out.returncode != 0
    naive = subprocess.run([BIN, "regex-check", str(tsv), "--naive"], capture_output=True, text=True, check=False)
    print("-- for comparison, patterns passed to .NET unchanged (NonBacktracking, IgnoreCase):")
    print(naive.stdout, end="")
    own = pathlib.Path(__file__).with_name("extra-regex.tsv")
    out = subprocess.run([BIN, "regex-check", str(own)], capture_output=True, text=True, check=False)
    print(f"== spike9 extra edge checks ({own.name}):")
    print(out.stdout, end="")
    total_fail += out.returncode != 0
    return 1 if total_fail else 0


sys.exit(main())

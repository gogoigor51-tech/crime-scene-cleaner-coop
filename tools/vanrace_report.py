"""Checks the vanrace suite (tools/run-pair.ps1 -Test vanrace): the van's sequence for one body run on both machines
shows the van prop once on each and counts the body once."""
import re
import sys


def after(log):
    m = re.findall(r"VANRACE after: collected (\w+) props shown (\d+) skipped (\d+) collected by the other (\d+) body counted (\w+)", log)
    if not m:
        return None
    col, shown, skipped, other, counted = m[-1]
    return {"collected": col == "True", "shown": int(shown), "skipped": int(skipped), "other": int(other), "counted": counted == "True"}


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "VANRACE dump done" not in h or "VANRACE dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    ha, ca = after(h), after(c)
    print(f"      host   {ha}\n      client {ca}")
    checks = [
        ("VANRACE host started" in h and "VANRACE client started" in c, "the van sequence started on both, the client later"),
        (ha and ca and ha["collected"] and ca["collected"], "the body is collected on both"),
        (ha and ca and ha["shown"] == 1 and ca["shown"] == 1, f"the van prop shown once on each (host {ha and ha['shown']}, client {ca and ca['shown']})"),
        (ha and ca and ha["counted"] and ca["counted"], "the body counted on both"),
    ]
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(bool(x) for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))

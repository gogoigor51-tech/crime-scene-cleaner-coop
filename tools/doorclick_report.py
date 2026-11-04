"""Checks the doorclick suite (tools/run-pair.ps1 -Test doorclick -Mission 11): doors clicked the way a player does end
the same on both machines, both the open/closed flag and where the door leaves are."""
import re
import sys


def state(log, label):
    m = re.findall(r"DOORCLICK " + label + r":(.*)", log)
    if not m:
        return None
    out = {}
    for i, flag, leaves in re.findall(r" (\d+):([OC])((?:\[[^\]]*\])*)", m[-1]):
        out[int(i)] = (flag, [tuple(float(x) for x in re.split(r"[/,]", l)) for l in re.findall(r"\[([^\]]*)\]", leaves)])
    return out


def same(a, b):
    if a is None or b is None or a.keys() != b.keys():
        return False
    for i in a:
        if a[i][0] != b[i][0] or len(a[i][1]) != len(b[i][1]):
            return False
        for la, lb in zip(a[i][1], b[i][1]):
            dy = abs((la[0] - lb[0] + 180) % 360 - 180)
            if dy > 5 or abs(la[1] - lb[1]) > 0.05 or abs(la[2] - lb[2]) > 0.05:
                return False
    return True


def changed(a, b):
    return a is not None and b is not None and any(a[i][0] != b[i][0] for i in a if i in b)


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "DOORCLICK dump done" not in h or "DOORCLICK dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    hb, ha, hc = (state(h, l) for l in ("before", "after host", "after client"))
    cb, ca, cc = (state(c, l) for l in ("before", "after host", "after client"))
    for who, s in (("host", (hb, ha, hc)), ("client", (cb, ca, cc))):
        print(f"      {who}: " + " | ".join(str(x) for x in s))
    checks = [
        (hb and len(hb) == 3 and same(hb, cb), "three doors found, the same on both before"),
        (changed(hb, ha), "the host's clicks changed its doors"),
        (same(ha, ca), "after the host's clicks: same flags and door leaves on the client"),
        (changed(ca, cc), "the client's clicks changed its doors"),
        (same(hc, cc), "after the client's clicks: same flags and door leaves on the host"),
    ]
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(bool(x) for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))

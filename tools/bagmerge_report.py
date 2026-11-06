"""Checks the bagmerge suite (tools/run-pair.ps1 -Test bagmerge): two players' trash bags merge once: one bag on both,
of the merged size, at the same spot, and never more bags than were made."""
import math
import re
import sys


def bags(s):
    out = []
    for part in s.split(";"):
        if "~" in part:
            p, size, who = part.split("~")
            out.append(([float(x) for x in p.split("/")], int(size), who))
    return out


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "BAGMERGE dump done" not in h or "BAGMERGE dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    hm = re.findall(r"BAGMERGE final max (\d+) bags (\S*)", h)
    cm = re.findall(r"BAGMERGE final max (\d+) bags (\S*)", c)
    perks = re.findall(r"perk 53 unlocked (\w+)", h + c)
    checks = [(perks == ["True", "True"], f"the bag perk is unlocked on both ({perks})")]
    hb, cb = (bags(hm[-1][1]) if hm else []), (bags(cm[-1][1]) if cm else [])
    checks.append((len(hb) == 1 and len(cb) == 1, f"one bag left on both (host {len(hb)}, client {len(cb)})"))
    checks.append((hb and cb and hb[0][1] == cb[0][1] and hb[0][1] >= 2, f"of the merged size (host {hb and hb[0][1]}, client {cb and cb[0][1]})"))
    checks.append((hb and cb and (hb[0][2] == "mine") != (cb[0][2] == "mine"), f"simulated by one machine (host {hb and hb[0][2]}, client {cb and cb[0][2]})"))
    d = math.dist(hb[0][0], cb[0][0]) if hb and cb else 99
    checks.append((d < 0.1, f"at the same spot ({d:.2f} m apart)"))
    mx = (int(hm[-1][0]) if hm else 99, int(cm[-1][0]) if cm else 99)
    checks.append((max(mx) <= 2, f"never more than the two bags made, no duplicates (most seen: host {mx[0]}, client {mx[1]})"))
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(x for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))

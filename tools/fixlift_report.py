"""Checks the fixlift suite (tools/run-pair.ps1 -Test fixlift): a piece of furniture both players stand up at the same
moment ends standing in place on both, at the same spot."""
import math
import re
import sys


def st(log, label):
    m = re.findall(r"FIXLIFT " + label + r" correct (\w+) pos (\S+) off ([\d.]+) angle ([\d.]+)", log)
    if not m:
        return None
    c, p, off, ang = m[-1]
    return c == "True", [float(x) for x in p.split("/")], float(off), float(ang)


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "FIXLIFT dump done" not in h or "FIXLIFT dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    hp = re.findall(r"FIXLIFT piece (.+?) of \d", h)
    cp = re.findall(r"FIXLIFT piece (.+?) of \d", c)
    hf, cf = st(h, "final"), st(c, "final")
    checks = [(hp and cp and hp[-1] == cp[-1] and hp[-1] != "NONE", f"the same knocked-over piece on both ({hp and hp[-1]})")]
    checks.append(("host finished the lift" in h and "the host's fix arrived mid-lift" in c, "the host finished first; its fix reached the client mid-lift"))
    for name, f in (("host", hf), ("client", cf)):
        checks.append((f and f[0] and f[2] < 0.02 and f[3] < 2, f"{name}: it stands in place ({f and (f[0], f[2], f[3])}: correct, off m, angle)"))
    d = math.dist(hf[1], cf[1]) if hf and cf else 99
    checks.append((d < 0.02, f"same spot on both ({d:.3f} m apart)"))
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(x for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))

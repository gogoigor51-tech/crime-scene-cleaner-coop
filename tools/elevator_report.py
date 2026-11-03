"""Checks the elevator suite (tools/run-pair.ps1 -Test elevator): a ride started by one player moves the other player
only if they stand in the car."""
import re
import sys


def y(log, label):
    m = re.findall(r"ELEVATOR " + label + r" y (-?[\d.]+) at \S+ in lower (\w+) in upper (\w+)", log)
    return (float(m[-1][0]), m[-1][1] == "True", m[-1][2] == "True") if m else None


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "ELEVATOR dump done" not in h or "ELEVATOR dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    hb1, ha1, hb2, ha2 = (y(h, l) for l in ("before ride 1", "after ride 1", "before ride 2", "after ride 2"))
    cb1, ca1, cb2, ca2 = (y(c, l) for l in ("before ride 1", "after ride 1", "before ride 2", "after ride 2"))
    checks = [("ELEVATOR buttons up True down True" in h and "ELEVATOR buttons up True down True" in c, "both elevator buttons found")]
    checks.append((cb1 and cb1[1] and hb1 and not hb1[1], f"ride 1: the client in the lower car, the host outside (client {cb1}, host {hb1})"))
    checks.append((cb1 and ca1 and ca1[0] - cb1[0] > 3, f"ride 1: the client went up ({cb1 and cb1[0]} -> {ca1 and ca1[0]})"))
    checks.append((hb1 and ha1 and abs(ha1[0] - hb1[0]) < 0.5, f"ride 1: the host, outside, did not move ({hb1 and hb1[0]} -> {ha1 and ha1[0]})"))
    checks.append((hb2 and hb2[2] and cb2 and cb2[2], f"ride 2: both in the upper car (host {hb2}, client {cb2})"))
    checks.append((hb2 and ha2 and hb2[0] - ha2[0] > 3, f"ride 2: the host went down ({hb2 and hb2[0]} -> {ha2 and ha2[0]})"))
    checks.append((cb2 and ca2 and cb2[0] - ca2[0] > 3, f"ride 2: the client, in the car, rode along ({cb2 and cb2[0]} -> {ca2 and ca2[0]})"))
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(x for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))

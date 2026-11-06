"""Checks the placed suite (tools/run-pair.ps1 -Test placed): a piece the client put on its spot stays fixed, kinematic
and on the spot on both machines, also after the host pushes it."""
import re
import sys

NUM = r"(-?\d+[.,]\d+)"


def state(log, label):
    m = re.findall(r"PLACED " + label + r": fixed (\w+) correct (\w+) kinematic (\w+) at \S+ off " + NUM + r" owner (\d+)", log)
    if not m:
        return None
    f, c, k, off, owner = m[-1]
    return {"fixed": f == "True", "correct": c == "True", "kinematic": k == "True", "off": float(off.replace(",", ".")), "owner": int(owner)}


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "PLACED dump done" not in h or "PLACED dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    hp, hr, ha = (state(h, l) for l in ("placed", "rested", "after push"))
    cp, cr, ca = (state(c, l) for l in ("placed", "rested", "after push"))
    print(f"      host   {hp} | {hr} | {ha}\n      client {cp} | {cr} | {ca}")
    ok_role = re.search(r"PLACED roles hologram (\d+) piece (\d+) item (\d+)", c)
    checks = [
        (ok_role and "PLACED client put" in c and "fixed True" in c.split("PLACED client put", 1)[1][:200], "the client put a piece on its spot"),
        (cp and hp and cp["fixed"] and hp["fixed"] and cp["correct"] and hp["correct"], f"placed on both (host {hp}, client {cp})"),
        (hr and hr["owner"] == 0, f"back with the host once it rested ({hr and hr['owner']})"),
        (hr and cr and hr["kinematic"] and cr["kinematic"], f"kinematic on both once back with the host (host {hr and hr['kinematic']}, client {cr and cr['kinematic']})"),
        (ha and ca and ha["off"] < 0.02 and ca["off"] < 0.02, f"still on its spot after the host's push (host {ha and ha['off']} m, client {ca and ca['off']} m)"),
        (ha and ca and ha["fixed"] and ca["fixed"], "still counted as placed on both"),
    ]
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(bool(x) for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))

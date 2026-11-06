"""Checks the washer suite (tools/run-pair.ps1 -Test washer -Mission 11): a click on another player's power washer
station stops before it hands out their lance; a click on your own goes through."""
import re
import sys


def state(log, label):
    m = re.findall(r"WASHER " + label + r": selected washer (\d+) own station (.*?) other's station (.*?) refused (\d+) own (\d+) others' let through (\d+)", log)
    if not m:
        return None
    n, own, other, ref, mine, others = m[-1]
    return {"lances": int(n), "own": own, "other": other, "refused": int(ref), "mine": int(mine), "others": int(others)}


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "WASHER dump done" not in h or "WASHER dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    cb, co, cw = (state(c, l) for l in ("before", "after other's", "after own"))
    print(f"      client {cb} | {co} | {cw}")
    checks = [
        ("WASHER host set down its station: True" in h, "the host set down its power washer"),
        (cb and cb["other"] != "none", f"the client sees the host's station ({cb and cb['other']})"),
        ("WASHER client clicked the host's station: True" in c, "the client clicked it"),
        (cb and co and co["refused"] > cb["refused"] and co["others"] == 0, f"the click on the host's washer stopped before EquipLance (refused {cb and cb['refused']} -> {co and co['refused']}, let through {co and co['others']})"),
        (co and co["lances"] != 2, f"the client has nothing of the host's washer selected ({co and co['lances']}; 2 = the host's)"),
        ("WASHER client set down its own station: True" in c, "the client set down its own washer"),
        (co and cw and cw["mine"] > co["mine"] and cw["refused"] == co["refused"], f"a click on its own washer goes through (own {co and co['mine']} -> {cw and cw['mine']}, refused {co and co['refused']} -> {cw and cw['refused']})"),
    ]
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(bool(x) for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))

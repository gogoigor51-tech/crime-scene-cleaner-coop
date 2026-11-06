"""Checks the stackreward suite (tools/run-pair.ps1 -Test stackreward -Mission 11): the Super Stacker reward the host
earns (the passage behind the shelf) ends the same on the client."""
import re
import sys


def snap(log, label):
    m = re.findall(r"STACKREWARD " + label + r":(.*)", log)
    return m[-1].strip() if m else None


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "STACKREWARD dump done" not in h or "STACKREWARD dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    hb, ha, cb, ca = snap(h, "before"), snap(h, "after"), snap(c, "before"), snap(c, "after")
    print(f"      host   before {hb}\n      host   after  {ha}\n      client before {cb}\n      client after  {ca}")
    checks = [
        ("STACKREWARD reach True" in h and "game True" in h, "the reward listener and the minigame found"),
        (hb and hb == cb and hb != "-", "the reward's objects the same on both before"),
        ("STACKREWARD host leaves the game with" in h and "listeners True" in h, "the host left the game with a winning score"),
        (ha and hb and ha != hb, "the reward changed the host's world (the passage)"),
        (ca and ca == ha, "and the client's the same way"),
    ]
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(bool(x) for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))

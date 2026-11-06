"""Checks the arcade suite (tools/run-pair.ps1 -Test arcade): the Super Stacker minigame opens only for the player who
uses the machine, and both players stay in the same job and in sync while it is played and after."""
import re
import sys


def state(log, label):
    m = re.findall(r"ARCADE " + label + r": key (\w+) together (\w+) player active (\w+) game open (\w+) doors (\S*)", log)
    if not m:
        return None
    k, tog, act, op, doors = m[-1]
    return {"key": k, "together": tog == "True", "active": act == "True", "open": op == "True", "doors": doors}


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "ARCADE dump done" not in h or "ARCADE dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    ho, hp, ha = (state(h, l) for l in ("open", "playing", "after"))
    co, cp, ca = (state(c, l) for l in ("open", "playing", "after"))
    hb, cb = state(h, "before"), state(c, "before")
    checks = [
        ("ARCADE machine True button True" in h and "ARCADE machine True button True" in c, "the machine and its button found on both"),
        (hb and cb and hb["doors"] == cb["doors"], f"same doors before (host {hb and hb['doors']}, client {cb and cb['doors']})"),
        (ho and ho["open"] and not ho["active"], f"the minigame is open on the host, its player switched off by the game ({ho})"),
        (co and not co["open"] and co["active"], f"the minigame did not open on the client ({co})"),
        (ho and co and ho["together"] and co["together"] and ho["key"] != "00000000", f"still in the same job while the host plays (host {ho and ho['key']} {ho and ho['together']}, client {co and co['key']} {co and co['together']})"),
        (hp and cp and hp["doors"] == cp["doors"] and cp["doors"] != cb["doors"], f"a door the client clicked during the game reached the host (host {hp and hp['doors']}, client {cp and cp['doors']})"),
        (ha and not ha["open"] and ha["active"], f"the host is back in the job after closing it ({ha})"),
        (ha and ca and ha["together"] and ca["together"] and ha["key"] == ca["key"] != "00000000", f"together after (host {ha and ha['key']}, client {ca and ca['key']})"),
        (ha and ca and ha["doors"] == ca["doors"] and ha["doors"] != hp["doors"], f"a door the host clicked after the game reached the client (host {ha and ha['doors']}, client {ca and ca['doors']})"),
    ]
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(bool(x) for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))

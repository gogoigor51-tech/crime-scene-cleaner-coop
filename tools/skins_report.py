"""Checks the skins suite (tools/run-pair.ps1 -Test skins): each player sees the other in the skin they chose, and the
mod's figure again once the client chose none, and the client's blue eyes (the colour ring) on the host's screen."""
import re
import sys


def state(log, label, player):
    m = re.findall(r"SKINS " + label + r": player " + str(player) + r" figure (\w+) skin '([^']*)' own mesh (\w+) worn '([^']*)'; mine '([^']*)'(?: eyes '([^']*)')?", log)
    if not m:
        return None
    fig, skin, own, worn, mine, eyes = m[-1]
    return {"figure": fig == "True", "skin": skin, "own": own == "True", "worn": worn, "mine": mine, "eyes": eyes}


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "SKINS dump done" not in h or "SKINS dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    hk = re.search(r"SKINS host chooses '([^']*)' \(([^)]*)\)", h)
    ck = re.search(r"SKINS client chooses '([^']*)' \(([^)]*)\)", c)
    hc, cc = state(h, "chosen", 1), state(c, "chosen", 0)
    hx = state(h, "cleared", 1)
    print(f"      host sees client: {hc} -> {hx}\n      client sees host: {cc}")
    checks = [
        (hk and ck and hk.group(2) and ck.group(2), f"both chose a person (host {hk and hk.group(1)}, client {ck and ck.group(1)})"),
        (hc and ck and hc["skin"] == ck.group(2) and not hc["own"], "the host sees the client as the client's choice, the mod's mesh hidden"),
        (cc and hk and cc["skin"] == hk.group(2) and not cc["own"], "the client sees the host as the host's choice"),
        (hx and hx["skin"] == "" and hx["own"], "after the client chose none, the host sees the mod's figure again"),
        (hc and hc["eyes"] == "#2E6FD0" and cc and cc["eyes"] == "#5C5530",
         f"each sees the other's eye colour (host sees {hc and hc['eyes']}, wanted #2E6FD0; client sees {cc and cc['eyes']}, wanted #5C5530)"),
    ]
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(bool(x) for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))

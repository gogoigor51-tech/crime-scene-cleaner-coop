"""Checks the flashlight suite (tools/run-pair.ps1 -Test flashlight): another player's personal flashlight is seen on
their figure while it is on, pointing where they look, and not once it is off."""
import math
import re
import sys

NUM = r"(-?\d+[.,]\d+)"


def num(s):
    return float(s.replace(",", "."))


def host(log, label):
    m = re.findall(r"FLASH host " + label + r": figure (\w+) light on (\w+) dir " + NUM + "/" + NUM + "/" + NUM + r" type (\S+) range (\d+) cookie (\w+)", log)
    if not m:
        return None
    fig, on, x, y, z, typ, rng, cookie = m[-1]
    return {"on": on == "True", "dir": (num(x), num(y), num(z)), "type": typ, "range": int(rng), "cookie": cookie == "True"}


def client(log, label):
    m = re.findall(r"FLASH client " + label + r": flashlight (\w+) on (\w+) dir " + NUM + "/" + NUM + "/" + NUM, log)
    if not m:
        return None
    has, on, x, y, z = m[-1]
    return {"has": has == "True", "on": on == "True", "dir": (num(x), num(y), num(z))}


def angle(a, b):
    d = sum(p * q for p, q in zip(a, b))
    n = math.sqrt(sum(p * p for p in a)) * math.sqrt(sum(q * q for q in b))
    return math.degrees(math.acos(max(-1, min(1, d / n)))) if n > 0 else 180


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "FLASH dump done" not in h or "FLASH dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    hb, ho, ht, hf = (host(h, l) for l in ("before", "on", "turned", "off"))
    cb, co, ct, cf = (client(c, l) for l in ("before", "on", "turned", "off"))
    checks = [
        (cb and cb["has"] and not cb["on"], f"the client has a flashlight, off at first ({cb})"),
        (hb and not hb["on"], f"no light on the client's figure before ({hb})"),
        (co and co["on"], f"the client switched it on ({co})"),
        (ho and ho["on"] and ho["type"] == "Spot" and ho["cookie"], f"the host sees the client's flashlight: a spot with the game's cookie ({ho})"),
        (ho and co and angle(ho["dir"], co["dir"]) < 20, f"it points where the client looks ({ho and co and round(angle(ho['dir'], co['dir']))} degrees off)"),
        (co and ct and angle(co["dir"], ct["dir"]) > 10 and ct["dir"][1] < -0.2, f"the client's view moved, looking down (the game's LookAt sets the pitch here) ({co and co['dir']} -> {ct and ct['dir']})"),
        (ht and ct and ht["on"] and angle(ht["dir"], ct["dir"]) < 20, f"and the light follows ({ht and ct and round(angle(ht['dir'], ct['dir']))} degrees off)"),
        (cf and not cf["on"] and hf and not hf["on"], f"switched off: gone on the host too (client {cf}, host {hf})"),
    ]
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(bool(x) for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))

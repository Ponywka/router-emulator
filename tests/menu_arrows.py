#!/usr/bin/env python3
"""Send arrow keys to the U-Boot bootmenu; report whether the menu stays."""
import pexpect, sys, time, re, os
exe = sys.argv[1]; nand = sys.argv[2]
cmd = ["wine", exe] if exe.endswith(".exe") else [exe]
p = pexpect.spawn(cmd[0], cmd[1:] + ["-M", f"cudy-wr3000p,nand-dir={nand}", "-nographic"],
                  encoding="latin1", timeout=200)
def clean(s):
    s = re.sub(r"\x1b\[\?25[lh]", "", s)
    s = re.sub(r"\x1b\[K\x1b\[(\d*)C", lambda m: " " * int(m.group(1) or 1), s)
    return re.sub(r"\x1b\[[0-9;?]*[a-zA-Z]", "", s)
buf = ""
end = time.time() + 180
while "stop autoboot" not in clean(buf) and time.time() < end:
    try: buf += p.read_nonblocking(65536, timeout=1)
    except pexpect.TIMEOUT: pass
p.send(" "); time.sleep(2)
for _ in range(3):
    p.send("\x1b[B"); time.sleep(1.5)
out = ""
try:
    while True: out += p.read_nonblocking(65536, timeout=2)
except Exception: pass
c = clean(out)
print("prompt MT7981> reached (menu left):", "MT7981>" in c)
print("menu redrawn:", "Run default boot command" in c or "Boot system via TFTP" in c)
p.terminate(force=True)

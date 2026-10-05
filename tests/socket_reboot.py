#!/usr/bin/env python3
"""Boot via socket serial (like the Windows launcher), run reboot, see BL2 again."""
import socket, subprocess, sys, time, re
import os
CMD = os.environ.get("RCMD", "reboot").encode()
cmd = sys.argv[1:]
port = 45123
args = ["-M", "cudy-wr3000p,nand-dir=work/rbtest", "-display", "none",
        "-chardev", f"socket,id=con,mux=on,host=127.0.0.1,port={port},server=on,wait=on",
        "-serial", "chardev:con", "-mon", "chardev=con"]
q = subprocess.Popen(cmd + args, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
for _ in range(100):
    try: s = socket.create_connection(("127.0.0.1", port)); break
    except OSError: time.sleep(0.2)
s.settimeout(1)
buf = b""
def wait(pat, t):
    global buf
    end = time.time() + t
    while time.time() < end:
        try: buf += s.recv(65536)
        except socket.timeout: pass
        if re.search(pat, buf): return True
    return False
print("boot:", wait(rb"Please press Enter", 240)); time.sleep(30)
s.sendall(b"\r"); print("shell:", wait(rb"root@OpenWrt", 30))
mark = len(buf); s.sendall(CMD + b"\r")
ok = False; end = time.time() + 120
while time.time() < end:
    try: buf += s.recv(65536)
    except socket.timeout: pass
    if b"BL2: v2" in buf[mark:]: ok = True; break
print("BL2 after reboot:", ok)
print(repr(buf[mark:][-600:]))
q.kill()

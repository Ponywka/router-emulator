#!/usr/bin/env python3
"""Fast emulator check: start QEMU with the console on a socket, wait for
console strings in order, optionally send input, always clean up.

  quick.py [--win] [--limit SEC] [-M machine,opts] [--qemu "ARGS"] STEP...

STEP is "PATTERN" (regex to wait for, 120 s), "PATTERN@SEC" (own timeout),
">TEXT" (send TEXT + CR) or "!SEC" (sleep).  Exit code 0 = all matched.
"""
import argparse, os, re, shlex, socket, subprocess, sys, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ap = argparse.ArgumentParser()
ap.add_argument("--win", action="store_true", help="Windows build under Wine")
ap.add_argument("--limit", type=int, default=300, help="hard limit, seconds")
ap.add_argument("-M", default="cudy-wr3000p,nand-dir=" + ROOT + "/nand")
ap.add_argument("--log", default=ROOT + "/work/quick.log")
ap.add_argument("--qemu", default="", help="extra QEMU args (one string)")
ap.add_argument("steps", nargs="*")
a = ap.parse_args()

port = 46000 + os.getpid() % 1000
if a.win:
    cmd = ["wine", os.environ.get("QEXE", ROOT + "/work/winpkg/WR3000X/qemu/qemu-system-aarch64.exe")]
else:
    cmd = [ROOT + "/src/qemu/build/qemu-system-aarch64"]
cmd += ["-M", a.M, "-display", "none",
        "-chardev", f"socket,id=con,mux=on,host=127.0.0.1,port={port},server=on,wait=on",
        "-serial", "chardev:con", "-mon", "chardev=con"] + shlex.split(a.qemu)
err = open(a.log + ".err", "w")
q = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=err)
t0 = time.time()
deadline = t0 + a.limit
log = open(a.log, "wb")
rc = 1
try:
    s = None
    while time.time() < deadline and q.poll() is None:
        try:
            s = socket.create_connection(("127.0.0.1", port), timeout=1)
            break
        except OSError:
            time.sleep(0.2)
    if not s:
        raise SystemExit("QEMU did not start (see %s.err)" % a.log)
    s.settimeout(0.5)
    buf = b""
    pos = 0

    def pump():
        global buf
        try:
            d = s.recv(65536)
            if not d:
                return False
            buf += d
            log.write(d)
            log.flush()
        except socket.timeout:
            pass
        return True

    for st in a.steps:
        if st.startswith(">"):
            s.sendall(st[1:].encode() + b"\r")
            continue
        if st.startswith("!"):
            end = time.time() + float(st[1:])
            while time.time() < end and pump():
                pass
            continue
        pat, _, to = st.rpartition("@") if re.search(r"@\d+$", st) else (st, "", "120")
        end = min(time.time() + float(to), deadline)
        rx = re.compile(pat.encode())
        m = None
        while time.time() < end and q.poll() is None:
            m = rx.search(buf, pos)
            if m:
                break
            if not pump():
                break
        if not m:
            m = rx.search(buf, pos)
        if not m:
            print(f"{time.time() - t0:6.1f}s FAIL  {pat}")
            tail = re.sub(rb"\x1b\[[0-9;?]*[A-Za-z]", b"", buf[-600:])
            print(tail.decode("latin1"))
            break
        pos = m.end()
        line = buf[buf.rfind(b"\n", 0, m.start()) + 1:m.end()]
        print(f"{time.time() - t0:6.1f}s ok    {pat}   | {line.decode('latin1').strip()[:100]}")
    else:
        rc = 0
finally:
    q.kill()
    if a.win:
        subprocess.run(["wineserver", "-k"], stderr=subprocess.DEVNULL)
sys.exit(rc)

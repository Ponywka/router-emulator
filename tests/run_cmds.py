#!/usr/bin/env python3
"""Boot the emulator, wait for the OpenWrt shell and run commands."""
import pexpect, sys, os, time
ROOT = "/home/ultras/wr3000p-emulator"
Q = f"{ROOT}/src/qemu/build/qemu-system-aarch64"
machine = os.environ.get("MACHINE", "cudy-wr3000p")
nand = os.environ.get("NAND", f"{ROOT}/nand-wr3000p")
extra = os.environ.get("QARGS", "-netdev user,id=lan1 -netdev user,id=wan").split()
cmds = sys.argv[1:]
p = pexpect.spawn(Q, ["-M", f"{machine},nand-dir={nand}", "-nographic"] + extra,
                  encoding="latin1", timeout=300)
p.logfile_read = open(f"{ROOT}/work/run.log", "w")
p.expect("Please press Enter to activate this console")
time.sleep(float(os.environ.get("SETTLE", "20")))
p.sendline("")
p.expect("root@OpenWrt:")
for c in cmds:
    p.sendline(c)
    p.expect("root@OpenWrt:", timeout=120)
    print(f"### {c}\n{p.before}")
p.terminate(force=True)

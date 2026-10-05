#!/usr/bin/env python3
"""Windows QEMU build under Wine (run as root), WAN via pcap; check carrier/DHCP."""
import socket, subprocess, time, re, sys
dev = sys.argv[1] if len(sys.argv) > 1 else "\\Device\\NPF_{00000002-0000-0000-0000-4E6574446576}"
port = 45200
args = ["wine", "work/winpkg/WR3000X/qemu/qemu-system-aarch64.exe",
        "-M", "cudy-wr3000p,nand-dir=Z:\\home\\ultras\\wr3000p-emulator\\work\\pcaptest",
        "-display", "none",
        "-chardev", f"socket,id=con,mux=on,host=127.0.0.1,port={port},server=on,wait=on",
        "-serial", "chardev:con", "-mon", "chardev=con",
        "-netdev", f"pcap,id=wan,ifname={dev}"]
q = subprocess.Popen(args, stdout=subprocess.DEVNULL, stderr=open("work/pcaptest.err", "w"))
for _ in range(300):
    try: s = socket.create_connection(("127.0.0.1", port)); break
    except OSError: time.sleep(0.3)
s.settimeout(1); buf = b""
def wait(p, t):
    global buf
    end = time.time() + t
    while time.time() < end:
        try: buf += s.recv(65536)
        except socket.timeout: pass
        except OSError: return False
        if re.search(p, buf): return True
    return False
print("boot", wait(rb"Please press Enter", 300)); time.sleep(50)
s.sendall(b"\r"); wait(rb"root@", 20)
for c in [b"cat /sys/class/net/wan/carrier /sys/class/net/wan/speed",
          b"ifstatus wan | grep -A2 '\"ipv4-address\"' | head -3",
          b"ping -c 2 192.168.200.254 | tail -2"]:
    m = len(buf); s.sendall(c + b"\r"); time.sleep(8); print(buf[m:].decode("latin1")[-300:])
q.kill()

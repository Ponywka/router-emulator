import pexpect, sys, time, socket, os
Q="/home/ultras/wr3000p-emulator/src/qemu/build/qemu-system-aarch64"
mon="/tmp/claude-1000/mon.sock"
try: os.unlink(mon)
except: pass
args=["-M","cudy-wr3000p,nand-dir=/home/ultras/wr3000p-emulator/nand","-nographic","-monitor",f"unix:{mon},server,nowait"]+sys.argv[1:]
p=pexpect.spawn(Q,args,encoding='latin1',timeout=120)
p.logfile_read=open('/home/ultras/wr3000p-emulator/work/reboot.log','w')
p.expect("U-Boot 20")
s=socket.socket(socket.AF_UNIX); s.connect(mon); s.send(b"system_reset\n")
print("reset sent")
i=p.expect(["BL2: Booting BL31","watchdog timeout",pexpect.TIMEOUT],timeout=90)
print("result",i)
p.terminate(force=True)

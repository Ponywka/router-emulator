#!/bin/bash
# Host side networking for the Router Emulator.
#
#   host-bridge.sh setup  [NIC] [BRIDGE]   enslave NIC into BRIDGE (keeps IP/MAC,
#                                          persistent via /etc/network/interfaces)
#   host-bridge.sh taps   [WAN_BR] [USER] [LAN_BR]
#                                          create tap devices for the router
#                                          ports: wr-wan -> WAN_BR (default br0),
#                                          wr-lan1..4 -> LAN_BR (default
#                                          br-wrlan, isolated; host gets
#                                          192.168.1.2/24 on it)
#   host-bridge.sh status
#   host-bridge.sh teardown [NIC] [BRIDGE] undo "setup"
#
# Router ports map to tap devices: wr-lan1..wr-lan4, wr-wan.
set -e
export PATH=$PATH:/usr/sbin:/sbin

NIC=${2:-enp0s3}
BR=${3:-br0}
IFACES=/etc/network/interfaces
TAPS="wr-lan1 wr-lan2 wr-lan3 wr-lan4 wr-wan"

need_root() {
    if [ "$(id -u)" != 0 ]; then
        exec sudo "$0" "$@"
    fi
}

setup() {
    need_root setup "$NIC" "$BR"
    if ip link show "$BR" >/dev/null 2>&1 &&
       [ "$(cat /sys/class/net/$NIC/master/ifindex 2>/dev/null)" = \
         "$(cat /sys/class/net/$BR/ifindex)" ]; then
        echo "$NIC is already part of $BR"
        return
    fi
    MAC=$(cat /sys/class/net/$NIC/address)
    ADDRS=$(ip -4 -o addr show dev "$NIC" | awk '{print $4}')
    GW=$(ip -4 route show default dev "$NIC" | awk '/default/ {print $3; exit}')

    # persistent configuration (ifupdown), keep a backup
    if ! grep -q "iface $BR" $IFACES; then
        cp -a $IFACES $IFACES.before-router-emulator
        sed -i "s/^allow-hotplug $NIC$/# allow-hotplug $NIC (moved to $BR)/; \
                s/^iface $NIC inet dhcp$/iface $NIC inet manual/" $IFACES
        cat >> $IFACES <<EOF

# bridge for the Router Emulator (created by host-bridge.sh)
auto $BR
iface $BR inet dhcp
	bridge_ports $NIC
	bridge_stp off
	bridge_fd 0
	bridge_hw $MAC
EOF
    fi

    # live switch-over, keeping the same MAC and addresses
    ip link add "$BR" type bridge stp_state 0 forward_delay 0
    ip link set "$BR" address "$MAC"
    ip link set "$BR" up
    dhcpcd -x "$NIC" >/dev/null 2>&1 || true
    ip link set "$NIC" master "$BR"
    for a in $ADDRS; do
        ip addr del "$a" dev "$NIC" 2>/dev/null || true
        ip addr add "$a" dev "$BR"
    done
    [ -n "$GW" ] && ip route replace default via "$GW" dev "$BR"
    dhcpcd -b "$BR" >/dev/null 2>&1 || true
    echo "$NIC is now a port of $BR ($ADDRS, gw $GW)"
}

ensure_bridge() {
    local br=$1
    if ! ip link show "$br" >/dev/null 2>&1; then
        ip link add "$br" type bridge stp_state 0 forward_delay 0
        if [ "$br" = br-wrlan ]; then
            ip addr add 192.168.1.2/24 dev "$br"
        fi
    fi
    ip link set "$br" up
    # Docker (br_netfilter + FORWARD DROP) would drop bridged frames
    for ipt in iptables ip6tables; do
        if command -v $ipt >/dev/null; then
            $ipt -C FORWARD -i "$br" -o "$br" -j ACCEPT 2>/dev/null ||
                $ipt -I FORWARD 1 -i "$br" -o "$br" -j ACCEPT
        fi
    done
}

attach_tap() {
    local t=$1 br=$2 user=$3
    ip link show "$t" >/dev/null 2>&1 || ip tuntap add dev "$t" mode tap user "$user"
    ip link set "$t" master "$br"
    ip link set "$t" up
}

taps() {
    local wbr=${2:-br0} user=${3:-${SUDO_USER:-$USER}} lbr=${4:-br-wrlan}
    need_root taps "$wbr" "$user" "$lbr"
    ensure_bridge "$wbr"
    ensure_bridge "$lbr"
    attach_tap wr-wan "$wbr" "$user"
    for t in wr-lan1 wr-lan2 wr-lan3 wr-lan4; do
        attach_tap "$t" "$lbr" "$user"
    done
    echo "wr-wan -> $wbr, wr-lan1..4 -> $lbr (taps owned by $user)"
}

teardown() {
    need_root teardown "$NIC" "$BR"
    for t in $TAPS; do ip link del "$t" 2>/dev/null || true; done
    ip link del br-wrlan 2>/dev/null || true
    # backup from setup (older versions named it before-mt7981/-wr3000x/-wr3000p)
    for b in before-router-emulator before-mt7981 before-wr3000x before-wr3000p; do
        if [ -f $IFACES.$b ]; then
            cp -a $IFACES.$b $IFACES
            break
        fi
    done
    ADDRS=$(ip -4 -o addr show dev "$BR" | awk '{print $4}')
    GW=$(ip -4 route show default dev "$BR" | awk '/default/ {print $3; exit}')
    dhcpcd -x "$BR" >/dev/null 2>&1 || true
    ip link set "$NIC" nomaster
    ip link del "$BR"
    for a in $ADDRS; do ip addr add "$a" dev "$NIC"; done
    [ -n "$GW" ] && ip route replace default via "$GW" dev "$NIC"
    dhcpcd -b "$NIC" >/dev/null 2>&1 || true
    echo "restored $NIC"
}

case "$1" in
setup) setup ;;
taps) taps "$@" ;;
teardown) teardown ;;
status)
    bridge link 2>/dev/null || true
    ip -br addr show
    ;;
*) sed -n '2,12p' "$0"; exit 1 ;;
esac

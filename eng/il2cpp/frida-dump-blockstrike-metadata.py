#!/usr/bin/env python3
"""Dump decrypted global-metadata.dat from Block Strike (com.rexetstudio.blockstrike) via Frida."""

import argparse
import subprocess
import sys
import time

BLOCK_STRIKE_SIZE = 8040192
PATTERN = "af 1b b1 fa 1d 00 00 00 00"

FRIDA_JS = r"""
const fileSize = %d;
const filePattern = '%s';

let fileAddr = null;

function dumpMemory() {
    if (fileAddr === null) return;
    const length = fileSize > 0 ? fileSize : 0x800000;
    send('dump', ptr(fileAddr).readByteArray(length));
}

function memorySearch() {
    const ranges = Process.enumerateRangesSync('r--').concat(Process.enumerateRangesSync('rw-'));
    for (const range of ranges) {
        try {
            Memory.scan(range.base, range.size, filePattern, {
                onMatch(address) {
                    fileAddr = ptr(address);
                    dumpMemory();
                },
                onComplete() {}
            });
        } catch (e) {}
    }
}

function waitForIl2Cpp() {
    const t = setInterval(() => {
        const m = Process.findModuleByName('libil2cpp.so');
        if (m) {
            clearInterval(t);
            memorySearch();
        }
    }, 200);
}

waitForIl2Cpp();
""" % (BLOCK_STRIKE_SIZE, PATTERN)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--adb", default="adb")
    parser.add_argument("--package", default="com.rexetstudio.blockstrike")
    parser.add_argument("--output", required=True)
    parser.add_argument("--size", type=int, default=BLOCK_STRIKE_SIZE)
    args = parser.parse_args()

    try:
        import frida  # noqa: F401
    except ImportError:
        print("Instale frida: pip install frida frida-tools", file=sys.stderr)
        return 1

    import frida

    devices = frida.enumerate_devices()
    device = None
    for d in devices:
        if d.type in ("usb", "remote"):
            device = d
            break
    if device is None:
        print("Nenhum dispositivo Frida USB encontrado.", file=sys.stderr)
        return 2

    print(f"Device: {device.name}")
    pid = device.spawn([args.package])
    session = device.attach(pid)

    dumped = {"ok": False}

    def on_message(message, data):
        if message.get("type") == "send" and message.get("payload") == "dump" and data:
            with open(args.output, "wb") as f:
                f.write(data)
            dumped["ok"] = True
            print(f"Saved {len(data)} bytes -> {args.output}")

    script = session.create_script(FRIDA_JS)
    script.on("message", on_message)
    script.load()
    device.resume(pid)

    deadline = time.time() + 90
    while time.time() < deadline and not dumped["ok"]:
        time.sleep(0.2)

    session.detach()
    if not dumped["ok"]:
        print("Timeout: metadata magic não encontrado na memória.", file=sys.stderr)
        return 3

    if open(args.output, "rb").read(4) != b"\xaf\x1b\xb1\xfa":
        print("Arquivo salvo mas magic inválido.", file=sys.stderr)
        return 4

    print("OK: global-metadata descriptografado.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
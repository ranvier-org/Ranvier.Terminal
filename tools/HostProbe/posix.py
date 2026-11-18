"""Run HostProbe under a controlling PTY with a known viewport and mouse reports."""
import errno
import fcntl
import os
import pty
import select
import signal
import struct
import sys
import termios
import time


def resize(fd, columns, rows):
    fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack("HHHH", rows, columns, 0, 0))


def check(dll, mouse):
    pid, master = pty.fork()
    if pid == 0:
        resize(0, 72, 16)
        mode = "--posix-hover" if mouse else "--posix-mouse-off"
        os.execvp("dotnet", ["dotnet", dll, mode])

    output = bytearray()
    deadline = time.monotonic() + 15

    def read():
        remaining = deadline - time.monotonic()
        if remaining <= 0 or not select.select([master], [], [], remaining)[0]:
            raise TimeoutError(bytes(output))
        try:
            block = os.read(master, 65536)
        except OSError as error:
            if error.errno != errno.EIO:
                raise
            block = b""
        output.extend(block)
        return bool(block)

    def expect(token):
        while token not in output:
            if not read():
                raise AssertionError((token, bytes(output)))

    try:
        expect(b"viewport=72x16")
        resize(master, 58, 12)
        expect(b"viewport=58x12")
        os.write(master, b"\x1b[<35;2;1M")  # No-button SGR motion over the label.
        if not mouse:
            os.write(master, b"\x03")
        while read():
            pass
        for mode in (b"1003", b"1006"):
            for switch in (b"h", b"l"):
                assert ((b"\x1b[?" + mode + switch) in output) == mouse, bytes(output)
    except BaseException:
        try:
            os.kill(pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        raise
    finally:
        os.close(master)
        _, status = os.waitpid(pid, 0)
    if os.waitstatus_to_exitcode(status) != 0:
        raise AssertionError(bytes(output))


if __name__ == "__main__":
    for enabled in (True, False):
        check(sys.argv[1], enabled)
    print("PASS owned initial viewport, idle PTY resize, no-button hover, mouse opt-out and terminal mode cleanup")

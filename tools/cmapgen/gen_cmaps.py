"""Generates src/AdoBye.Content/Resources/predefined-cmaps.bin from Adobe's cmap-resources (BSD-3-Clause).

Usage:  git clone --depth 1 https://github.com/adobe-type-tools/cmap-resources.git tools/cmapgen/src/cmap-resources
        python tools/cmapgen/gen_cmaps.py

Container layout (little endian):
  "PCM1", u16 count, then per CMap: u8 nameLen, name, u32 offset, u32 compressedLen, u32 rawLen
  followed by independently raw-deflated blobs. Each blob decodes to:
  u8 usecmapLen, usecmap name, u8 wmode, varint codespaceCount {u8 nbytes, varint lo, varint hi}
  then for nbytes 1..4: varint rangeCount {varint lo-delta (from previous hi+1), varint hi-lo, zigzag varint cid-delta (from previous cid+len)}
"""
import os, re, struct, sys, zlib

ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, 'src', 'cmap-resources')
OUT = os.path.join(ROOT, '..', '..', 'src', 'AdoBye.Content', 'Resources', 'predefined-cmaps.bin')

NAMES = ('GB-EUC-H GB-EUC-V GBpc-EUC-H GBpc-EUC-V GBK-EUC-H GBK-EUC-V GBKp-EUC-H GBKp-EUC-V GBK2K-H GBK2K-V '
         'UniGB-UCS2-H UniGB-UCS2-V UniGB-UTF16-H UniGB-UTF16-V B5pc-H B5pc-V HKscs-B5-H HKscs-B5-V ETen-B5-H ETen-B5-V '
         'ETenms-B5-H ETenms-B5-V CNS-EUC-H CNS-EUC-V UniCNS-UCS2-H UniCNS-UCS2-V UniCNS-UTF16-H UniCNS-UTF16-V '
         '83pv-RKSJ-H 90ms-RKSJ-H 90ms-RKSJ-V 90msp-RKSJ-H 90msp-RKSJ-V 90pv-RKSJ-H Add-RKSJ-H Add-RKSJ-V EUC-H EUC-V '
         'Ext-RKSJ-H Ext-RKSJ-V H V UniJIS-UCS2-H UniJIS-UCS2-V UniJIS-UCS2-HW-H UniJIS-UCS2-HW-V UniJIS-UTF16-H UniJIS-UTF16-V '
         'KSC-EUC-H KSC-EUC-V KSCms-UHC-H KSCms-UHC-V KSCms-UHC-HW-H KSCms-UHC-HW-V KSCpc-EUC-H UniKS-UCS2-H UniKS-UCS2-V '
         'UniKS-UTF16-H UniKS-UTF16-V').split()


def find(name):
    for dp, _, fs in os.walk(SRC):
        if name in fs and os.path.basename(dp) == 'CMap' and 'deprecated' not in dp:
            return os.path.join(dp, name)
    raise FileNotFoundError(name)


def hexv(s):
    return int(s, 16)


def parse(path):
    txt = open(path, encoding='latin-1').read()
    txt = re.sub(r'%.*', '', txt)
    use = ''
    m = re.search(r'/(\S+)\s+usecmap', txt)
    if m: use = m.group(1)
    wm = re.search(r'/WMode\s+(\d)\s+def', txt)
    wmode = int(wm.group(1)) if wm else 0
    cs = []
    for blk in re.findall(r'begincodespacerange(.*?)endcodespacerange', txt, re.S):
        for lo, hi in re.findall(r'<([0-9a-fA-F]+)>\s*<([0-9a-fA-F]+)>', blk):
            cs.append((len(lo) // 2, hexv(lo), hexv(hi)))
    rng = {1: [], 2: [], 3: [], 4: []}
    for blk in re.findall(r'begincidrange(.*?)endcidrange', txt, re.S):
        for lo, hi, cid in re.findall(r'<([0-9a-fA-F]+)>\s*<([0-9a-fA-F]+)>\s*(\d+)', blk):
            rng[len(lo) // 2].append((hexv(lo), hexv(hi), int(cid)))
    for blk in re.findall(r'begincidchar(.*?)endcidchar', txt, re.S):
        for c, cid in re.findall(r'<([0-9a-fA-F]+)>\s*(\d+)', blk):
            rng[len(c) // 2].append((hexv(c), hexv(c), int(cid)))
    return use, wmode, cs, rng


def varint(v):
    out = bytearray()
    while v >= 0x80:
        out.append(v & 0x7F | 0x80); v >>= 7
    out.append(v)
    return bytes(out)


def zz(v):
    return varint((v << 1) ^ (v >> 63) if v >= 0 else ((-v) << 1) - 1)


def encode(use, wmode, cs, rng):
    b = bytearray()
    u = use.encode(); b.append(len(u)); b += u; b.append(wmode)
    b += varint(len(cs))
    for n, lo, hi in cs:
        b.append(n); b += varint(lo); b += varint(hi)
    for n in (1, 2, 3, 4):
        rs = sorted(rng[n])
        b += varint(len(rs))
        prev_hi = -1; prev_cid = 0
        for lo, hi, cid in rs:
            b += varint(lo - (prev_hi + 1)) if lo > prev_hi else varint(0)
            b += varint(hi - lo)
            b += zz(cid - prev_cid)
            prev_hi = hi; prev_cid = cid + (hi - lo) + 1
    return bytes(b)


def main():
    blobs = []
    for n in NAMES:
        raw = encode(*parse(find(n)))
        c = zlib.compressobj(9, zlib.DEFLATED, -15)
        blobs.append((n, c.compress(raw) + c.flush(), len(raw)))
    head = bytearray(b'PCM1' + struct.pack('<H', len(blobs)))
    hdr_len = len(head) + sum(1 + len(n) + 12 for n, _, _ in blobs)
    off = hdr_len
    for n, z, rl in blobs:
        head += bytes([len(n)]) + n.encode() + struct.pack('<III', off, len(z), rl)
        off += len(z)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'wb') as f:
        f.write(head)
        for _, z, _ in blobs: f.write(z)
    print(len(blobs), 'cmaps,', off, 'bytes')


main()

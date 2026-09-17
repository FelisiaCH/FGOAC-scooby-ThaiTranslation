r"""Build a game font that can show Thai, from the original game font and a Thai donor font.

The game font has no Thai, and the game draws one glyph per character
with no shaping.  This tool copies the donor's Thai outlines into Hangul
glyph slots of the game font and maps the Thai code points to them.  It
also gives every multi-character Thai cluster in the --text files its own
glyph, shaped by HarfBuzz from the donor and mapped to a Private Use Area
code point from U+E000.  Game text must then be encoded with
thai_clusters.encode() and the clusters.json written here.

The source must be the ORIGINAL game font (Source Han Serif SC Bold,
65,535 glyphs, App\zh\rom\font\SEGA_Skip-B.ttf), not one this tool has
already patched.  It is only read.

The donor can be any OFL font with Thai glyphs.  A variable font is
pinned at wght 700, with its other axes at their defaults.  The donor
tested in the game is Noto Serif Thai (OFL), from
  https://github.com/google/fonts/raw/main/ofl/notoserifthai/NotoSerifThai%5Bwdth,wght%5D.ttf
Neither font is kept in this repo.

Writes to --out-dir: the new font (named like the source), clusters.json,
slots.tsv (which glyph slot each new code point took), preview.svg (each
text line as the game draws it, in black, above HarfBuzz's rendering from
the donor, in blue), and donor.ttf (the donor as used).  Exits non-zero
if any check fails.

Requires fontTools and uharfbuzz.

Usage
  python tools/thai_font_build.py --source ORIGINAL_FONT --donor DONOR_FONT
      --text FILE [--text FILE ...] --out-dir DIR
"""
import argparse
import hashlib
import json
import logging
import os
import shutil
import sys
import time

import uharfbuzz as hb
from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

import thai_clusters

# Spot checks: tho thahan, sara ii, mai ek, sara am, sara u.
CHECK_CPS = [0x0E17, 0x0E35, 0x0E48, 0x0E33, 0x0E38]
FAILURES = []


class CountingHandler(logging.Handler):
    """Counts log records, so fontTools warnings show in the summary."""
    count = 0

    def emit(self, record):
        CountingHandler.count += 1


def check(ok, text):
    print(("  ok   " if ok else "  FAIL ") + text)
    if not ok:
        FAILURES.append(text)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def same_file(a, b):
    return os.path.normcase(os.path.realpath(a)) == os.path.normcase(os.path.realpath(b))


def shape(hb_font, text):
    """HarfBuzz output as (gid, x_offset, y_offset, x_advance, cluster)."""
    buf = hb.Buffer()
    buf.add_str(text)
    buf.direction = "ltr"
    buf.script = "thai"
    buf.language = "th"
    hb.shape(hb_font, buf, {})
    return [(i.codepoint, p.x_offset, p.y_offset, p.x_advance, i.cluster)
            for i, p in zip(buf.glyph_infos, buf.glyph_positions)]


def write_preview(path, font, best, dglyphs, dorder, hb_font, lines, encoded):
    """Per line: the encoded text drawn naively with the new font, then HarfBuzz's reference."""
    scale = 96 / font["head"].unitsPerEm
    margin = 40
    pitch = 150
    pair_gap = 230
    glyphs = font.getGlyphSet()
    paths = []
    width = 0

    def draw(glyphset, name, x, y, fill):
        pen = SVGPathPen(glyphset)
        glyphset[name].draw(pen)
        if pen.getCommands():
            paths.append('<path fill="%s" transform="translate(%.2f %.2f) scale(%.4f %.4f)" d="%s"/>'
                         % (fill, margin + x * scale, y, scale, -scale, pen.getCommands()))

    for i, (s, e) in enumerate(zip(lines, encoded)):
        top = margin + i * (pitch + pair_gap)
        x = 0
        for ch in e:
            name = best.get(ord(ch), ".notdef")
            draw(glyphs, name, x, top + 120, "#1a1a1a")
            x += font["hmtx"][name][0]
        width = max(width, 2 * margin + x * scale)
        x = 0
        for gid, dx, dy, adv, cl in shape(hb_font, s):
            draw(dglyphs, dorder[gid], x + dx, top + 120 + pitch - dy * scale, "#1f4e8c")
            x += adv
        width = max(width, 2 * margin + x * scale)
        if i < len(lines) - 1:
            rule_y = top + 120 + pitch + 75
            paths.append('<line x1="0" y1="%.1f" x2="100%%" y2="%.1f" stroke="#bbbbbb" stroke-width="1"/>'
                         % (rule_y, rule_y))
    height = margin + (len(lines) - 1) * (pitch + pair_gap) + 120 + pitch + 60 + margin
    width = int(width)
    with open(path, "w", encoding="ascii", newline="\n") as f:
        f.write('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d">\n' % (width, height))
        f.write('<rect width="100%" height="100%" fill="#ffffff"/>\n')
        f.write("\n".join(paths))
        f.write("\n</svg>\n")
    return width, height


def main(argv=None):
    parser = argparse.ArgumentParser(
        prog="thai_font_build.py",
        description="Build a game font that can show Thai.",
    )
    parser.add_argument("--source", required=True, metavar="ORIGINAL_FONT",
                        help="the original game font SEGA_Skip-B.ttf (read only)")
    parser.add_argument("--donor", required=True, metavar="DONOR_FONT",
                        help="any OFL font with Thai glyphs; variable fonts are pinned at wght 700")
    parser.add_argument("--text", required=True, action="append", metavar="FILE",
                        help="UTF-8 text whose Thai clusters get glyphs (repeatable)")
    parser.add_argument("--out-dir", required=True, metavar="DIR",
                        help="where the outputs go (not on drive E:)")
    args = parser.parse_args(argv)

    out_dir = os.path.realpath(args.out_dir)
    out = os.path.join(out_dir, os.path.basename(args.source))
    instance = os.path.join(out_dir, "donor.ttf")
    if os.path.splitdrive(out_dir)[0].upper() == "E:":
        sys.exit("STOP: %s is on drive E:, the untouched reference copy" % out_dir)
    if any(same_file(o, i) for o in (out, instance) for i in (args.source, args.donor)):
        sys.exit("STOP: an output in %s would overwrite --source or --donor" % out_dir)
    lines = []
    for path in args.text:
        with open(path, encoding="utf-8-sig") as f:
            lines += [line for line in f.read().splitlines() if line]
    if not lines:
        sys.exit("STOP: the --text files hold no text")

    logging.basicConfig(level=logging.WARNING, format="LOG %(levelname)s %(name)s: %(message)s")
    logging.captureWarnings(True)
    logging.getLogger().addHandler(CountingHandler())
    start = time.time()

    # 1. Donor: pinned if variable, saved into out-dir, then loaded from that file by fontTools and HarfBuzz.
    os.makedirs(out_dir, exist_ok=True)
    with TTFont(args.donor) as original_donor:
        variable = "fvar" in original_donor
        if variable:
            location = {axis.axisTag: axis.defaultValue for axis in original_donor["fvar"].axes}
            if "wght" in location:
                location["wght"] = 700
            print("donor is variable; instance at " + ", ".join("%s=%g" % kv for kv in location.items()))
            instantiateVariableFont(original_donor, location).save(instance)
    if not variable:
        print("donor is static; used as is")
        shutil.copyfile(args.donor, instance)
    donor = TTFont(instance)
    dcmap = donor.getBestCmap()
    dglyphs = donor.getGlyphSet()
    dorder = donor.getGlyphOrder()
    thai = [cp for cp in range(0x0E01, 0x0E5C) if cp in dcmap]
    print("donor Thai code points: %d" % len(thai))
    hb_font = hb.Font(hb.Face(hb.Blob.from_file_path(instance)))
    upem = donor["head"].unitsPerEm
    hb_font.scale = (upem, upem)
    dotted = donor.getGlyphID(dcmap[0x25CC]) if 0x25CC in dcmap else None

    # 2. Source (read only, must be the untouched original).
    print("source sha256 %s" % sha256(args.source))
    font = TTFont(args.source)
    glyf = font["glyf"]
    orig_best = dict(font.getBestCmap())
    orig_gids = {c: font.getGlyphID(n) for c, n in orig_best.items()}
    print("source numGlyphs: %d, post format %s" % (font["maxp"].numGlyphs, font["post"].formatType))
    if font["head"].unitsPerEm != upem:
        sys.exit("STOP: donor unitsPerEm %d, source %d; outlines are not scaled"
                 % (upem, font["head"].unitsPerEm))
    already = [cp for cp in thai if cp in orig_best]
    if already:
        sys.exit("STOP: the source already maps %d Thai code points; use the original game font" % len(already))

    # 3. Clusters and their PUA code points.
    cluster_list = thai_clusters.distinct_clusters(lines)
    pua = {c: 0xE000 + i for i, c in enumerate(cluster_list)}
    taken = [cp for cp in pua.values() if cp in orig_best]
    if taken:
        sys.exit("STOP: PUA code points already mapped in the source: %s" % taken)
    print("clusters: %d distinct, PUA U+E000..U+%04X" % (len(cluster_list), 0xE000 + len(cluster_list) - 1))

    # 4. Slots: Hangul glyphs mapped once in (3,10,12) and never used as a component.
    t = time.time()
    components = set()
    composites = 0
    for name in font.getGlyphOrder():
        g = glyf[name]
        if g.isComposite():
            composites += 1
            components.update(c.glyphName for c in g.components)
    print("composite glyphs in source: %d (distinct components referenced: %d, scan %.1fs)"
          % (composites, len(components), time.time() - t))
    uni12 = [st for st in font["cmap"].tables if (st.platformID, st.platEncID, st.format) == (3, 10, 12)][0].cmap
    uses = {}
    for name in uni12.values():
        uses[name] = uses.get(name, 0) + 1
    slots = []
    skipped = 0
    cp = 0xAC00
    while len(slots) < len(thai) + len(cluster_list):
        name = orig_best[cp]
        if uses[name] == 1 and name not in components:
            slots.append((cp, name))
        else:
            skipped += 1
        cp += 1
    print("slots: %d taken from U+AC00..U+%04X, %d candidates skipped" % (len(slots), cp - 1, skipped))
    thai_slots = slots[:len(thai)]
    cluster_slots = slots[len(thai):]
    # (new code point, Hangul code point, slot glyph name) for every slot.
    assign = [(tcp, hcp, name) for tcp, (hcp, name) in zip(thai, thai_slots)]
    assign += [(pua[c], hcp, name) for c, (hcp, name) in zip(cluster_list, cluster_slots)]

    # 5a. Thai outlines and metrics.
    ymin = (0, None)
    ymax = (0, None)
    for tcp, (hcp, name) in zip(thai, thai_slots):
        rec = DecomposingRecordingPen(dglyphs)
        dglyphs[dcmap[tcp]].draw(rec)
        pen = TTGlyphPen(None)
        rec.replay(pen)
        new = pen.glyph()
        new.recalcBounds(glyf)
        glyf[name] = new
        has_outline = new.numberOfContours > 0
        font["hmtx"][name] = (donor["hmtx"][dcmap[tcp]][0], new.xMin if has_outline else 0)
        if has_outline and new.yMin < ymin[0]:
            ymin = (new.yMin, tcp)
        if has_outline and new.yMax > ymax[0]:
            ymax = (new.yMax, tcp)

    # 5b. One glyph per cluster from HarfBuzz's shaped result.
    shaped = {}
    for c, (hcp, name) in zip(cluster_list, cluster_slots):
        run = shape(hb_font, c)
        for gid, dx, dy, adv, cl in run:
            if gid == 0 or gid == dotted or "25cc" in dorder[gid].lower() or "dotted" in dorder[gid].lower():
                sys.exit("STOP: HarfBuzz emitted %s (gid %d) for cluster %s" % (dorder[gid], gid, ascii(c)))
        shaped[c] = run
        pen = TTGlyphPen(None)
        x = 0
        for gid, dx, dy, adv, cl in run:
            rec = DecomposingRecordingPen(dglyphs)
            dglyphs[dorder[gid]].draw(rec)
            rec.replay(TransformPen(pen, (1, 0, 0, 1, x + dx, dy)))
            x += adv
        new = pen.glyph()
        new.recalcBounds(glyf)
        glyf[name] = new
        font["hmtx"][name] = (x, new.xMin)

    # 6. cmap: formats 4 and 12 only.
    for st in font["cmap"].tables:
        if st.format in (4, 12):
            for ncp, hcp, name in assign:
                if st.cmap.get(hcp) == name:
                    del st.cmap[hcp]
                st.cmap[ncp] = name
    for st in font["cmap"].tables:
        if st.format == 4:
            try:
                size = len(st.compile(font))
            except Exception as e:
                sys.exit("STOP: format 4 subtable (%d,%d) failed to compile: %r"
                         % (st.platformID, st.platEncID, e))
            print("format 4 (%d,%d) compiles to %d bytes (limit 65535)" % (st.platformID, st.platEncID, size))
    slot_names = {name for hcp, name in slots}
    slot_hangul = {hcp for hcp, name in slots}
    for st in font["cmap"].tables:
        if st.format == 14:
            hits = sum(1 for pairs in st.uvsDict.values() for u, n in pairs if n in slot_names or u in slot_hangul)
            print("format 14 (%d,%d) entries touching the slots: %d" % (st.platformID, st.platEncID, hits))
        if st.format == 6:
            hits = sum(1 for n in st.cmap.values() if n in slot_names)
            print("format 6 (%d,%d) entries touching the slots: %d" % (st.platformID, st.platEncID, hits))

    # 7. Save.
    t = time.time()
    font.save(out)
    print("saved %s (%d bytes, save %.1fs)" % (out, os.path.getsize(out), time.time() - t))
    with open(os.path.join(out_dir, "clusters.json"), "w", encoding="ascii", newline="\n") as f:
        json.dump(pua, f, ensure_ascii=True, indent=1)
    print("wrote clusters.json")

    # 8. Verify from a fresh load. Post is format 3 (no stored names): compare glyph IDs, not names.
    print("verification:")
    f2 = TTFont(out)
    check(f2["maxp"].numGlyphs == 65535 and len(f2.getGlyphOrder()) == 65535,
          "numGlyphs %d" % f2["maxp"].numGlyphs)
    new_best = f2.getBestCmap()
    new_gids = {c: f2.getGlyphID(n) for c, n in new_best.items()}
    slot_gid = {ncp: font.getGlyphID(name) for ncp, hcp, name in assign}
    removed = set(orig_gids) - set(new_gids)
    added = set(new_gids) - set(orig_gids)
    changed = [c for c in set(orig_gids) & set(new_gids) if orig_gids[c] != new_gids[c]]
    check(removed == slot_hangul, "best cmap: %d code points removed, all of them the Hangul slots" % len(removed))
    check(added == set(thai) | set(pua.values()),
          "best cmap: %d code points added, exactly the donor Thai set + the PUA clusters" % len(added))
    check(not changed, "best cmap: %d other mappings changed (by glyph ID)" % len(changed))
    check(all(new_gids[tcp] == slot_gid[tcp] for tcp in thai),
          "%d Thai code points map to their slot glyph IDs" % len(thai))
    check(all(new_gids[p] == slot_gid[p] for p in pua.values()),
          "%d PUA code points map to their slot glyph IDs" % len(pua))
    for st in f2["cmap"].tables:
        if st.format in (4, 12):
            ok = all(ncp in st.cmap and f2.getGlyphID(st.cmap[ncp]) == slot_gid[ncp] and hcp not in st.cmap
                     for ncp, hcp, name in assign)
            check(ok, "subtable (%d,%d,%d) holds the swap (Thai + PUA)" % (st.platformID, st.platEncID, st.format))
    for tcp in CHECK_CPS:
        name = new_best[tcp]
        g = f2["glyf"][name]
        adv = f2["hmtx"][name][0]
        dadv = donor["hmtx"][dcmap[tcp]][0]
        check(adv == dadv and g.numberOfContours > 0,
              "U+%04X -> gid %d: advance %d (donor %d), lsb %d, contours %d, bbox %d %d %d %d, program %d bytes"
              % (tcp, f2.getGlyphID(name), adv, dadv, f2["hmtx"][name][1], g.numberOfContours,
                 g.xMin, g.yMin, g.xMax, g.yMax, len(g.program.getBytecode())))
    cymin = (0, None)
    cymax = (0, None)
    for c in cluster_list:
        name = new_best[pua[c]]
        g = f2["glyf"][name]
        adv, lsb = f2["hmtx"][name]
        hb_adv = sum(r[3] for r in shaped[c])
        check(adv == hb_adv and lsb == g.xMin and g.numberOfContours > 0 and not g.program.getBytecode(),
              "%s -> U+%04X -> gid %d, advance %d (HarfBuzz sum %d), lsb %d, bbox %d %d %d %d"
              % (ascii(c)[1:-1], pua[c], f2.getGlyphID(name), adv, hb_adv, lsb, g.xMin, g.yMin, g.xMax, g.yMax))
        print("         HarfBuzz: " + "  ".join("%s(cl%d) off %d,%d adv %d" % (dorder[gid], cl, dx, dy, a)
                                              for gid, dx, dy, a, cl in shaped[c]))
        if g.yMin < cymin[0]:
            cymin = (g.yMin, c)
        if g.yMax > cymax[0]:
            cymax = (g.yMax, c)
    encoded = [thai_clusters.encode(s, pua) for s in lines]
    missing = sorted({ord(ch) for e in encoded for ch in e if ord(ch) not in new_best})
    check(not missing, "every character of the encoded text lines is in the new cmap (missing: %s)"
          % ["U+%04X" % m for m in missing])
    for e in encoded:
        print("  encode: %s" % ascii(e))
    with open(os.path.join(out_dir, "slots.tsv"), "w", encoding="ascii", newline="\n") as f:
        f.write("new_cp\tgid\tglyph_in_original\tformer_hangul_cp\n")
        for ncp, hcp, name in assign:
            f.write("U+%04X\t%d\t%s\tU+%04X\n" % (ncp, slot_gid[ncp], name, hcp))
    print("wrote slots.tsv")
    print("Thai outline extremes: yMin %d (U+%04X), yMax %d (U+%04X)" % (ymin[0], ymin[1], ymax[0], ymax[1]))
    print("cluster glyph extremes: yMin %d (%s), yMax %d (%s); hhea ascent %d descent %d"
          % (cymin[0], ascii(cymin[1]), cymax[0], ascii(cymax[1]), f2["hhea"].ascent, f2["hhea"].descent))

    # 9. Preview.
    width, height = write_preview(os.path.join(out_dir, "preview.svg"), f2, new_best, dglyphs, dorder,
                                  hb_font, lines, encoded)
    print("wrote preview.svg (%dx%d)" % (width, height))

    print("failures: %d, logged warnings: %d, runtime %.1fs"
          % (len(FAILURES), CountingHandler.count, time.time() - start))
    return 1 if FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())

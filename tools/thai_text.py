r"""Turn the game's plain-text files into gettext templates, and build game files from Thai translations.

FGO Arcade keeps its Japanese text in App\rom\single\*.txt, and the
English fan patch puts same-named English files in App\zh\rom\single\,
which the game loads instead.  Each file is UTF-8 with a BOM: a first
line " utf8-dos", then comment (#), blank and key=value lines.  A literal
backslash + n in a value is a line break in the game.

extract  writes one template per component (the file name without a
         trailing _digits and .txt) to DIR\single\<component>.pot.  It
         takes every key whose English value differs from the Japanese
         one.  msgctxt is <file>:<key>, msgid is the English value, and
         the Japanese value is an extracted comment.  Only backslash + n
         becomes a real newline; any other backslash stays as it is.  It
         stops if a value holds two backslashes in a row, because then
         backslash + n could be an escaped backslash followed by n.
lines    writes each line of every distinct Thai msgstr in the .po files
         under the given folders, as --text for thai_font_build.py.
build    writes every English file to DIR\single\ with values replaced
         from <po dir>\single\<component>.po, matched by msgctxt.
         community mode: a community msgstr that is not fuzzy, else an AI
         msgstr, else English.  ai mode: an AI msgstr, else English.
         Real newlines become backslash + n again.  Thai is encoded with
         thai_clusters.encode() and the font's clusters.json.  It stops
         before writing if a cluster is missing, or if a value to write
         holds two backslashes in a row.  Every other byte of each file
         is kept.

The game folders are only read.  The generated files hold the game's
script text: keep them out of this repo.

Requires polib.

Usage
  python tools/thai_text.py extract --jp JP_ROM --en EN_ROM --out DIR
  python tools/thai_text.py lines --po DIR [--po DIR ...] --out FILE
  python tools/thai_text.py build --en EN_ROM --mode {ai,community}
      [--ai DIR] [--community DIR] [--clusters CLUSTERS_JSON] --out DIR
"""
import argparse
import glob
import json
import os
import re
import sys

import polib

import thai_clusters

BOM = b"\xef\xbb\xbf"
FIRST_LINE = " utf8-dos"
THAI = re.compile("[\u0e00-\u0e7f]")
JAPANESE = re.compile("[\u3040-\u30ff\u4e00-\u9fff\uff00-\uffef]")
ASCII_LETTER = re.compile("[A-Za-z]")
DOUBLE_BACKSLASH = r"\\"
METADATA = {
    "MIME-Version": "1.0",
    "Content-Type": "text/plain; charset=UTF-8",
    "Content-Transfer-Encoding": "8bit",
}


def stop(message):
    sys.exit("STOP: " + message)


def component(name):
    """quest_prop_000001.txt -> quest_prop"""
    return re.sub(r"(_\d+)?\.txt$", "", name)


def parse(path):
    """The file as a list of lines and line ends, and its {key: (value, index of the line in that list)}."""
    with open(path, "rb") as f:
        data = f.read()
    if not data.startswith(BOM):
        stop(path + " has no UTF-8 BOM")
    try:
        text = data[len(BOM):].decode("utf-8")
    except UnicodeDecodeError as e:
        stop("%s is not UTF-8: %s" % (path, e))
    # Lines at even indexes, their ends ("\r\n", or a bare "\n") at odd ones.
    parts = re.split("(\r?\n)", text)
    if parts[0] != FIRST_LINE:
        stop("%s line 1 is %r, expected %r" % (path, parts[0], FIRST_LINE))
    entries = {}
    for i in range(2, len(parts), 2):
        line = parts[i]
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            stop("%s line %d is not blank, a comment or key=value: %r" % (path, i // 2 + 1, line))
        key, value = line.split("=", 1)
        if key in entries:
            stop("%s line %d repeats the key %s" % (path, i // 2 + 1, key))
        entries[key] = (value, i)
    return parts, entries


def double_backslashes(files):
    """(side, file, key, value) for every value holding two backslashes in a row."""
    return [(side, name, key, value)
            for name, en, jp in files
            for side, entries in (("EN", en), ("JP", jp))
            for key, (value, i) in entries.items() if DOUBLE_BACKSLASH in value]


def extract(jp_rom, en_rom, out):
    en_dir = os.path.join(en_rom, "single")
    jp_dir = os.path.join(jp_rom, "single")
    names = sorted(n for n in os.listdir(en_dir) if n.endswith(".txt"))
    if not names:
        stop("no .txt files in " + en_dir)
    jp_only = sorted(set(n for n in os.listdir(jp_dir) if n.endswith(".txt")) - set(names))
    no_jp = [n for n in names if not os.path.isfile(os.path.join(jp_dir, n))]
    files = [(n, parse(os.path.join(en_dir, n))[1], {} if n in no_jp else parse(os.path.join(jp_dir, n))[1])
             for n in names]
    doubled = double_backslashes(files)
    if doubled:
        for side, name, key, value in doubled:
            print("  %s %s:%s=%r" % (side, name, key, value))
        stop("%d values hold two backslashes in a row, so backslash + n is ambiguous; nothing written"
             % len(doubled))

    templates = {}
    file_counts = {}
    en_only = []
    kinds = {}
    no_letters = {}
    no_japanese = {}
    for name, en, jp in files:
        comp = component(name)
        if comp not in templates:
            templates[comp] = polib.POFile(wrapwidth=0)
            templates[comp].metadata = dict(METADATA)
        file_counts[comp] = file_counts.get(comp, 0) + 1
        for key, (value, i) in en.items():
            ctx = name + ":" + key
            if key in jp:
                if value == jp[key][0]:
                    continue
                comment = "JP: " + jp[key][0].replace("\\n", "\n")
            elif ASCII_LETTER.search(value):
                en_only.append(ctx)
                comment = "JP: (no such key)"
            else:
                continue
            templates[comp].append(polib.POEntry(msgctxt=ctx, msgid=value.replace("\\n", "\n"), msgstr="",
                                                 comment=comment, occurrences=[("single/" + name, "")]))
            kind = re.sub(r"\d+", "", key)
            kinds[kind] = kinds.get(kind, 0) + 1
            jp_value = jp[key][0] if key in jp else None
            if not ASCII_LETTER.search(value):
                no_letters.setdefault(kind, []).append((ctx, value, jp_value))
            if jp_value is not None and not JAPANESE.search(jp_value):
                no_japanese.setdefault(kind, []).append((ctx, value, jp_value))

    os.makedirs(os.path.join(out, "single"), exist_ok=True)
    for comp, pot in templates.items():
        pot.save(os.path.join(out, "single", comp + ".pot"), newline="\n")

    print("EN files: %d, with no JP file: %d %s" % (len(names), len(no_jp), no_jp))
    groups = {}
    for n in jp_only:
        groups.setdefault(component(n), []).append(n)
    print("JP-only files, skipped: %d" % len(jp_only))
    for comp, group in groups.items():
        print("  %-22s %4d  %s .. %s" % (comp, len(group), group[0], group[-1]))
    print("EN-only keys, included: %d" % len(en_only))
    for ctx in en_only:
        print("  " + ctx)
    print("%-22s %6s %8s" % ("component", "files", "entries"))
    for comp, pot in templates.items():
        print("  %-20s %6d %8d  %s" % (comp, file_counts[comp], len(pot),
                                        os.path.join(out, "single", comp + ".pot")))
    print("  %-20s %6d %8d" % ("total", len(names), sum(len(pot) for pot in templates.values())))
    print("%-40s %6s" % ("key name", "count"))
    for kind, count in sorted(kinds.items(), key=lambda kv: (-kv[1], kv[0])):
        print("  %-38s %6d" % (kind, count))
    for title, found in (("translatable EN value with no ASCII letter", no_letters),
                         ("JP value with no Japanese character", no_japanese)):
        print("review, %s: %d key names" % (title, len(found)))
        for kind, examples in sorted(found.items()):
            print("  %s: %d of %d" % (kind, len(examples), kinds[kind]))
            for ctx, value, jp_value in examples[:3]:
                print("    %s  EN %r  JP %r" % (ctx, value, jp_value))


def lines(po_dirs, out):
    msgstrs = []
    count = 0
    for po_dir in po_dirs:
        if not os.path.isdir(po_dir):
            stop(po_dir + " is not a folder")
        for path in sorted(glob.glob(os.path.join(po_dir, "**", "*.po"), recursive=True)):
            count += 1
            msgstrs += [e.msgstr for e in polib.pofile(path) if not e.obsolete and THAI.search(e.msgstr)]
    msgstrs = list(dict.fromkeys(msgstrs))
    text = list(dict.fromkeys(line for s in msgstrs for line in s.split("\n") if line))
    if not text:
        stop("no msgstr with Thai in %d .po files; nothing written" % count)
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(text) + "\n")
    print("%d .po files, %d distinct Thai msgstrs, %d distinct lines written to %s"
          % (count, len(msgstrs), len(text), out))


def load_pos(label, po_dir):
    """{component: (path, {msgctxt: entry})} from po_dir\\single\\*.po; empty if po_dir is None."""
    pos = {}
    if po_dir is None:
        return pos
    if not os.path.isdir(po_dir):
        stop(po_dir + " is not a folder")
    for path in sorted(glob.glob(os.path.join(po_dir, "single", "*.po"))):
        try:
            entries = {e.msgctxt: e for e in polib.pofile(path) if not e.obsolete}
        except (OSError, ValueError) as e:
            stop("cannot read %s: %r" % (path, e))
        pos[os.path.basename(path)[:-len(".po")]] = (path, entries)
    print("%s: %d .po files, %d entries, from %s"
          % (label, len(pos), sum(len(entries) for path, entries in pos.values()), po_dir))
    return pos


def build(en_rom, mode, ai_dir, community_dir, clusters_path, out):
    en_dir = os.path.join(en_rom, "single")
    out_dir = os.path.join(out, "single")
    if os.path.normcase(os.path.realpath(en_dir)) == os.path.normcase(os.path.realpath(out_dir)):
        stop("--out would overwrite the files of --en")
    names = sorted(n for n in os.listdir(en_dir) if n.endswith(".txt"))
    if not names:
        stop("no .txt files in " + en_dir)
    ai = load_pos("ai", ai_dir)
    community = load_pos("community", community_dir) if mode == "community" else {}
    mapping = None
    if clusters_path:
        with open(clusters_path, encoding="utf-8") as f:
            mapping = json.load(f)

    counts = {"community": 0, "ai": 0, "english": 0}
    untouched = 0
    seen = set()
    doubled = []
    problems = []
    outputs = []
    for name in names:
        parts, entries = parse(os.path.join(en_dir, name))
        comp = component(name)
        for key, (value, i) in entries.items():
            ctx = name + ":" + key
            seen.add((comp, ctx))
            c = community.get(comp, (None, {}))[1].get(ctx)
            a = ai.get(comp, (None, {}))[1].get(ctx)
            if c is None and a is None:
                untouched += 1
                continue
            if c is not None and c.msgstr and "fuzzy" not in c.flags:
                source, new = "community", c.msgstr
            elif a is not None and a.msgstr:
                source, new = "ai", a.msgstr
            else:
                counts["english"] += 1
                continue
            counts[source] += 1
            new = new.replace("\n", "\\n")
            if DOUBLE_BACKSLASH in new:
                doubled.append(ctx)
                continue
            if THAI.search(new):
                missing = [s for s in thai_clusters.distinct_clusters([new]) if mapping is None or s not in mapping]
                if mapping is None or missing:
                    problems.append((ctx, missing))
                    continue
                new = thai_clusters.encode(new, mapping)
            parts[i] = key + "=" + new
        outputs.append((name, BOM + "".join(parts).encode("utf-8")))

    unmatched = 0
    for pos in (community, ai):
        for comp, (path, entries) in pos.items():
            for ctx in entries:
                if (comp, ctx) not in seen:
                    unmatched += 1
                    print("WARNING: %s: msgctxt %r matches no line of the %s files in %s" % (path, ctx, comp, en_dir))
    if doubled:
        for ctx in doubled:
            print("  " + ctx)
        stop("%d values to write hold two backslashes in a row, so backslash + n would be ambiguous; nothing written"
             % len(doubled))
    if problems:
        for ctx, missing in problems:
            print("  %s: %s" % (ctx, " ".join(ascii(s)[1:-1] for s in missing) or "(no multi-character cluster)"))
        if mapping is None:
            stop("%d values hold Thai and --clusters was not given; nothing written" % len(problems))
        stop("%d values hold Thai clusters missing from %s; nothing written" % (len(problems), clusters_path))

    os.makedirs(out_dir, exist_ok=True)
    for name, data in outputs:
        with open(os.path.join(out_dir, name), "wb") as f:
            f.write(data)
    print("wrote %d files to %s" % (len(outputs), out_dir))
    print("values from community: %d, ai: %d, english (in a .po, no usable msgstr): %d"
          % (counts["community"], counts["ai"], counts["english"]))
    print("key=value lines in no .po, left as they are: %d" % untouched)
    print(".po msgctxts matching no line: %d" % unmatched)


def main(argv=None):
    parser = argparse.ArgumentParser(
        prog="thai_text.py",
        description="Extract the game's plain text to gettext templates, and build game files from translations.",
    )
    commands = parser.add_subparsers(dest="command", required=True)
    p = commands.add_parser("extract", help=r"write .pot templates from single\*.txt")
    p.add_argument("--jp", required=True, metavar="JP_ROM", help=r"the Japanese App\rom (read only)")
    p.add_argument("--en", required=True, metavar="EN_ROM", help=r"the English App\zh\rom (read only)")
    p.add_argument("--out", required=True, metavar="DIR", help=r"templates go to DIR\single")
    p = commands.add_parser("lines", help="write the Thai text lines of .po files, for thai_font_build.py --text")
    p.add_argument("--po", required=True, action="append", metavar="DIR",
                   help="folder searched for .po files, recursively (repeatable)")
    p.add_argument("--out", required=True, metavar="FILE", help="UTF-8 text file to write")
    p = commands.add_parser("build", help=r"write single\*.txt with translated values")
    p.add_argument("--en", required=True, metavar="EN_ROM", help=r"the English App\zh\rom (read only)")
    p.add_argument("--mode", required=True, choices=["ai", "community"], help="which translation set comes first")
    p.add_argument("--ai", metavar="DIR", help=r"AI translations, as DIR\single\<component>.po")
    p.add_argument("--community", metavar="DIR",
                   help=r"community translations, as DIR\single\<component>.po (ignored in ai mode)")
    p.add_argument("--clusters", metavar="CLUSTERS_JSON", help="clusters.json of the Thai game font")
    p.add_argument("--out", required=True, metavar="DIR", help=r"files go to DIR\single")
    args = parser.parse_args(argv)

    sys.stdout.reconfigure(errors="backslashreplace")
    if args.command == "extract":
        extract(args.jp, args.en, args.out)
    elif args.command == "lines":
        lines(args.po, args.out)
    else:
        build(args.en, args.mode, args.ai, args.community, args.clusters, args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())

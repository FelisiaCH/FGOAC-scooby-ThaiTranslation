r"""Thai font probe: put a test font and four Thai test strings into a game install, or undo it.

This is a test, not an installer.  It shows whether the game draws Thai
correctly with a font from thai_font_build.py.  It replaces the font and
five text lines, and each new text value starts with "T0 ":

  App\zh\rom\font\SEGA_Skip-B.ttf          replaced by DIR\SEGA_Skip-B.ttf
  App\zh\rom\single\quest_top_prop.txt     name1, long_name1: the Singularity F
                                           name on the singularity select screen
  App\zh\rom\single\spot_prop.txt          spot_name1, spot_name2: its first two
                                           map spots
  App\zh\rom\single\quest_prop_000001.txt  quest_name: its first quest name

The strings are the four lines of thai_probe_text.txt, next to this
script, encoded with DIR\clusters.json.  So the font must be built with
--text tools/thai_probe_text.txt.

The first apply copies the four original files to ROOT\_t0-backup with a
sha256 manifest, and verifies the copies before it changes anything.  A
later apply verifies that backup and edits from it, so apply can be run
again.  Restore copies the originals back and checks their hashes.  The
backup stays in place.

It refuses a game folder on drive E: (the untouched reference copy), and
it refuses to run while ago.exe or "FGOAC scooby.exe" is running.

Usage
  python tools/thai_probe.py apply --game ROOT --build-dir DIR
  python tools/thai_probe.py restore --game ROOT
"""
import argparse
import csv
import hashlib
import json
import os
import shutil
import subprocess
import sys

import thai_clusters

TEXT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "thai_probe_text.txt")
BACKUP_DIR = "_t0-backup"
FONT = r"App\zh\rom\font\SEGA_Skip-B.ttf"
QUEST_TOP = r"App\zh\rom\single\quest_top_prop.txt"
SPOT = r"App\zh\rom\single\spot_prop.txt"
QUEST = r"App\zh\rom\single\quest_prop_000001.txt"
FILES = [FONT, QUEST_TOP, SPOT, QUEST]


def refuse(message):
    print("REFUSED: " + message)
    sys.exit(2)


def edits(build_dir):
    """The line edits, with the test strings encoded to the PUA cluster code points of the new font."""
    try:
        with open(TEXT, encoding="utf-8-sig") as f:
            strings = [line for line in f.read().splitlines() if line]
    except (OSError, ValueError) as e:
        refuse("cannot read %s: %r" % (TEXT, e))
    if len(strings) != 4:
        refuse("%s holds %d lines, need 4" % (TEXT, len(strings)))
    clusters_path = os.path.join(build_dir, "clusters.json")
    try:
        with open(clusters_path, encoding="ascii") as f:
            mapping = json.load(f)
        th1, th2, th3, th4 = [thai_clusters.encode(s, mapping) for s in strings]
    except (OSError, ValueError, KeyError) as e:
        refuse("cannot encode the test strings with %s: %r" % (clusters_path, e))
    # "\\n" is a literal backslash + n, as the game files store it.
    return [
        (QUEST_TOP, "name1=Singularity F", "name1=T0 " + th1),
        (QUEST_TOP, "long_name1=Singularity F\\nFlame Contaminated City",
         "long_name1=T0 " + th1 + "\\n" + th2 + " " + th3),
        (SPOT, "spot_name1=Unidentified Coordinate X-A", "spot_name1=T0 " + th2),
        (SPOT, "spot_name2=Unidentified Coordinate X-B", "spot_name2=T0 " + th4),
        (QUEST, "quest_name=Burning City", "quest_name=T0 " + th3),
    ]


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def check_drive(game):
    if os.path.splitdrive(game)[0].upper() == "E:":
        refuse(game + " is on drive E:, the untouched reference copy")


def check_processes():
    out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True,
                         text=True, errors="replace", check=True).stdout
    running = {row[0].lower() for row in csv.reader(out.splitlines()) if row}
    for exe in ("ago.exe", "FGOAC scooby.exe"):
        if exe.lower() in running:
            refuse(exe + " is running; close the launcher and the game first")


def edit_lines(data, old, new):
    """Replace the one CRLF-separated line equal to old.  Refuses unless exactly one matches."""
    if not data.startswith(b"\xef\xbb\xbf"):
        refuse("file has no UTF-8 BOM")
    lines = data.split(b"\r\n")
    hits = [i for i, line in enumerate(lines) if line == old.encode("utf-8")]
    if len(hits) != 1:
        refuse("%d lines equal %r, need exactly 1" % (len(hits), old))
    lines[hits[0]] = new.encode("utf-8")
    return b"\r\n".join(lines)


def apply(game, build_dir):
    backup = os.path.join(game, BACKUP_DIR)
    new_font = os.path.join(build_dir, "SEGA_Skip-B.ttf")
    line_edits = edits(build_dir)
    if not os.path.isfile(new_font):
        refuse(new_font + " not found; run tools/thai_font_build.py first")
    have_backup = os.path.exists(backup)
    if have_backup:
        # Existing backup: verify it, then edit from the backed-up originals.
        manifest_path = os.path.join(backup, "manifest.json")
        if not os.path.isfile(manifest_path):
            refuse(manifest_path + " not found; game files not changed")
        with open(manifest_path) as f:
            manifest = json.load(f)
    originals = {}
    for rel in FILES:
        with open(os.path.join(backup if have_backup else game, rel), "rb") as f:
            originals[rel] = f.read()
        if have_backup and hashlib.sha256(originals[rel]).hexdigest() != manifest.get(rel):
            refuse("backup copy of %s does not match the manifest; game files not changed" % rel)
    if have_backup:
        print("existing backup verified against manifest, using it as the originals: " + backup)
    edited = {}
    for rel, old, new in line_edits:
        edited[rel] = edit_lines(edited.get(rel, originals[rel]), old, new)
    print("all %d edits validated" % len(line_edits))

    if not have_backup:
        manifest = {rel: hashlib.sha256(data).hexdigest() for rel, data in originals.items()}
        for rel in FILES:
            dst = os.path.join(backup, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(os.path.join(game, rel), dst)
        with open(os.path.join(backup, "manifest.json"), "w") as f:
            json.dump(manifest, f, indent=2)
        for rel in FILES:
            if sha256(os.path.join(backup, rel)) != manifest[rel]:
                refuse("backup copy of %s does not match the original; game files not changed" % rel)
            print("backed up %s sha256=%s" % (rel, manifest[rel]))
        print("backup verified: " + backup)

    shutil.copyfile(new_font, os.path.join(game, FONT))
    print("wrote %s sha256=%s" % (FONT, sha256(os.path.join(game, FONT))))
    for rel, data in edited.items():
        with open(os.path.join(game, rel), "wb") as f:
            f.write(data)
        print("wrote %s sha256=%s" % (rel, sha256(os.path.join(game, rel))))


def restore(game):
    backup = os.path.join(game, BACKUP_DIR)
    manifest_path = os.path.join(backup, "manifest.json")
    if not os.path.isfile(manifest_path):
        refuse(manifest_path + " not found")
    with open(manifest_path) as f:
        manifest = json.load(f)
    for rel in FILES:
        if sha256(os.path.join(backup, rel)) != manifest[rel]:
            refuse("backup copy of %s does not match the manifest; nothing restored" % rel)
    bad = 0
    for rel in FILES:
        dst = os.path.join(game, rel)
        shutil.copy2(os.path.join(backup, rel), dst)
        got = sha256(dst)
        bad += got != manifest[rel]
        print("%s %s sha256=%s" % ("restored" if got == manifest[rel] else "MISMATCH", rel, got))
    print("backup left in place: " + backup)
    return 1 if bad else 0


def main(argv=None):
    parser = argparse.ArgumentParser(
        prog="thai_probe.py",
        description="Put the Thai test font and strings into a game install, or undo it.",
    )
    commands = parser.add_subparsers(dest="command", required=True)
    p = commands.add_parser("apply", help="back up the originals, then write the test font and strings")
    p.add_argument("--game", required=True, metavar="ROOT", help="game folder (the one holding App)")
    p.add_argument("--build-dir", required=True, metavar="DIR", help="--out-dir of thai_font_build.py")
    p = commands.add_parser("restore", help="copy the backed-up originals back")
    p.add_argument("--game", required=True, metavar="ROOT", help="game folder (the one holding App)")
    args = parser.parse_args(argv)

    game = os.path.realpath(args.game)
    check_drive(game)
    check_processes()
    if args.command == "apply":
        apply(game, args.build_dir)
        return 0
    return restore(game)


if __name__ == "__main__":
    sys.exit(main())

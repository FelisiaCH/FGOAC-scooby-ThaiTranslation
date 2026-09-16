#!/usr/bin/env python3
"""Invariant checker for the FGOAC scooby Thai localisation (task T-V0).

Compares the working tree against a baseline git ref (default HEAD) and exits
non-zero if any localisation invariant is broken, printing one line per
violation.  Python 3.11, standard library only.  Read-only: it never checks
out, stashes, or writes anything.

Checks
  C1 json-invariants      card JSONs parse, keys/counts/Japanese fields frozen
  C2 thai-digits          no U+0E50..U+0E59 under src/ overlay/ patch/ docs/
  C3 xaml-structure       src/*.xaml parses; Tag and SelectedIndex multisets frozen
  C4 placeholders         changed strings keep their {...} placeholder multiset
  C5 dialog-filters       OpenFileDialog filter literals keep their '|' count
  C6 config-tokens        per-file counts of windowed/borderless/... frozen,
                          counted only as whole quoted values, never as prose
  C7 resolution-captions  strings matching the WxH regex still match
  C8 encoding-bom         overlay/ decodes as UTF-8; every .ps1 keeps its
                          baseline BOM state (added and removed both fail)
  C9 ps1-ansi-hazard      no non-ASCII added to a .ps1 that is BOM-less at
                          baseline (PowerShell 5.1 would read it as ANSI)

Note on C8: six of the nine .ps1 files carry a UTF-8 BOM and three
(patch/Apply-EN-Patch.ps1, patch/Build-Manifest.ps1, src/assets/extract-icon.ps1)
do not.  So the invariant is "unchanged from baseline", not "always present";
C9 is what actually guards against mojibake in the BOM-less three.

Usage
  python3 tools/check-thai.py [--base HEAD] [-v]
"""

from __future__ import annotations

import argparse
import bisect
import json
import os
import re
import subprocess
import sys
from collections import Counter
from xml.parsers import expat

# --------------------------------------------------------------------------
# constants
# --------------------------------------------------------------------------

CARD_NAMES = "src/FGOLocalPlatform.CardNames.json"
CRAFT_EFFECTS = "src/FGOLocalPlatform.CraftEffects.json"
EXPECTED_COUNTS = {CARD_NAMES: 1384, CRAFT_EFFECTS: 1264}
FROZEN_JSON_FIELDS = ("Japanese", "NormalJapanese", "MaximumJapanese")

THAI_DIGIT_RE = re.compile("[\u0e50-\u0e59]")
THAI_DIGIT_ROOTS = ("src/", "overlay/", "patch/", "docs/")

CONFIG_TOKENS = ("windowed", "borderless", "exclusive", "keyboard", "xinput", "16:9")

# A config token only counts when it is the WHOLE content of a quoted value --
# Tag="keyboard", "keyboard", 'keyboard'.  The same word inside a longer quoted
# sentence ("The shared keyboard buttons work with every input method...") is
# ordinary prose that must be free to become Thai, and is never counted.
QUOTED_DOUBLE_RE = re.compile(r'"([^"\n]*)"')
QUOTED_SINGLE_RE = re.compile(r"'([^'\n]*)'")

RESOLUTION_RE = re.compile(r"^\s*(\d{3,4})\s*[xX\u00d7]\s*(\d{3,4})\s*$")

UTF8_BOM = b"\xef\xbb\xbf"

# Files we never try to decode as text.
BINARY_SUFFIXES = {
    ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".pdf", ".zip",
    ".dll", ".exe", ".farc", ".bin", ".woff", ".woff2", ".ttf", ".otf",
}


# --------------------------------------------------------------------------
# reporting
# --------------------------------------------------------------------------

class Report:
    """Collects violations and method notes; one printed line each."""

    def __init__(self, verbose: bool = False) -> None:
        self.violations: list[str] = []
        self.notes: list[str] = []
        self.verbose = verbose

    def fail(self, check: str, path: str, what: str,
             expected: object = None, actual: object = None) -> None:
        line = "VIOLATION [%s] %s: %s" % (check, path, what)
        if expected is not None or actual is not None:
            line += "; expected %s, actual %s" % (_short(expected), _short(actual))
        self.violations.append(line)
        print(line)

    def note(self, check: str, message: str) -> None:
        line = "NOTE [%s] %s" % (check, message)
        self.notes.append(line)
        print(line)

    def method(self, check: str, message: str) -> None:
        """Always printed: how a check compared, so the result is auditable."""
        print("METHOD [%s] %s" % (check, message))

    def info(self, message: str) -> None:
        if self.verbose:
            print("INFO %s" % message)


def _short(value: object, limit: int = 160) -> str:
    text = value if isinstance(value, str) else repr(value)
    text = text.replace("\r", "\\r").replace("\n", "\\n").replace("\t", "\\t")
    if len(text) > limit:
        text = text[: limit - 3] + "..."
    return text


# --------------------------------------------------------------------------
# git plumbing (read-only)
# --------------------------------------------------------------------------

class Repo:
    """Read-only access to the working tree and a baseline git ref."""

    def __init__(self, root: str, ref: str) -> None:
        self.root = root
        self.ref = ref
        self._base_cache: dict[str, bytes | None] = {}
        self._work_cache: dict[str, bytes | None] = {}
        self.base_files = self._list_base()
        self.work_files = self._list_work()

    # -- process helpers ---------------------------------------------------

    def _git(self, *args: str) -> bytes:
        proc = subprocess.run(
            ["git", "-C", self.root, *args],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        )
        if proc.returncode != 0:
            raise RuntimeError(
                "git %s failed: %s"
                % (" ".join(args), proc.stderr.decode("utf-8", "replace").strip())
            )
        return proc.stdout

    def _list_base(self) -> set[str]:
        out = self._git("ls-tree", "-r", "-z", "--name-only", self.ref)
        return {name for name in out.decode("utf-8").split("\0") if name}

    def _list_work(self) -> set[str]:
        out = self._git("ls-files", "-z", "--cached", "--others", "--exclude-standard")
        names = {name for name in out.decode("utf-8").split("\0") if name}
        return {name for name in names if os.path.isfile(os.path.join(self.root, name))}

    # -- content -----------------------------------------------------------

    def base_bytes(self, path: str) -> bytes | None:
        """Baseline blob content.  git stores LF; callers normalise."""
        if path not in self._base_cache:
            if path not in self.base_files:
                self._base_cache[path] = None
            else:
                proc = subprocess.run(
                    ["git", "-C", self.root, "cat-file", "blob", "%s:%s" % (self.ref, path)],
                    stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                )
                self._base_cache[path] = proc.stdout if proc.returncode == 0 else None
        return self._base_cache[path]

    def work_bytes(self, path: str) -> bytes | None:
        if path not in self._work_cache:
            full = os.path.join(self.root, path)
            try:
                with open(full, "rb") as handle:
                    self._work_cache[path] = handle.read()
            except OSError:
                self._work_cache[path] = None
        return self._work_cache[path]

    # -- convenience -------------------------------------------------------

    def both_text(self, path: str) -> tuple[str | None, str | None]:
        """(baseline, worktree) as newline-normalised str, or None if absent/binary."""
        return decode(self.base_bytes(path)), decode(self.work_bytes(path))


def decode(raw: bytes | None) -> str | None:
    """UTF-8 decode, drop a leading BOM, normalise CRLF/CR to LF.

    The repo checks out CRLF (.gitattributes `* text=auto eol=crlf`) while git
    stores LF, so every baseline-vs-worktree text comparison must normalise or
    every single file would look changed.
    """
    if raw is None:
        return None
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        return None
    if text.startswith("\ufeff"):
        text = text[1:]
    return text.replace("\r\n", "\n").replace("\r", "\n")


def is_binary_path(path: str) -> bool:
    return os.path.splitext(path)[1].lower() in BINARY_SUFFIXES


# --------------------------------------------------------------------------
# placeholder extraction
# --------------------------------------------------------------------------

def brace_placeholders(text: str) -> list[str]:
    """Multiset (as an ordered list) of `{...}` groups, honouring {{ and }}."""
    out: list[str] = []
    i, n = 0, len(text)
    while i < n:
        char = text[i]
        if char == "{":
            if i + 1 < n and text[i + 1] == "{":
                i += 2
                continue
            depth, j = 0, i
            while j < n:
                if text[j] == "{":
                    depth += 1
                elif text[j] == "}":
                    depth -= 1
                    if depth == 0:
                        j += 1
                        break
                j += 1
            out.append(text[i:j])
            i = j
        elif char == "}" and i + 1 < n and text[i + 1] == "}":
            i += 2
        else:
            i += 1
    return out


# --------------------------------------------------------------------------
# C# string literal scanner
# --------------------------------------------------------------------------

class StringLit:
    __slots__ = ("line", "text", "interp", "placeholders")

    def __init__(self, line: int, text: str, interp: bool, placeholders: list[str]) -> None:
        self.line = line
        self.text = text
        self.interp = interp
        self.placeholders = placeholders


def _scan_char_literal(src: str, i: int) -> int:
    n = len(src)
    i += 1
    while i < n:
        if src[i] == "\\":
            i += 2
            continue
        if src[i] == "'":
            return i + 1
        if src[i] == "\n":
            return i
        i += 1
    return n


def _scan_hole(src: str, i: int) -> int:
    """src[i] == '{' inside an interpolated string; return index past its '}'."""
    n = len(src)
    depth, j = 0, i
    while j < n:
        char = src[j]
        if char == "{":
            depth += 1
            j += 1
            continue
        if char == "}":
            depth -= 1
            j += 1
            if depth == 0:
                return j
            continue
        if char == '"':
            interp, verbatim = False, False
            k = j - 1
            while k >= 0 and src[k] in "$@":
                if src[k] == "$":
                    interp = True
                else:
                    verbatim = True
                k -= 1
            _, j, _ = _scan_string(src, j, interp, verbatim)
            continue
        if char == "'":
            j = _scan_char_literal(src, j)
            continue
        j += 1
    return n


def _scan_string(src: str, i: int, interp: bool, verbatim: bool) -> tuple[str, int, list[str]]:
    """src[i] == '"'.  Returns (content, index past closing quote, holes)."""
    n = len(src)
    i += 1
    parts: list[str] = []
    holes: list[str] = []
    while i < n:
        char = src[i]
        if not verbatim and char == "\\":
            parts.append(src[i:i + 2])
            i += 2
            continue
        if char == '"':
            if verbatim and i + 1 < n and src[i + 1] == '"':
                parts.append('""')
                i += 2
                continue
            i += 1
            break
        if not verbatim and char == "\n":
            # unterminated literal; bail out rather than eating the file
            break
        if interp and char == "{":
            if i + 1 < n and src[i + 1] == "{":
                parts.append("{{")
                i += 2
                continue
            end = _scan_hole(src, i)
            parts.append(src[i:end])
            holes.append(src[i:end])
            i = end
            continue
        if interp and char == "}" and i + 1 < n and src[i + 1] == "}":
            parts.append("}}")
            i += 2
            continue
        parts.append(char)
        i += 1
    return "".join(parts), i, holes


def cs_strings(src: str) -> list[StringLit]:
    """All C# string literals in source order, skipping comments and chars."""
    newlines = [m.start() for m in re.finditer("\n", src)]

    def line_of(idx: int) -> int:
        return bisect.bisect_right(newlines, idx) + 1

    out: list[StringLit] = []
    i, n = 0, len(src)
    while i < n:
        char = src[i]
        if char == "/" and i + 1 < n:
            if src[i + 1] == "/":
                j = src.find("\n", i)
                i = n if j < 0 else j + 1
                continue
            if src[i + 1] == "*":
                j = src.find("*/", i + 2)
                i = n if j < 0 else j + 2
                continue
        if char == "'":
            i = _scan_char_literal(src, i)
            continue
        if char in "$@":
            j = i
            interp = verbatim = False
            while j < n and src[j] in "$@":
                if src[j] == "$":
                    interp = True
                else:
                    verbatim = True
                j += 1
            if j < n and src[j] == '"':
                text, end, holes = _scan_string(src, j, interp, verbatim)
                out.append(StringLit(line_of(i), text, interp,
                                     holes if interp else brace_placeholders(text)))
                i = end
                continue
            i = j if j > i else i + 1
            continue
        if char == '"':
            text, end, _ = _scan_string(src, i, False, False)
            out.append(StringLit(line_of(i), text, False, brace_placeholders(text)))
            i = end
            continue
        i += 1
    return out


# --------------------------------------------------------------------------
# XAML scanner
# --------------------------------------------------------------------------

class XamlDoc:
    def __init__(self) -> None:
        self.strings: list[StringLit] = []
        self.tags: Counter[str] = Counter()
        self.selected: Counter[str] = Counter()


def parse_xaml(text: str) -> XamlDoc:
    """Parse XAML with expat.  Raises expat.ExpatError when malformed."""
    doc = XamlDoc()
    parser = expat.ParserCreate()
    parser.ordered_attributes = True
    parser.buffer_text = True
    buf: list[str] = []
    buf_line = [1]

    def flush() -> None:
        joined = "".join(buf)
        del buf[:]
        if joined.strip():
            doc.strings.append(
                StringLit(buf_line[0], joined, False, brace_placeholders(joined))
            )

    def start(_name: str, attrs: list[str]) -> None:
        flush()
        line = parser.CurrentLineNumber
        for k in range(0, len(attrs), 2):
            name, value = attrs[k], attrs[k + 1]
            doc.strings.append(StringLit(line, value, False, brace_placeholders(value)))
            if name == "Tag":
                doc.tags[value] += 1
            elif name == "SelectedIndex":
                doc.selected[value] += 1

    def end(_name: str) -> None:
        flush()

    def chars(data: str) -> None:
        if not buf:
            buf_line[0] = parser.CurrentLineNumber
        buf.append(data)

    parser.StartElementHandler = start
    parser.EndElementHandler = end
    parser.CharacterDataHandler = chars
    parser.Parse(text.encode("utf-8"), True)
    flush()
    return doc


# --------------------------------------------------------------------------
# check 1 -- card JSON invariants
# --------------------------------------------------------------------------

def check_json(repo: Repo, rep: Report) -> None:
    tag = "C1 json-invariants"
    for path, expected_count in EXPECTED_COUNTS.items():
        work_raw = repo.work_bytes(path)
        if work_raw is None:
            rep.fail(tag, path, "file is missing from the working tree")
            continue
        work_text = decode(work_raw)
        if work_text is None:
            rep.fail(tag, path, "file does not decode as UTF-8")
            continue
        try:
            work = json.loads(work_text)
        except json.JSONDecodeError as exc:
            rep.fail(tag, path, "does not parse as JSON (%s)" % exc)
            continue
        if not isinstance(work, dict):
            rep.fail(tag, path, "top level is not a JSON object",
                     "object", type(work).__name__)
            continue

        if len(work) != expected_count:
            rep.fail(tag, path, "entry count changed", expected_count, len(work))

        base_text = decode(repo.base_bytes(path))
        if base_text is None:
            rep.fail(tag, path, "not present in baseline %s; cannot diff" % repo.ref)
            continue
        try:
            base = json.loads(base_text)
        except json.JSONDecodeError as exc:
            rep.fail(tag, path, "baseline does not parse as JSON (%s)" % exc)
            continue

        base_keys, work_keys = set(base), set(work)
        if base_keys != work_keys:
            missing = sorted(base_keys - work_keys)
            added = sorted(work_keys - base_keys)
            if missing:
                rep.fail(tag, path, "%d baseline key(s) removed" % len(missing),
                         "all baseline keys present", "missing " + ", ".join(missing[:8]))
            if added:
                rep.fail(tag, path, "%d key(s) added" % len(added),
                         "no new keys", "added " + ", ".join(added[:8]))

        for key in sorted(base_keys & work_keys):
            bval, wval = base[key], work[key]
            if not isinstance(bval, dict) or not isinstance(wval, dict):
                continue
            for field in FROZEN_JSON_FIELDS:
                if field not in bval:
                    continue
                if field not in wval:
                    rep.fail(tag, path, "%s.%s was removed" % (key, field),
                             bval[field], "<absent>")
                elif wval[field] != bval[field]:
                    rep.fail(tag, path, "%s.%s changed" % (key, field),
                             bval[field], wval[field])


# --------------------------------------------------------------------------
# check 2 -- Thai digits
# --------------------------------------------------------------------------

def check_thai_digits(repo: Repo, rep: Report) -> None:
    tag = "C2 thai-digits"
    for path in sorted(repo.work_files):
        if not path.startswith(THAI_DIGIT_ROOTS):
            continue
        if is_binary_path(path):
            continue
        text = decode(repo.work_bytes(path))
        if text is None:
            continue  # not UTF-8 text; check 8 owns overlay/, others are binary
        for lineno, line in enumerate(text.split("\n"), 1):
            found = THAI_DIGIT_RE.findall(line)
            if found:
                points = " ".join("U+%04X" % ord(c) for c in sorted(set(found)))
                rep.fail(tag, "%s:%d" % (path, lineno),
                         "Thai digit(s) %s present" % points,
                         "Arabic numerals only", _short(line.strip(), 80))


# --------------------------------------------------------------------------
# check 3 -- XAML structure
# --------------------------------------------------------------------------

def _xaml_paths(repo: Repo) -> list[str]:
    return sorted(
        p for p in repo.work_files
        if p.startswith("src/") and p.endswith(".xaml") and p.count("/") == 1
    )


def check_xaml(repo: Repo, rep: Report) -> dict[str, tuple[XamlDoc | None, XamlDoc | None]]:
    """Parse every src/*.xaml, compare Tag/SelectedIndex multisets.

    Returns the parsed docs so checks 4 and 7 can reuse them.
    """
    tag = "C3 xaml-structure"
    docs: dict[str, tuple[XamlDoc | None, XamlDoc | None]] = {}
    for path in _xaml_paths(repo):
        base_text, work_text = repo.both_text(path)
        work_doc = base_doc = None
        if work_text is None:
            rep.fail(tag, path, "does not decode as UTF-8")
        else:
            try:
                work_doc = parse_xaml(work_text)
            except expat.ExpatError as exc:
                rep.fail(tag, path, "does not parse as XML (%s)" % exc)
        if base_text is not None:
            try:
                base_doc = parse_xaml(base_text)
            except expat.ExpatError as exc:
                rep.fail(tag, path, "baseline does not parse as XML (%s)" % exc)
        docs[path] = (base_doc, work_doc)

        if base_doc is None or work_doc is None:
            continue
        for label, battr, wattr in (
            ("Tag", base_doc.tags, work_doc.tags),
            ("SelectedIndex", base_doc.selected, work_doc.selected),
        ):
            if battr != wattr:
                lost = battr - wattr
                gained = wattr - battr
                detail = []
                if lost:
                    detail.append("lost " + ", ".join(
                        "%r x%d" % (k, v) for k, v in sorted(lost.items())))
                if gained:
                    detail.append("gained " + ", ".join(
                        "%r x%d" % (k, v) for k, v in sorted(gained.items())))
                rep.fail(tag, path, "%s attribute multiset changed" % label,
                         "%d value(s) as in baseline" % sum(battr.values()),
                         "; ".join(detail))
    return docs


# --------------------------------------------------------------------------
# checks 4 and 7 -- placeholders and resolution captions in changed strings
# --------------------------------------------------------------------------

def _cs_paths(repo: Repo) -> list[str]:
    return sorted(p for p in repo.work_files if p.startswith("src/") and p.endswith(".cs"))


def _collect_strings(
    repo: Repo,
    docs: dict[str, tuple[XamlDoc | None, XamlDoc | None]],
) -> dict[str, tuple[list[StringLit] | None, list[StringLit] | None]]:
    """Per-file (baseline strings, worktree strings) for src/**/*.cs + src/*.xaml."""
    out: dict[str, tuple[list[StringLit] | None, list[StringLit] | None]] = {}
    for path in _cs_paths(repo):
        base_text, work_text = repo.both_text(path)
        base = cs_strings(base_text) if base_text is not None else None
        work = cs_strings(work_text) if work_text is not None else None
        out[path] = (base, work)
    for path, (base_doc, work_doc) in docs.items():
        out[path] = (
            base_doc.strings if base_doc is not None else None,
            work_doc.strings if work_doc is not None else None,
        )
    return out


def check_placeholders_and_resolutions(
    repo: Repo, rep: Report,
    strings: dict[str, tuple[list[StringLit] | None, list[StringLit] | None]],
) -> None:
    ph_tag = "C4 placeholders"
    res_tag = "C7 resolution-captions"
    positional = fallback = 0

    for path in sorted(strings):
        base, work = strings[path]
        if work is None:
            continue
        if base is None:
            rep.note(ph_tag, "%s is new in the working tree; nothing to diff against" % path)
            continue

        if len(base) == len(work):
            positional += 1
            for idx, (bs, ws) in enumerate(zip(base, work)):
                if bs.text != ws.text:
                    bph, wph = Counter(bs.placeholders), Counter(ws.placeholders)
                    if bph != wph:
                        rep.fail(ph_tag, "%s:%d" % (path, ws.line),
                                 "string #%d changed and its {...} placeholders changed" % idx,
                                 sorted(bph.elements()), sorted(wph.elements()))
                if RESOLUTION_RE.match(bs.text) and not RESOLUTION_RE.match(ws.text):
                    rep.fail(res_tag, "%s:%d" % (path, ws.line),
                             "string #%d no longer matches the resolution pattern" % idx,
                             bs.text, ws.text)
        else:
            fallback += 1
            rep.note(ph_tag,
                     "%s: string count changed (%d -> %d); positional matching not possible, "
                     "falling back to whole-file placeholder multiset comparison"
                     % (path, len(base), len(work)))
            bph = Counter(p for s in base for p in s.placeholders)
            wph = Counter(p for s in work for p in s.placeholders)
            if bph != wph:
                lost, gained = bph - wph, wph - bph
                detail = []
                if lost:
                    detail.append("lost " + ", ".join(
                        "%s x%d" % (k, v) for k, v in sorted(lost.items())))
                if gained:
                    detail.append("gained " + ", ".join(
                        "%s x%d" % (k, v) for k, v in sorted(gained.items())))
                rep.fail(ph_tag, path,
                         "whole-file {...} placeholder multiset changed (fallback method)",
                         "%d placeholder(s) as in baseline" % sum(bph.values()),
                         "; ".join(detail))

            base_res = Counter(s.text for s in base if RESOLUTION_RE.match(s.text))
            work_res = Counter(s.text for s in work if RESOLUTION_RE.match(s.text))
            missing = base_res - work_res
            if missing:
                rep.fail(res_tag, path,
                         "resolution caption(s) no longer present (fallback method)",
                         ", ".join("%r x%d" % (k, v) for k, v in sorted(base_res.items())),
                         "missing " + ", ".join(
                             "%r x%d" % (k, v) for k, v in sorted(missing.items())))

    rep.method(ph_tag, "%d file(s) compared string-by-string (positional), "
                       "%d file(s) fell back to whole-file placeholder multisets"
                       % (positional, fallback))


# --------------------------------------------------------------------------
# check 5 -- OpenFileDialog filter separators
# --------------------------------------------------------------------------

def check_dialog_filters(
    repo: Repo, rep: Report,
    strings: dict[str, tuple[list[StringLit] | None, list[StringLit] | None]],
) -> None:
    tag = "C5 dialog-filters"
    checked: list[str] = []
    for path in sorted(strings):
        if not (path.endswith(".cs") or path.endswith(".xaml")):
            continue
        base, work = strings[path]
        if base is None or work is None:
            continue
        base_text, work_text = repo.both_text(path)
        if "OpenFileDialog" not in (base_text or "") and "OpenFileDialog" not in (work_text or ""):
            continue
        checked.append(path)

        if len(base) == len(work):
            # Positional: compare '|' counts string-by-string.
            for idx, (bs, ws) in enumerate(zip(base, work)):
                bc, wc = bs.text.count("|"), ws.text.count("|")
                if bc and bc != wc:
                    rep.fail(tag, "%s:%d" % (path, ws.line),
                             "string #%d changed its '|' separator count" % idx,
                             "%d '|' in %r" % (bc, _short(bs.text, 60)),
                             "%d '|' in %r" % (wc, _short(ws.text, 60)))
        else:
            total_b = sum(s.text.count("|") for s in base)
            total_w = sum(s.text.count("|") for s in work)
            rep.note(tag, "%s: string count changed; using whole-file '|'-in-literals count" % path)
            if total_b != total_w:
                rep.fail(tag, path,
                         "whole-file '|' count inside string literals changed (fallback method)",
                         total_b, total_w)
    rep.method(tag, "per-string '|' counts over the literals of %s "
                    "(falls back to a whole-file '|'-in-literals count when a file's "
                    "string count moved)"
                    % (", ".join(checked) if checked else "<no OpenFileDialog file found>"))


# --------------------------------------------------------------------------
# check 6 -- config tokens
# --------------------------------------------------------------------------

def count_quoted_token(text: str, token: str) -> int:
    """Occurrences of `token` as the ENTIRE content of a quoted value.

    Matches Tag="keyboard", "keyboard" and 'keyboard'.  Does not match the word
    inside a longer quoted sentence, which is prose and must stay translatable.
    Double- and single-quoted regions are scanned independently so an apostrophe
    in prose cannot swallow a neighbouring double-quoted value.
    """
    total = 0
    for pattern in (QUOTED_DOUBLE_RE, QUOTED_SINGLE_RE):
        for match in pattern.finditer(text):
            if match.group(1) == token:
                total += 1
    return total


def check_config_tokens(repo: Repo, rep: Report) -> None:
    tag = "C6 config-tokens"
    rep.method(tag, "case-sensitive counts of %s per text file, counted only where the "
                    "token is the whole content of a quoted value (Tag=\"keyboard\", "
                    "\"keyboard\", 'keyboard'); the same word inside a longer quoted "
                    "sentence is prose and is never counted"
                    % ", ".join(repr(t) for t in CONFIG_TOKENS))
    for path in sorted(repo.work_files & repo.base_files):
        if is_binary_path(path):
            continue
        base_text, work_text = repo.both_text(path)
        if base_text is None or work_text is None:
            continue
        for token in CONFIG_TOKENS:
            bc = count_quoted_token(base_text, token)
            wc = count_quoted_token(work_text, token)
            if bc != wc:
                rep.fail(tag, path,
                         "count of config token %r as a whole quoted value changed" % token,
                         bc, wc)


# --------------------------------------------------------------------------
# check 8 -- encoding and BOM state
# --------------------------------------------------------------------------

def check_encoding(repo: Repo, rep: Report) -> None:
    tag = "C8 encoding-bom"
    for path in sorted(repo.work_files):
        if not path.startswith("overlay/"):
            continue
        if is_binary_path(path):
            continue
        raw = repo.work_bytes(path)
        if raw is None:
            continue
        try:
            raw.decode("utf-8")
        except UnicodeDecodeError as exc:
            rep.fail(tag, path, "does not decode as UTF-8 (byte 0x%02X at offset %d)"
                     % (raw[exc.start], exc.start), "valid UTF-8", exc.reason)

    rep.method(tag, "every .ps1 must keep the BOM state it has at baseline %s; six of "
                    "the nine carry EF BB BF and three do not, so adding a BOM and "
                    "removing one are both violations" % repo.ref)
    for path in sorted(repo.work_files):
        if not path.endswith(".ps1"):
            continue
        raw = repo.work_bytes(path)
        if raw is None:
            continue
        has_bom = raw.startswith(UTF8_BOM)
        base_raw = repo.base_bytes(path)
        if base_raw is None:
            rep.note(tag, "%s is new in the working tree; no baseline BOM state to "
                          "compare (BOM %s) -- C9 still applies"
                     % (path, "present" if has_bom else "absent"))
            continue
        had_bom = base_raw.startswith(UTF8_BOM)
        if had_bom and not has_bom:
            rep.fail(tag, path, "UTF-8 BOM was REMOVED "
                     "(PowerShell 5.1 would read it as ANSI and mojibake Thai)",
                     "EF BB BF", raw[:3].hex(" ").upper() or "<empty>")
        elif has_bom and not had_bom:
            rep.fail(tag, path, "UTF-8 BOM was ADDED to a file that is BOM-less at baseline",
                     base_raw[:3].hex(" ").upper() or "<empty>", "EF BB BF")


# --------------------------------------------------------------------------
# check 9 -- non-ASCII smuggled into a BOM-less .ps1
# --------------------------------------------------------------------------

def check_ps1_ansi_hazard(repo: Repo, rep: Report) -> None:
    """The hazard the BOM rule exists to catch.

    The three BOM-less .ps1 files are pure ASCII today, so PowerShell 5.1's
    ANSI fallback is harmless.  The moment one of them gains a non-ASCII byte
    without a BOM, its output mojibakes on Windows.
    """
    tag = "C9 ps1-ansi-hazard"
    for path in sorted(repo.work_files):
        if not path.endswith(".ps1"):
            continue
        raw = repo.work_bytes(path)
        if raw is None:
            continue
        if raw.startswith(UTF8_BOM):
            continue  # a BOM makes the file safe; C8 reports the state change
        base_raw = repo.base_bytes(path)
        if base_raw is not None and base_raw.startswith(UTF8_BOM):
            continue  # C8 already reports the stripped BOM
        offenders = [(i, b) for i, b in enumerate(raw) if b >= 0x80]
        if not offenders:
            continue
        offset, byte = offenders[0]
        lineno = raw[:offset].count(b"\n") + 1
        rep.fail(tag, path,
                 "non-ASCII added to a BOM-less .ps1 -- PowerShell 5.1 will read this "
                 "as ANSI and mojibake it. Add a UTF-8 BOM or revert",
                 "pure ASCII (0 byte(s) >= 0x80)",
                 "%d byte(s) >= 0x80, first 0x%02X at offset %d (line %d)"
                 % (len(offenders), byte, offset, lineno))


# --------------------------------------------------------------------------
# main
# --------------------------------------------------------------------------

def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="check-thai.py",
        description="Thai localisation invariant checker for FGOAC scooby.",
    )
    parser.add_argument("--base", default="HEAD", metavar="REF",
                        help="baseline git ref to diff against (default: HEAD)")
    parser.add_argument("-v", "--verbose", action="store_true",
                        help="print extra per-file detail")
    args = parser.parse_args(argv)

    try:
        root = subprocess.run(
            ["git", "rev-parse", "--show-toplevel"],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True,
        ).stdout.decode("utf-8").strip()
    except (subprocess.CalledProcessError, FileNotFoundError) as exc:
        print("ERROR not inside a git work tree (%s)" % exc, file=sys.stderr)
        return 2

    rep = Report(verbose=args.verbose)
    try:
        repo = Repo(root, args.base)
    except RuntimeError as exc:
        print("ERROR %s" % exc, file=sys.stderr)
        return 2

    check_json(repo, rep)
    check_thai_digits(repo, rep)
    docs = check_xaml(repo, rep)
    strings = _collect_strings(repo, docs)
    check_placeholders_and_resolutions(repo, rep, strings)
    check_dialog_filters(repo, rep, strings)
    check_config_tokens(repo, rep)
    check_encoding(repo, rep)
    check_ps1_ansi_hazard(repo, rep)

    if rep.violations:
        print("FAIL %d violation(s) against baseline %s" % (len(rep.violations), args.base))
        return 1
    print("OK 9/9 checks clean against baseline %s (%d worktree file(s) inspected)"
          % (args.base, len(repo.work_files)))
    return 0


if __name__ == "__main__":
    sys.exit(main())

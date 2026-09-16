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
  C4 placeholders         changed strings keep their {...} placeholder multiset.
                          Inside StringFormat=, and inside a C# interpolation
                          expression, only the structure is compared, because
                          the literal text there renders on screen as prose.
     contract-literal     but a literal that is PARSED rather than displayed
                          (.Equals/.Contains/.StartsWith/.EndsWith/.IndexOf
                          argument, == or != operand, .Replace search term,
                          case label) must stay byte-identical
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

def _split_top_level_commas(body: str) -> list[str]:
    """Split a markup-extension body on commas that are not inside nested {}."""
    parts: list[str] = []
    depth = 0
    cur: list[str] = []
    for char in body:
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
        if char == "," and depth == 0:
            parts.append("".join(cur))
            cur = []
        else:
            cur.append(char)
    parts.append("".join(cur))
    return parts


# A trailing unit is a short Latin/percent run right after the last format
# token -- "px", "%", "ms".  Anything longer or further along is prose.
UNIT_RE = re.compile(r"^[ \t]?([%A-Za-z]{1,4})(?=$|[\s(,.])")


def placeholder_signature(placeholder: str) -> str:
    """Reduce a `{...}` placeholder to the part that must stay byte-identical.

    A markup extension carrying a StringFormat renders its literal text on
    screen, so that text is translatable prose.  Its signature keeps:
      * the extension name and every Name=Value pair (ElementName=, Path=, ...)
      * the inner {...} format tokens of the StringFormat value, in order
      * any trailing unit such as px or %
    and drops the human-readable text around those tokens.

    A C# interpolation hole renders the result of an expression, and a string
    literal embedded in that expression is usually display text.  Its signature
    keeps the whole expression -- identifiers, member access, operators,
    argument counts, format specifiers such as :D5, and the number and position
    of the embedded literals -- but blanks each literal's contents.  Literals
    sitting in a comparison position are a separate, sharper check: see
    check_contract_literals.

    Everything else -- {StaticResource ...} and a StringFormat whose value is a
    bare .NET format string such as F1 -- is compared verbatim.
    """
    if not (placeholder.startswith("{") and placeholder.endswith("}")):
        return placeholder
    body = placeholder[1:-1]
    if "StringFormat" not in body:
        if '"' in body:
            return "{" + _blank_literals_in_code(body) + "}"
        return placeholder

    reduced: list[str] = []
    narrowed = False
    for part in _split_top_level_commas(body):
        stripped = part.strip()
        if not stripped.startswith("StringFormat="):
            reduced.append(stripped)
            continue
        value = stripped[len("StringFormat="):]
        tokens = brace_placeholders(value)
        if not tokens:
            # e.g. StringFormat=F1 -- a .NET format specifier, not a template.
            reduced.append(stripped)
            continue
        narrowed = True
        last = value.rfind("}")
        tail = value[last + 1:] if last >= 0 else ""
        match = UNIT_RE.match(tail)
        unit = match.group(1) if match else ""
        reduced.append("StringFormat=" + "|".join(tokens) + (("|" + unit) if unit else ""))

    if not narrowed:
        return placeholder
    return "{" + ",".join(reduced) + "}"


def signature_counter(placeholders: list[str]) -> Counter[str]:
    return Counter(placeholder_signature(p) for p in placeholders)


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
            _, j, _, _ = _scan_string(src, j, interp, verbatim)
            continue
        if char == "'":
            j = _scan_char_literal(src, j)
            continue
        j += 1
    return n


def _scan_string(src: str, i: int, interp: bool,
                 verbatim: bool) -> tuple[str, int, list[str], list[tuple[int, int]]]:
    """src[i] == '"'.

    Returns (content, index past closing quote, holes, hole spans).  The spans
    are absolute (start, end) offsets into `src` for each `{...}` hole, so a
    caller can recurse into the C# expression a hole contains.
    """
    n = len(src)
    i += 1
    parts: list[str] = []
    holes: list[str] = []
    spans: list[tuple[int, int]] = []
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
            spans.append((i, end))
            i = end
            continue
        if interp and char == "}" and i + 1 < n and src[i + 1] == "}":
            parts.append("}}")
            i += 2
            continue
        parts.append(char)
        i += 1
    return "".join(parts), i, holes, spans


# --------------------------------------------------------------------------
# contract literals: a string that is parsed rather than displayed
# --------------------------------------------------------------------------

# A literal handed to one of these is matched against, never shown.
CONTRACT_METHODS = ("Equals", "Contains", "StartsWith", "EndsWith", "IndexOf")
# .Replace(search, replacement): the search term is a contract, the
# replacement is display text and is free to be translated.
SEARCH_ARG_METHODS = ("Replace",)

_IDENT_TAIL_RE = re.compile(r"([A-Za-z_][A-Za-z0-9_]*)\s*$")
_CASE_TAIL_RE = re.compile(r"(?:^|[^A-Za-z0-9_])case\s*$")
_RECEIVER_HEAD_RE = re.compile(
    r"^\.\s*(" + "|".join(CONTRACT_METHODS + SEARCH_ARG_METHODS) + r")\s*\(")
# How far around a literal we look for ==, !=, case and =>.
_CONTEXT_WINDOW = 80


def _blank_literals_in_code(code: str) -> str:
    """Replace every string literal's contents with a marker, keeping structure.

    `x.Equals("HOLO") ? "Fatal Foil" : "Normal"` becomes
    `x.Equals("<str>") ? "<str>" : "<str>"`, so translating display text does
    not read as a structural change while the shape of the expression, the
    argument count and the literal positions all still compare.
    """
    out: list[str] = []
    i, n = 0, len(code)
    while i < n:
        char = code[i]
        if char == "'":
            j = _scan_char_literal(code, i)
            out.append(code[i:j])  # char literals are structural, keep verbatim
            i = j
            continue
        if char == '"' or char in "$@":
            j = i
            interp = verbatim = False
            while j < n and code[j] in "$@":
                if code[j] == "$":
                    interp = True
                else:
                    verbatim = True
                j += 1
            if j < n and code[j] == '"':
                _, end, _, _ = _scan_string(code, j, interp, verbatim)
                out.append('"<str>"')
                i = end
                continue
            out.append(code[i:j] if j > i else char)
            i = j if j > i else i + 1
            continue
        out.append(char)
        i += 1
    return "".join(out)


def _method_before_paren(src: str, paren: int, low: int) -> str | None:
    """Name of the member call whose argument list opens at `paren`, if any."""
    start = max(low, paren - _CONTEXT_WINDOW)
    match = _IDENT_TAIL_RE.search(src[start:paren])
    if not match:
        return None
    ident_start = start + match.start(1)
    before = src[start:ident_start].rstrip()
    if not before.endswith("."):
        return None
    return match.group(1)


class CsLiteral:
    __slots__ = ("line", "content", "contract", "reason")

    def __init__(self, line: int, content: str, contract: bool, reason: str) -> None:
        self.line = line
        self.content = content
        self.contract = contract
        self.reason = reason


def _classify_literal(src: str, lit_start: int, lit_end: int,
                      stack: list[list], low: int, high: int) -> tuple[bool, str]:
    """Is this literal parsed (a contract) or merely displayed?"""
    if stack:
        method, argidx = stack[-1]
        if method in CONTRACT_METHODS:
            return True, "argument to .%s(" % method
        if method in SEARCH_ARG_METHODS and argidx == 0:
            return True, "search argument of .%s(" % method

    before = src[max(low, lit_start - _CONTEXT_WINDOW):lit_start].rstrip()
    if before.endswith("==") or before.endswith("!="):
        return True, "operand of %s" % before[-2:]
    if _CASE_TAIL_RE.search(before):
        return True, "case label"

    after = src[lit_end:min(high, lit_end + _CONTEXT_WINDOW)].lstrip()
    if after.startswith("==") or after.startswith("!="):
        return True, "operand of %s" % after[:2]
    if after.startswith("=>"):
        return True, "switch arm pattern"
    match = _RECEIVER_HEAD_RE.match(after)
    if match:
        return True, "receiver of .%s(" % match.group(1)

    return False, "display text"


def scan_cs_literals(src: str) -> list[CsLiteral]:
    """Every string literal in a C# file, in source order, classified.

    Recurses into interpolation holes, so a literal embedded in an expression
    is classified by the code that actually surrounds it.
    """
    newlines = [m.start() for m in re.finditer("\n", src)]

    def line_of(idx: int) -> int:
        return bisect.bisect_right(newlines, idx) + 1

    out: list[CsLiteral] = []

    def scan_code(low: int, high: int) -> None:
        stack: list[list] = []
        i = low
        while i < high:
            char = src[i]
            if char == "/" and i + 1 < high:
                if src[i + 1] == "/":
                    j = src.find("\n", i)
                    i = high if j < 0 or j >= high else j + 1
                    continue
                if src[i + 1] == "*":
                    j = src.find("*/", i + 2)
                    i = high if j < 0 or j >= high else j + 2
                    continue
            if char == "'":
                i = _scan_char_literal(src, i)
                continue
            if char == "(":
                stack.append([_method_before_paren(src, i, low), 0])
                i += 1
                continue
            if char == ")":
                if stack:
                    stack.pop()
                i += 1
                continue
            if char == "," and stack:
                stack[-1][1] += 1
                i += 1
                continue
            if char == '"' or char in "$@":
                j = i
                interp = verbatim = False
                while j < high and src[j] in "$@":
                    if src[j] == "$":
                        interp = True
                    else:
                        verbatim = True
                    j += 1
                if j < high and src[j] == '"':
                    content, end, _, spans = _scan_string(src, j, interp, verbatim)
                    contract, reason = _classify_literal(src, i, end, stack, low, high)
                    out.append(CsLiteral(line_of(i), content, contract, reason))
                    for hole_start, hole_end in spans:
                        scan_code(hole_start + 1, hole_end - 1)
                    i = end
                    continue
                i = j if j > i else i + 1
                continue
            i += 1

    scan_code(0, len(src))
    return out


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
                text, end, holes, _ = _scan_string(src, j, interp, verbatim)
                out.append(StringLit(line_of(i), text, interp,
                                     holes if interp else brace_placeholders(text)))
                i = end
                continue
            i = j if j > i else i + 1
            continue
        if char == '"':
            text, end, _, _ = _scan_string(src, i, False, False)
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
                    bph = signature_counter(bs.placeholders)
                    wph = signature_counter(ws.placeholders)
                    if bph != wph:
                        rep.fail(ph_tag, "%s:%d" % (path, ws.line),
                                 "string #%d changed and its {...} placeholders changed" % idx,
                                 sorted(bs.placeholders), sorted(ws.placeholders))
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
            bph = signature_counter([p for s in base for p in s.placeholders])
            wph = signature_counter([p for s in work for p in s.placeholders])
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
                       "%d file(s) fell back to whole-file placeholder multisets; "
                       "inside a StringFormat= markup extension only the structure is "
                       "compared (extension name, every Name=Value pair, the inner {...} "
                       "format tokens in order, any trailing unit) -- its on-screen prose "
                       "is translatable and is not compared"
                       % (positional, fallback))


# --------------------------------------------------------------------------
# check 4b -- contract literals must not be translated
# --------------------------------------------------------------------------

def check_contract_literals(repo: Repo, rep: Report) -> None:
    """A literal that is parsed, not displayed, must stay byte-identical.

    Translating one silently changes behaviour: it reads like prose but
    something matches against it.  Display text in the same expression is free.
    """
    tag = "C4 contract-literal"
    rep.method(tag, "string literals in src/**/*.cs are classified by the code around "
                    "them; a changed literal fails when it is an argument to .%s(, an "
                    "operand of == or !=, the FIRST argument of .Replace(, or a case "
                    "label / switch arm pattern -- displayed literals are free"
                    % "(, .".join(CONTRACT_METHODS))
    for path in sorted(p for p in repo.work_files
                       if p.startswith("src/") and p.endswith(".cs")):
        base_text, work_text = repo.both_text(path)
        if base_text is None or work_text is None:
            continue
        base = scan_cs_literals(base_text)
        work = scan_cs_literals(work_text)

        if len(base) != len(work):
            rep.note(tag, "%s: literal count changed (%d -> %d); comparing the multiset "
                          "of contract literals instead of matching positionally"
                     % (path, len(base), len(work)))
            bset = Counter(lit.content for lit in base if lit.contract)
            wset = Counter(lit.content for lit in work if lit.contract)
            missing = bset - wset
            if missing:
                rep.fail(tag, path,
                         "contract literal(s) no longer present (fallback method)",
                         ", ".join("%r x%d" % (k, v) for k, v in sorted(missing.items())),
                         "gone")
            continue

        for bl, wl in zip(base, work):
            if bl.contract and bl.content != wl.content:
                rep.fail(tag, "%s:%d" % (path, wl.line),
                         "a literal in a comparison position was changed (%s) -- it is "
                         "matched against, not displayed" % bl.reason,
                         bl.content, wl.content)
            elif bl.contract != wl.contract:
                rep.fail(tag, "%s:%d" % (path, wl.line),
                         "literal %r moved between a comparison position and display "
                         "text" % wl.content, bl.reason, wl.reason)


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
    check_contract_literals(repo, rep)
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

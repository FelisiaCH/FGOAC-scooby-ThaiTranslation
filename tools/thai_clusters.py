"""Split Thai text into clusters and encode them for the Thai game font.

The game draws one glyph per character and does no shaping, so Thai
vowel and tone marks would land in the wrong place.  thai_font_build.py
gives every multi-character cluster its own precomposed glyph at a
Private Use Area code point, and writes the mapping to clusters.json.
encode() rewrites text to use those code points.

A cluster is one character plus the marks that follow it.  Sara am
(U+0E33) counts as a mark, because its nikhahit interacts with a tone
mark before it.

Standard library only.
"""


def is_mark(ch):
    """True for a Thai character that joins the cluster before it."""
    # U+0E33 sara am counts as a mark: its nikhahit interacts with a preceding tone mark.
    cp = ord(ch)
    return cp == 0x0E31 or 0x0E33 <= cp <= 0x0E3A or 0x0E47 <= cp <= 0x0E4E


def clusters(text):
    """Split text into clusters.  A mark with nothing before it stands alone."""
    out = []
    for ch in text:
        if out and is_mark(ch):
            out[-1] += ch
        else:
            out.append(ch)
    return out


def distinct_clusters(texts):
    """The multi-character clusters in texts, each once, in first-seen order."""
    return list(dict.fromkeys(c for text in texts for c in clusters(text) if len(c) > 1))


def encode(text, mapping):
    """Replace each multi-character cluster with its PUA character.  KeyError if one is missing."""
    return "".join(chr(mapping[c]) if len(c) > 1 else c for c in clusters(text))

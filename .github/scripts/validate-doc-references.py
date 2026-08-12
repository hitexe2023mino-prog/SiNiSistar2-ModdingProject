#!/usr/bin/env python3
"""Validate that documentation references名指しする code symbols actually exist.

REFACTOR001 RF-002. The defect this guards against is real: SPEC005-traceability
recorded FR-415 as `Tested` and cited
`CrestPleasureAndValidationTests.ShippedDefaultsLeaveEveryNewMechanismInert`,
a test that exists nowhere but in that document.

Only symbols this repository owns are checked. A backticked `Type.Member` is
verified when `Type` is declared under `src/` or `tests/`; game and framework
types (`PlayerStatusManager.MP`, `AbnormalData.MaxLevel`) are declared in the
IL2CPP interop assemblies rather than here, so they are skipped rather than
reported as missing. That rule is what keeps the check free of false positives.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DOC_DIRS = ("docs/implementation",)
SOURCE_DIRS = ("src", "tests")
EXCLUDED_PARTS = {"bin", "obj"}

# `class Foo`, `sealed record struct Bar`, `internal static class Baz`, …
DECLARATION = re.compile(
    r"\b(?:class|record|struct|interface|enum)\s+(?P<name>[A-Z]\w*)"
)
# Backticked spans are where documents name code in this repository.
CODE_SPAN = re.compile(r"`([^`\n]+)`")
# `Type.Member`, tolerating a call suffix and a trailing generic argument list.
QUALIFIED = re.compile(r"^(?P<qualifier>[A-Z]\w*(?:\.[A-Z]\w*)*)\.(?P<member>[A-Z]\w*)")
# A bare `SomethingTests` names a test class outright, with no member to check.
# The suffix is this repository's own convention, so the name has to resolve.
BARE_TEST_CLASS = re.compile(r"^(?P<name>[A-Z]\w*Tests)$")


def source_files() -> list[Path]:
    files: list[Path] = []
    for directory in SOURCE_DIRS:
        for path in (ROOT / directory).rglob("*.cs"):
            if EXCLUDED_PARTS.isdisjoint(path.parts):
                files.append(path)
    return files


def declared_types() -> dict[str, list[Path]]:
    """Type name to every file declaring it. Partial types legitimately repeat."""
    types: dict[str, list[Path]] = {}
    for path in source_files():
        text = path.read_text(encoding="utf-8", errors="replace")
        for match in DECLARATION.finditer(text):
            types.setdefault(match.group("name"), []).append(path)
    return types


def member_exists(member: str, files: list[Path]) -> bool:
    """Whether a member of that name, or one it prefixes, is declared in the files.

    These documents abbreviate long test names by their leading words —
    `ProfileValidationTests.ShippedDefaultsHaveNoEffect` for the method actually
    called `ShippedDefaultsHaveNoEffectOnTheGame`. That is an established
    convention here rather than a defect, so a prefix match counts. A name that
    prefixes nothing is the case worth reporting: it points at no method at all.
    """
    prefix = re.compile(rf"\b{re.escape(member)}\w*")
    return any(
        prefix.search(path.read_text(encoding="utf-8", errors="replace"))
        for path in files
    )


def validate_document(path: Path, types: dict[str, list[Path]]) -> list[str]:
    errors: list[str] = []
    relative = path.relative_to(ROOT)
    checked: set[str] = set()

    for line_number, line in enumerate(
        path.read_text(encoding="utf-8").splitlines(), start=1
    ):
        for span in CODE_SPAN.findall(line):
            span = span.strip()

            bare = BARE_TEST_CLASS.match(span)
            if bare and bare.group("name") not in types:
                name = bare.group("name")
                if name not in checked:
                    checked.add(name)
                    errors.append(
                        f"{relative}:{line_number}: `{name}` does not exist. "
                        "No test class of that name is declared under tests/."
                    )
                continue

            match = QUALIFIED.match(span)
            if not match:
                continue

            # The qualifier may be namespace-qualified; the type is its last part.
            owner = match.group("qualifier").rsplit(".", 1)[-1]
            member = match.group("member")
            declarations = types.get(owner)
            if not declarations:
                # Declared outside this repository (game interop, BepInEx, BCL).
                continue

            key = f"{owner}.{member}"
            if key in checked or member_exists(member, declarations):
                checked.add(key)
                continue

            checked.add(key)
            owners = ", ".join(
                str(p.relative_to(ROOT)).replace("\\", "/") for p in declarations
            )
            errors.append(
                f"{relative}:{line_number}: `{key}` does not exist. "
                f"{owner} is declared in {owners}, and no member named {member} "
                "appears there."
            )

    return errors


def main() -> int:
    types = declared_types()
    if not types:
        print("ERROR: no type declarations found under src/ or tests/")
        return 1

    documents = sorted(
        path
        for directory in DOC_DIRS
        for path in (ROOT / directory).glob("*.md")
    )
    if not documents:
        print(f"ERROR: no documents found under {', '.join(DOC_DIRS)}")
        return 1

    errors: list[str] = []
    for document in documents:
        errors.extend(validate_document(document, types))

    if errors:
        print(f"Documentation reference validation failed with {len(errors)} error(s):")
        for error in errors:
            print(f"- {error}")
        return 1

    print(
        f"Validated {len(documents)} documents against "
        f"{len(types)} types declared in this repository."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())

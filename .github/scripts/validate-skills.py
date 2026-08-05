#!/usr/bin/env python3
"""Validate repository-local agent skills without third-party dependencies."""

from __future__ import annotations

import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SKILLS_ROOT = ROOT / ".agents" / "skills"
CLAUDE_SKILLS_ROOT = ROOT / ".claude" / "skills"
ALLOWED_FRONTMATTER = {"name", "description"}
NAME_PATTERN = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
LINK_PATTERN = re.compile(r"\[[^\]]+\]\(([^)]+)\)")


def parse_frontmatter(path: Path) -> tuple[dict[str, str], str]:
    content = path.read_text(encoding="utf-8")
    match = re.match(r"\A---\r?\n(.*?)\r?\n---\r?\n(.*)\Z", content, re.DOTALL)
    if not match:
        raise ValueError("frontmatter must be delimited by ---")

    fields: dict[str, str] = {}
    for line in match.group(1).splitlines():
        field = re.fullmatch(r"([a-z][a-z0-9-]*):\s*(.+)", line)
        if not field:
            raise ValueError(f"unsupported frontmatter line: {line!r}")
        key, value = field.groups()
        if key in fields:
            raise ValueError(f"duplicate frontmatter key: {key}")
        fields[key] = value.strip().strip('"').strip("'")
    return fields, match.group(2)


def validate_skill(skill_dir: Path) -> list[str]:
    errors: list[str] = []
    skill_file = skill_dir / "SKILL.md"
    relative = skill_dir.relative_to(ROOT)

    try:
        fields, body = parse_frontmatter(skill_file)
    except ValueError as error:
        return [f"{relative}: {error}"]

    if set(fields) != ALLOWED_FRONTMATTER:
        errors.append(
            f"{relative}: frontmatter keys must be exactly "
            f"{sorted(ALLOWED_FRONTMATTER)}, found {sorted(fields)}"
        )

    name = fields.get("name", "")
    description = fields.get("description", "")
    if not NAME_PATTERN.fullmatch(name):
        errors.append(f"{relative}: invalid skill name {name!r}")
    if name != skill_dir.name:
        errors.append(f"{relative}: name must match directory name")
    if not description or len(description) > 1024:
        errors.append(f"{relative}: description must contain 1-1024 characters")
    if "使用" not in description:
        errors.append(f"{relative}: description must state when to use the skill")

    text = skill_file.read_text(encoding="utf-8")
    if re.search(r"\bTODO\b|\[TODO", text, re.IGNORECASE):
        errors.append(f"{relative}: unresolved TODO placeholder")
    if len(body.splitlines()) >= 500:
        errors.append(f"{relative}: SKILL.md body must stay below 500 lines")

    for target in LINK_PATTERN.findall(body):
        if target.startswith(("http://", "https://", "#", "/")):
            continue
        resolved = (skill_dir / target.split("#", 1)[0]).resolve()
        try:
            resolved.relative_to(skill_dir.resolve())
        except ValueError:
            errors.append(f"{relative}: link escapes skill directory: {target}")
            continue
        if not resolved.exists():
            errors.append(f"{relative}: missing linked file: {target}")

    metadata = skill_dir / "agents" / "openai.yaml"
    if not metadata.is_file():
        errors.append(f"{relative}: agents/openai.yaml is required")
    else:
        metadata_text = metadata.read_text(encoding="utf-8")
        for key in ("display_name", "short_description", "default_prompt"):
            if not re.search(rf'^  {key}: "[^"]+"$', metadata_text, re.MULTILINE):
                errors.append(f"{relative}: agents/openai.yaml needs quoted {key}")
        prompt = re.search(r'^  default_prompt: "([^"]+)"$', metadata_text, re.MULTILINE)
        short_description = re.search(
            r'^  short_description: "([^"]+)"$', metadata_text, re.MULTILINE
        )
        if short_description and not 25 <= len(short_description.group(1)) <= 64:
            errors.append(
                f"{relative}: short_description must contain 25-64 characters"
            )
        if prompt and f"${name}" not in prompt.group(1):
            errors.append(f"{relative}: default_prompt must mention ${name}")

    return errors


def render_claude_adapter(name: str, description: str) -> str:
    """Return the complete Claude Code adapter for a canonical skill."""
    return (
        "---\n"
        f"name: {name}\n"
        f"description: {description}\n"
        "---\n\n"
        "# Claude Code Adapter\n\n"
        f"[正本のスキル指示](../../../.agents/skills/{name}/SKILL.md)を最後まで読み、"
        "そこに記載された手順と停止条件に従う。正本から参照される相対パスは、"
        "正本の `SKILL.md` があるディレクトリを基準に解決する。\n"
    )


def validate_claude_adapter(canonical_dir: Path) -> list[str]:
    errors: list[str] = []
    canonical_file = canonical_dir / "SKILL.md"
    try:
        canonical_fields, _ = parse_frontmatter(canonical_file)
    except ValueError:
        # validate_skill reports the actionable canonical frontmatter error.
        return errors
    name = canonical_fields.get("name", canonical_dir.name)
    adapter_file = CLAUDE_SKILLS_ROOT / name / "SKILL.md"
    relative = adapter_file.relative_to(ROOT)

    if not adapter_file.is_file():
        return [f"{relative}: Claude Code adapter is required"]

    expected = render_claude_adapter(name, canonical_fields.get("description", ""))
    actual = adapter_file.read_text(encoding="utf-8")
    if actual != expected:
        errors.append(
            f"{relative}: adapter must match the canonical name and description "
            "and delegate to its SKILL.md"
        )
    return errors


def is_under(path: Path, directory: Path) -> bool:
    return path == directory or directory in path.parents


def main() -> int:
    if not SKILLS_ROOT.is_dir():
        print(f"ERROR: missing skills directory: {SKILLS_ROOT}")
        return 1

    skill_dirs = sorted(path.parent for path in SKILLS_ROOT.glob("*/SKILL.md"))
    if not skill_dirs:
        print("ERROR: no skills found")
        return 1

    errors: list[str] = []
    for misplaced in ROOT.rglob("SKILL.md"):
        if ".git" in misplaced.parts:
            continue
        if not is_under(misplaced, SKILLS_ROOT) and not is_under(
            misplaced, CLAUDE_SKILLS_ROOT
        ):
            errors.append(
                f"{misplaced.relative_to(ROOT)}: skill must be under .agents/skills "
                "or .claude/skills"
            )

    for skill_dir in skill_dirs:
        errors.extend(validate_skill(skill_dir))
        errors.extend(validate_claude_adapter(skill_dir))

    canonical_names = {path.name for path in skill_dirs}
    claude_names = {
        path.parent.name for path in CLAUDE_SKILLS_ROOT.glob("*/SKILL.md")
    }
    for extra_name in sorted(claude_names - canonical_names):
        errors.append(
            f".claude/skills/{extra_name}: adapter has no canonical skill under "
            ".agents/skills"
        )

    if errors:
        print(f"Skill validation failed with {len(errors)} error(s):")
        for error in errors:
            print(f"- {error}")
        return 1

    print(
        f"Validated {len(skill_dirs)} canonical skills and Claude Code adapters: "
        + ", ".join(path.name for path in skill_dirs)
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())

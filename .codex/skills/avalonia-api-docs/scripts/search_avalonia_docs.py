#!/usr/bin/env python3
"""Search Avalonia XML docs from the NuGet packages restored for a project."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def wsl_path(path: str) -> Path:
    match = re.match(r"^([A-Za-z]):[\\/](.*)$", path)
    if os.name == "posix" and match:
        drive = match.group(1).lower()
        rest = match.group(2).replace("\\", "/")
        return Path(f"/mnt/{drive}/{rest}")
    return Path(path)


def find_assets(start: Path, explicit: str | None) -> Path:
    if explicit:
        path = Path(explicit)
        if path.exists():
            return path
        raise FileNotFoundError(f"project.assets.json not found: {path}")

    current = start.resolve()
    for directory in [current, *current.parents]:
        matches = sorted(directory.glob("**/obj/project.assets.json"))
        if matches:
            return matches[0]

    raise FileNotFoundError("Could not find obj/project.assets.json from current directory")


def target_framework(assets: dict) -> str:
    project = assets.get("project", {})
    frameworks = project.get("frameworks", {})
    if frameworks:
        return sorted(frameworks.keys())[0]

    for target_name in assets.get("targets", {}):
        match = re.search(r"Version=v?(\d+\.\d+)", target_name)
        if match:
            major = match.group(1).split(".")[0]
            return f"net{major}.0"

    return "net10.0"


def package_roots(assets: dict) -> list[Path]:
    roots: list[Path] = []
    for raw in assets.get("packageFolders", {}):
        root = wsl_path(raw)
        if root.exists():
            roots.append(root)
    return roots


def avalonia_xml_files(assets: dict, tfm: str) -> list[Path]:
    roots = package_roots(assets)
    if not roots:
        return []

    files: list[Path] = []
    seen: set[Path] = set()
    for library in assets.get("libraries", {}).values():
        package_path = library.get("path", "")
        if not package_path.lower().startswith("avalonia"):
            continue

        for root in roots:
            package_dir = root / package_path
            for base in ("ref", "lib"):
                directory = package_dir / base / tfm
                for xml_file in sorted(directory.glob("*.xml")):
                    resolved = xml_file.resolve()
                    if resolved not in seen:
                        files.append(xml_file)
                        seen.add(resolved)

    return files


def compact_text(element: ET.Element) -> str:
    text = " ".join(part.strip() for part in element.itertext() if part.strip())
    return re.sub(r"\s+", " ", text)


def matches(member_name: str, text: str, terms: list[str]) -> bool:
    haystack = f"{member_name} {text}".lower()
    return all(term.lower() in haystack for term in terms)


def search(files: list[Path], terms: list[str], max_results: int) -> int:
    count = 0
    for xml_file in files:
        try:
            root = ET.parse(xml_file).getroot()
        except ET.ParseError as exc:
            print(f"skip malformed XML: {xml_file}: {exc}", file=sys.stderr)
            continue

        for member in root.findall("./members/member"):
            name = member.attrib.get("name", "")
            text = compact_text(member)
            if not matches(name, text, terms):
                continue

            count += 1
            print(f"{count}. {name}")
            print(f"   file: {xml_file}")
            if text:
                snippet = text[:500]
                suffix = "..." if len(text) > 500 else ""
                print(f"   docs: {snippet}{suffix}")
            print()

            if count >= max_results:
                return count

    return count


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("terms", nargs="*", help="Terms that must all match member name or docs text")
    parser.add_argument("--project-assets", help="Path to obj/project.assets.json")
    parser.add_argument("--tfm", help="Target framework to search, for example net10.0")
    parser.add_argument("--max-results", type=int, default=20)
    parser.add_argument("--list-files", action="store_true")
    args = parser.parse_args()

    assets_path = find_assets(Path.cwd(), args.project_assets)
    assets = json.loads(assets_path.read_text(encoding="utf-8-sig"))
    tfm = args.tfm or target_framework(assets)
    files = avalonia_xml_files(assets, tfm)

    if args.list_files:
        print(f"assets: {assets_path}")
        print(f"tfm: {tfm}")
        for xml_file in files:
            print(xml_file)
        return 0 if files else 1

    if not args.terms:
        parser.error("provide search terms or use --list-files")

    if not files:
        print(f"No Avalonia XML docs found for {tfm}; restore the project first.", file=sys.stderr)
        return 1

    found = search(files, args.terms, args.max_results)
    if found == 0:
        print("No matching Avalonia API docs found.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

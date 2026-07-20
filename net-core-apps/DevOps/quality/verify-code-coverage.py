#!/usr/bin/env python3
"""Enforce a deduplicated production-source line coverage floor."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


PRODUCTION_ROOTS = {"buildingblocks", "services"}
EXCLUDED_SEGMENTS = {"bin", "obj", "migrations"}
EXCLUDED_SUFFIXES = (".designer.cs", ".g.cs", ".generated.cs")


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("results", type=Path, help="Directory containing Cobertura reports")
    parser.add_argument("--repository-root", type=Path, required=True)
    parser.add_argument("--minimum-line-rate", type=float, required=True,
                        help="Required covered line percentage, from 0 through 100")
    return parser.parse_args()


def production_path(filename: str, repository_root: Path) -> Path | None:
    path = Path(filename)
    if not path.is_absolute():
        path = repository_root / path
    try:
        relative = path.resolve().relative_to(repository_root.resolve())
    except ValueError:
        return None

    lowered = tuple(part.lower() for part in relative.parts)
    if not lowered or lowered[0] not in PRODUCTION_ROOTS:
        return None
    if any(part in EXCLUDED_SEGMENTS or part == "tests" or part.endswith(".tests")
           for part in lowered[:-1]):
        return None
    if lowered[-1].endswith(EXCLUDED_SUFFIXES):
        return None
    return relative


def collect_lines(results: Path, repository_root: Path) -> dict[tuple[str, int], int]:
    lines: dict[tuple[str, int], int] = {}
    reports = sorted(results.rglob("*.cobertura.xml"))
    if not reports:
        raise RuntimeError(f"No Cobertura reports were found below {results}.")

    for report in reports:
        document = ET.parse(report)
        for item in document.findall(".//class"):
            relative = production_path(item.get("filename", ""), repository_root)
            if relative is None:
                continue
            for line in item.findall("./lines/line"):
                key = (relative.as_posix().lower(), int(line.attrib["number"]))
                lines[key] = max(lines.get(key, 0), int(line.attrib.get("hits", "0")))
    return lines


def main() -> int:
    arguments = parse_arguments()
    if not 0 <= arguments.minimum_line_rate <= 100:
        raise ValueError("--minimum-line-rate must be between 0 and 100.")

    lines = collect_lines(arguments.results, arguments.repository_root)
    if not lines:
        raise RuntimeError("Coverage reports contained no eligible production source lines.")

    covered = sum(hits > 0 for hits in lines.values())
    total = len(lines)
    rate = covered * 100.0 / total
    print(f"Production line coverage: {rate:.2f}% ({covered}/{total}); "
          f"required: {arguments.minimum_line_rate:.2f}%")
    return 0 if rate >= arguments.minimum_line_rate else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, RuntimeError, ValueError, ET.ParseError) as error:
        print(f"Coverage verification failed: {error}", file=sys.stderr)
        sys.exit(2)

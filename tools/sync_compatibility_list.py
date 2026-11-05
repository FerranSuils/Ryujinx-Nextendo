#!/usr/bin/env python3
"""Sync Ryujinx GitHub compatibility issues CSV into docs/compatibility.csv."""

from __future__ import annotations

import csv
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
ISSUES_CSV = REPO_ROOT / "Ryujinx Games List Compatibility.csv"
DOCS_CSV = REPO_ROOT / "docs" / "compatibility.csv"

TITLE_ID_RE = re.compile(r" - ([0-9A-Fa-f]{16})\s*$")
VALID_STATUSES = frozenset({"playable", "ingame", "menus", "boots", "nothing"})

LABEL_DESCRIPTIONS: dict[str, str] = {
    "audio": "Audio issues such as silence, crackling, or desync.",
    "audout": "AudOut service issues affecting sound output.",
    "gpu": "GPU rendering glitches, corruption, or missing visuals.",
    "nvdec": "Hardware video decoding (NVDEC) problems; cutscenes or FMV may fail.",
    "crash": "The game crashes during play or at specific scenes.",
    "deadlock": "The game can hang or deadlock.",
    "slow": "Performance is below expected frame rate.",
    "services": "HLE service emulation issues.",
    "services-horizon": "New Horizon service stubs are incomplete.",
    "online": "Online features are affected or untested.",
    "online-broken": "Online multiplayer or services do not work.",
    "ldn-works": "Local wireless (LDN) works with limitations.",
    "ldn-untested": "Local wireless (LDN) has not been verified.",
    "ldn-broken": "Local wireless (LDN) does not work.",
    "ldn-partial": "Local wireless (LDN) works only partially.",
    "ldn": "Local wireless (LDN) related issue.",
    "opengl-backend-bug": "OpenGL graphics backend bug.",
    "vulkan-backend-bug": "Vulkan graphics backend bug.",
    "opengl": "OpenGL-specific problem.",
    "amd-vendor-bug": "AMD GPU-specific driver or backend issue.",
    "intel-vendor-bug": "Intel GPU-specific driver or backend issue.",
    "mac-bug": "macOS-specific problem.",
    "loader-allocator": "Loader memory allocator issue at startup.",
    "32-bit": "32-bit title; may need specific CPU settings.",
    "ue4": "Unreal Engine 4 title with engine-specific bugs.",
    "homebrew": "Homebrew content; not a commercial release.",
    "regression": "Previously worked; broke in a newer emulator build.",
    "bug": "General emulation bug.",
    "cpu": "CPU emulation issue.",
    "kernel": "Kernel/HOS emulation issue.",
    "horizon": "Horizon OS service issue.",
    "gui": "Emulator UI or overlay issue.",
    "needs update": "Game or emulator build needs an update for this title.",
    "incomplete": "Compatibility report is incomplete.",
    "demo": "Demo build; full game may differ.",
    "lan": "LAN multiplayer label from the tracker.",
    "compat-unverified": "No verified status on the tracker; treat as unknown.",
}


def normalize_name(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "", (name or "").lower())


def parse_issue_title(title: str, extracted_id: str) -> tuple[str, str]:
    title = (title or "").strip()
    game_id = (extracted_id or "").strip().upper()
    match = TITLE_ID_RE.search(title)
    if not game_id and match:
        game_id = match.group(1).upper()
    name = title
    if match:
        name = title[: match.start()].strip()
    elif game_id:
        name = re.sub(rf"\s*-\s*{re.escape(game_id)}\s*$", "", title, flags=re.I).strip()
    return game_id, name


def labels_from_field(raw: str) -> list[str]:
    return [part.strip() for part in (raw or "").split(";") if part.strip()]


def clean_labels(labels: list[str]) -> list[str]:
    cleaned: list[str] = []
    for label in labels:
        lower = label.lower()
        if lower.startswith("status-"):
            continue
        cleaned.append(label)
    return cleaned


def status_from_labels(labels: list[str]) -> str:
    for label in labels:
        lower = label.lower()
        if lower.startswith("status-"):
            value = lower.removeprefix("status-")
            if value in VALID_STATUSES:
                return value
    return ""


def describe_labels(labels: list[str]) -> str:
    if not labels:
        return "No known glitches recorded for this title."
    parts: list[str] = []
    for label in labels:
        key = label.lower()
        text = LABEL_DESCRIPTIONS.get(key)
        if text:
            parts.append(text)
        else:
            parts.append(f"Tracked label: {label}.")
    return " ".join(parts)


@dataclass
class CompatRow:
    title_id: str = ""
    game_name: str = ""
    labels: list[str] = field(default_factory=list)
    status: str = ""
    last_updated: str = ""
    known_issues: str = ""
    issue_number: str = ""

    def score(self) -> tuple[int, int, int]:
        return (
            1 if self.status else 0,
            len(self.labels),
            len(self.known_issues),
        )


def read_docs_rows() -> list[CompatRow]:
    rows: list[CompatRow] = []
    if not DOCS_CSV.exists():
        return rows
    with DOCS_CSV.open(newline="", encoding="utf-8") as handle:
        reader = csv.reader(handle)
        header = next(reader, None)
        known_idx = header.index('"known_issues"') if header and '"known_issues"' in header else -1
        for row in reader:
            if len(row) < 5:
                continue
            labels = labels_from_field(row[2])
            known = row[known_idx].strip('"') if known_idx >= 0 and len(row) > known_idx else ""
            rows.append(
                CompatRow(
                    title_id=row[0].strip('"').upper(),
                    game_name=row[1].strip('"'),
                    labels=labels,
                    status=row[3].strip().lower(),
                    last_updated=row[4].strip(),
                    known_issues=known,
                )
            )
    return rows


def read_issue_rows() -> list[CompatRow]:
    rows: list[CompatRow] = []
    with ISSUES_CSV.open(newline="", encoding="utf-8") as handle:
        for raw in csv.DictReader(handle):
            title = raw.get("issue_title") or ""
            if "Game Incompatibility" in title and " - " not in title:
                continue
            title_id, game_name = parse_issue_title(title, raw.get("extracted_game_id") or "")
            labels = labels_from_field(raw.get("issue_labels") or "")
            status = (raw.get("extracted_status") or "").strip().lower()
            if not status:
                status = status_from_labels(labels)
            labels = clean_labels(labels)
            rows.append(
                CompatRow(
                    title_id=title_id,
                    game_name=game_name,
                    labels=labels,
                    status=status,
                    last_updated=(raw.get("last_event_date") or "").strip(),
                    issue_number=raw.get("issue_number") or "",
                )
            )
    return rows


def merge_rows(primary: CompatRow, fallback: CompatRow) -> CompatRow:
    status = primary.status or fallback.status
    labels = primary.labels or fallback.labels
    title_id = primary.title_id or fallback.title_id
    game_name = primary.game_name or fallback.game_name
    last_updated = primary.last_updated or fallback.last_updated
    if not status and not labels:
        status = "boots"
        labels = ["compat-unverified"]
    known_issues = describe_labels(labels)
    if fallback.known_issues and not primary.labels:
        known_issues = fallback.known_issues
    return CompatRow(
        title_id=title_id,
        game_name=game_name,
        labels=labels,
        status=status if status in VALID_STATUSES else "boots",
        last_updated=last_updated,
        known_issues=known_issues,
        issue_number=primary.issue_number,
    )


def pick_better(existing: CompatRow, candidate: CompatRow) -> CompatRow:
    if candidate.score() > existing.score():
        return candidate
    return existing


def build_merged_catalog() -> list[CompatRow]:
    docs_rows = read_docs_rows()
    docs_by_id = {row.title_id: row for row in docs_rows if row.title_id}
    docs_by_name = {normalize_name(row.game_name): row for row in docs_rows if row.game_name}

    by_id: dict[str, CompatRow] = {}
    by_name: dict[str, CompatRow] = {}

    for issue in read_issue_rows():
        fallback = None
        if issue.title_id and issue.title_id in docs_by_id:
            fallback = docs_by_id[issue.title_id]
        elif normalize_name(issue.game_name) in docs_by_name:
            fallback = docs_by_name[normalize_name(issue.game_name)]
        merged = merge_rows(issue, fallback) if fallback else merge_rows(issue, CompatRow())
        if merged.title_id:
            by_id[merged.title_id] = pick_better(by_id.get(merged.title_id, merged), merged)
        else:
            key = normalize_name(merged.game_name)
            if key:
                by_name[key] = pick_better(by_name.get(key, merged), merged)

    for doc in docs_rows:
        if doc.title_id and doc.title_id not in by_id:
            by_id[doc.title_id] = merge_rows(doc, CompatRow())
        elif not doc.title_id:
            key = normalize_name(doc.game_name)
            if key and key not in by_name:
                by_name[key] = merge_rows(doc, CompatRow())

    catalog = list(by_id.values()) + list(by_name.values())
    catalog.sort(key=lambda row: row.game_name.lower())
    return catalog


def write_docs_csv(rows: list[CompatRow]) -> None:
    DOCS_CSV.parent.mkdir(parents=True, exist_ok=True)
    with DOCS_CSV.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle, quoting=csv.QUOTE_MINIMAL)
        writer.writerow(["title_id", "game_name", "labels", "status", "last_updated", "known_issues"])
        for row in rows:
            writer.writerow(
                [
                    row.title_id,
                    row.game_name,
                    ";".join(row.labels),
                    row.status,
                    row.last_updated,
                    row.known_issues,
                ]
            )


def write_issues_csv(rows: list[CompatRow]) -> None:
    """Rewrite the local issues CSV with merged status, labels, and known_errors."""
    issue_rows: list[dict[str, str]] = []
    with ISSUES_CSV.open(newline="", encoding="utf-8") as handle:
        reader = csv.DictReader(handle)
        fieldnames = list(reader.fieldnames or [])
        for extra in ("merged_status", "merged_labels", "known_errors"):
            if extra not in fieldnames:
                fieldnames.append(extra)
        lookup_by_id = {row.title_id: row for row in rows if row.title_id}
        lookup_by_name = {normalize_name(row.game_name): row for row in rows if row.game_name}
        for raw in reader:
            title_id, game_name = parse_issue_title(
                raw.get("issue_title") or "", raw.get("extracted_game_id") or ""
            )
            merged = lookup_by_id.get(title_id) or lookup_by_name.get(normalize_name(game_name))
            if merged:
                raw["merged_status"] = merged.status
                raw["merged_labels"] = ";".join(merged.labels)
                raw["known_errors"] = merged.known_issues
            else:
                raw["merged_status"] = raw.get("extracted_status") or ""
                raw["merged_labels"] = raw.get("issue_labels") or ""
                raw["known_errors"] = describe_labels(clean_labels(labels_from_field(raw.get("issue_labels") or "")))
            issue_rows.append(raw)

    with ISSUES_CSV.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(issue_rows)


def main() -> int:
    if not ISSUES_CSV.exists():
        print(f"Missing issues CSV: {ISSUES_CSV}", file=sys.stderr)
        return 1
    catalog = build_merged_catalog()
    write_docs_csv(catalog)
    write_issues_csv(catalog)
    print(f"Wrote {len(catalog)} rows to {DOCS_CSV}")
    print(f"Updated {ISSUES_CSV}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

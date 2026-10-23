"""Builds dist/PPLua-<version>.zip from a Release build: the plugin, MoonSharp, the docs and the bundled mods.

Run `dotnet build src/PPLua.csproj -c Release` first. Nothing from the game is packaged, and neither are
files players add for themselves (a mod's custom/ folder, Homelander's own sounds/laser.mp3).
Writes dist/SHA256SUMS.txt next to the zip for the release page.
"""
import fnmatch
import hashlib
import re
import sys
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parent.parent
version = re.search(r"<Version>(.+?)</Version>", (root / "src" / "PPLua.csproj").read_text()).group(1)
build = root / "src" / "bin" / "Release"
dist = root / "dist"
dist.mkdir(exist_ok=True)
out = dist / f"PPLua-{version}.zip"

# player-supplied files: kept out of the release even if they sit in a working copy (see .gitignore)
EXCLUDE = ["*/custom/*", "Homelander/sounds/laser.mp3"]

plugin = "BepInEx/plugins/PPLua/"
files = {
    build / "PPLua.dll": plugin + "PPLua.dll",
    build / "MoonSharp.Interpreter.dll": plugin + "MoonSharp.Interpreter.dll",
    root / "LICENSE": plugin + "LICENSE",
    root / "THIRD-PARTY-NOTICES.md": plugin + "THIRD-PARTY-NOTICES.md",
    root / "CHANGELOG.md": plugin + "CHANGELOG.md",
    root / "README.md": plugin + "docs/README.md",
    root / "README.ru.md": plugin + "docs/README.ru.md",
    root / "docs" / "API.md": plugin + "docs/API.md",
    root / "docs" / "API.ru.md": plugin + "docs/API.ru.md",
}
for f in sorted((root / "LuaMods").rglob("*")):
    rel = f.relative_to(root / "LuaMods").as_posix()
    if f.is_file() and not any(fnmatch.fnmatch(rel, p) for p in EXCLUDE):
        files[f] = "LuaMods/" + rel

missing = [str(src) for src in files if not src.exists()]
if missing:
    sys.exit("missing (build first?): " + ", ".join(missing))

with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for src, arc in files.items():
        z.write(src, arc)


def sha256(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


sums = {out.name: sha256(out), "PPLua.dll": sha256(build / "PPLua.dll"),
        "MoonSharp.Interpreter.dll": sha256(build / "MoonSharp.Interpreter.dll")}
(dist / "SHA256SUMS.txt").write_text("".join(f"{h}  {n}\n" for n, h in sums.items()))

print(out)
for f in sorted(files.values()):
    print("  " + f)
for n, h in sums.items():
    print(f"SHA-256 {n}: {h}")

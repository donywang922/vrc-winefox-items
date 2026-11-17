"""Port the original hand-item menus, remapping only asset/parameter references."""
import hashlib
import json
import re
import shutil
import uuid
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[1]
ROOT = PROJECT / "Assets" / "WineFoxItems"
SOURCE = PROJECT.parent / "vrc-wine-fox" / "Assets"
PARAMETERS = {"item": "WineFoxItems/Item", "itrigger": "WineFoxItems/TriggerInput"}
MENU_NAMES = {"InstallMenu": "主菜单", "ToolsMenu": "物品", "FoodMenu": "食品"}


def guid(path):
    return re.search(r"^guid: ([0-9a-f]{32})$", Path(str(path) + ".meta").read_text(encoding="utf-8"), re.M)[1]


def field(block, name, indent=4):
    return re.search(r"(?m)^" + " " * indent + re.escape(name) + r":([^\n]*)", block)[1].strip()


def scalar(value):
    return json.loads(value) if value.startswith('"') else value


source_icons = {guid(p): p for p in (SOURCE / "图标").glob("*.png")}
menu_remap = {guid(SOURCE / "菜单" / (original + ".asset")): name for name, original in MENU_NAMES.items()}
expected = []
for target_name, original_name in MENU_NAMES.items():
    source_path = SOURCE / "菜单" / (original_name + ".asset")
    source_text = source_path.read_text(encoding="utf-8")
    header, body = source_text.split("  controls:\n", 1)
    header = re.sub(r"(?m)^  m_Name:.*$", "  m_Name: " + target_name, header)
    header = re.sub(r"(?m)^  Parameters:.*$", "  Parameters: {fileID: 0}", header)
    blocks = [b for b in re.split(r"(?m)(?=^  - name:)", body) if b.strip()]
    output, specs = [], []
    for block in blocks:
        name = scalar(re.search(r"(?m)^  - name:([^\n]*)", block)[1].strip())
        if target_name == "InstallMenu" and name not in ("物品", "食品"):
            continue
        icon_guid = re.search(r"guid: ([0-9a-f]{32})", field(block, "icon"))[1]
        icon_source = source_icons[icon_guid]
        icon_target = ROOT / "Resources" / icon_source.name
        target_guid = guid(icon_target) if Path(str(icon_target) + ".meta").exists() else uuid.uuid4().hex
        shutil.copyfile(icon_source, icon_target)
        icon_meta = Path(str(icon_source) + ".meta").read_text(encoding="utf-8")
        Path(str(icon_target) + ".meta").write_text(re.sub(r"(?m)^guid:.*$", "guid: " + target_guid, icon_meta), encoding="utf-8")
        block = block.replace(icon_guid, target_guid)
        parameter = scalar(field(block, "name", indent=6))
        mapped_parameter = PARAMETERS.get(parameter, parameter)
        block = re.sub(r"(?m)^      name:[^\n]*$", "      name: " + mapped_parameter, block)
        submenu = ""
        match = re.search(r"guid: ([0-9a-f]{32})", field(block, "subMenu"))
        if match:
            submenu = menu_remap[match[1]]
            block = block.replace(match[1], guid(ROOT / "Menus" / (submenu + ".asset")))
        output.append(block)
        specs.append({"name": name, "type": int(field(block, "type")), "parameter": mapped_parameter,
                      "value": float(field(block, "value")), "style": int(field(block, "style")),
                      "iconFile": icon_source.name, "iconSha256": hashlib.sha256(icon_source.read_bytes()).hexdigest(),
                      "subMenu": submenu})
    (ROOT / "Menus" / (target_name + ".asset")).write_text(header + "  controls:\n" + "".join(output), encoding="utf-8")
    expected.append({"name": target_name, "sourceFile": source_path.name, "controls": specs})

# Remove the generated wrapper, which was not in the original menu.
for suffix in ("", ".meta"):
    obsolete = ROOT / "Menus" / ("WineFoxItemsMenu.asset" + suffix)
    if obsolete.exists():
        obsolete.unlink()
(ROOT / "Editor" / "OriginalMenuLayout.json").write_text(json.dumps({"menus": expected}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("Restored original radial menus: main=物品/食品, item controls=7, food controls=5; original icons/importer settings preserved.")

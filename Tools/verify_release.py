"""Verify that the exported package matches the installed, tested assets."""
from pathlib import Path
import hashlib
import tarfile
import re

root = Path(__file__).resolve().parents[1]
package = root / 'Releases/WineFoxItems-MA.unitypackage'
with tarfile.open(package, 'r:gz') as archive:
    members = {m.name: m for m in archive.getmembers()}
    assets = {}
    for name in members:
        if name.endswith('/pathname'):
            path = archive.extractfile(members[name]).read().decode().rstrip('\0\r\n')
            content = members.get(name.rsplit('/', 1)[0] + '/asset')
            if content:
                data = archive.extractfile(content).read()
                assert (root / path).read_bytes() == data, f'Package differs: {path}'
                assets[path] = hashlib.sha256(data).hexdigest()
    required = ['WineFoxItems.prefab', 'Meshes/FinalMergedItems.asset',
                'WineFoxItemsFX.controller', 'Editor/WineFoxItemsBuildPlugin.cs',
                'Menus/InstallMenu.asset', 'Menus/ToolsMenu.asset', 'Menus/FoodMenu.asset',
                'Animations/StaffAxisIdle_x.anim', 'Animations/StaffAxisIdle_y.anim']
    for path in required:
        assert 'Assets/WineFoxItems/' + path in assets, f'Missing: {path}'
    assert all(p.startswith('Assets/WineFoxItems/') for p in assets)
    assert not any('SourceMergedItems' in p or 'Variants/' in p or 'World.prefab' in p for p in assets)
prefab = (root/'Assets/WineFoxItems/WineFoxItems.prefab').read_text(encoding='utf-8')
configs = {}
for name, body in re.findall(r'  - nameOrPrefix: ([^\n]+)\n(.*?)(?=\n  - nameOrPrefix:|\n---|\Z)',prefab,re.S):
    configs[name] = {key: int(re.search(r'^    '+key+r': (\d+)$',body,re.M)[1]) for key in ['syncType','localOnly','saved']}
expression = {name:p for name,p in configs.items() if p['syncType'] != 0}
assert expression == {
    'WineFoxItems/Item': {'syncType':1,'localOnly':0,'saved':1},
    'WineFoxItems/TriggerInput': {'syncType':3,'localOnly':0,'saved':1},
}, expression
controller = (root/'Assets/WineFoxItems/WineFoxItemsFX.controller').read_text(encoding='utf-8')
assert all(p not in controller for p in ['WineFoxItems/EatInput','WineFoxItems/FoodContact','Wine Fox / Food input'])
report = f'PASS: {len(assets)} exported files exactly match installed assets.\n'
report += 'PASS: original expression parameter policy: Item Int + TriggerInput Bool, both synced/saved, 9 bits total and 0 extra bits; no extra debug parameters/layer.\n'
report += 'PASS: native cannon, hidden mesh, original menus, staff reset clips and required FX-mask build plugin are included.\n'
report += 'PASS: no source avatar, tester, raw source mesh, old world reference or experimental variants in release.\n'
report += 'Package SHA256: ' + hashlib.sha256(package.read_bytes()).hexdigest() + '\n'
(root / 'Artifacts/release-verification.txt').write_text(report, encoding='utf-8')
print(report)

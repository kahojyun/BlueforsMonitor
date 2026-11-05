#!/usr/bin/env python3
"""Fail when locked packages or preserved license bytes drift from the reviewed inventory."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def locked_packages():
    result = set()
    locks = sorted(ROOT.glob('**/packages*.lock.json'))
    if not locks:
        raise ValueError('No packages.lock.json files found')
    for path in locks:
        for target in json.loads(path.read_text(encoding="utf-8"))['dependencies'].values():
            for name, value in target.items():
                if value['type'].lower() != 'project':
                    result.add((name.lower(), value['resolved']))
    return result

def main():
    data = json.loads((ROOT / 'licenses/inventory.json').read_text(encoding="utf-8"))
    reviewed = {(p['package'].lower(), p['version']) for p in data['packages']}
    actual = locked_packages()
    if actual != reviewed:
        raise ValueError(f'Unreviewed packages: {sorted(actual-reviewed)}; obsolete inventory: {sorted(reviewed-actual)}')
    notices = (ROOT / 'THIRD_PARTY_NOTICES.md').read_text(encoding="utf-8")
    for package in data['packages']:
        label = f"| [{package['package']}](https://www.nuget.org/packages/{package['package']}/{package['version']}) | {package['version']} |"
        if label not in notices:
            raise ValueError(f"Notice table out of date: {package['package']}")
        if not package['notices']:
            raise ValueError(f'Missing notice: {package["package"]}')
        for path in package['notices']:
            if path not in data['files']:
                raise ValueError(f'Unhashed notice: {path}')
    for name, expected in data['files'].items():
        path = (ROOT / name).resolve()
        if ROOT not in path.parents:
            raise ValueError(f'Invalid notice path: {name}')
        if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError(f'Notice changed: {name}')
    print(f'PASS: {len(actual)} locked package versions and {len(data["files"])} notice hashes')

if __name__ == '__main__':
    main()

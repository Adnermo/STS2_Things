from pathlib import Path
import hashlib
import json
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]
TASK = ROOT / 'build/latest-reports-20261013'
BASE = ROOT / 'build/mod-compatibility-20261012/source'
FILES = ('STS2_Things.json', 'STS2_Things.dll', 'STS2_Things.pck', 'STS2_Things.BaseLibBridge.dll')

def digest(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()

def main():
    package = TASK / 'package-final'
    assert json.loads((package / FILES[0]).read_text('utf-8'))['version'] == '1.25.22'
    receipt = json.loads((TASK / 'install-final-receipt.json').read_text('utf-8-sig'))
    assert not receipt['is_error']
    installed = Path(receipt['result']['installed'])
    for item in receipt['result']['files']:
        assert digest(package / item['name']) == item['sha256'] == digest(installed / item['name'])
    changed, unchanged = [], []
    for folder in ('STS2_Things', 'bootstrap'):
        for source in (BASE / folder).rglob('*.cs'):
            rel = source.relative_to(BASE)
            if any(part in ('obj','bin','.godot') for part in rel.parts): continue
            target = TASK / 'source' / rel
            assert target.is_file()
            (unchanged if source.read_bytes() == target.read_bytes() else changed).append(rel.as_posix())
    assert set(changed) == {'STS2_Things/Visuals/NThingsCombatBackground.cs',
                           'STS2_Things/Compatibility/Mods/KnownModAdapters.cs', 'bootstrap/UnifiedBootstrap.cs'}
    new = ['STS2_Things/Compatibility/Mods/MomoLibCompatibility.cs',
           'STS2_Things/Hooks/CaveGodRandomTargetPatch.cs']
    for path in new:
        assert (TASK/'source'/path).read_bytes() == (ROOT/path).read_bytes()
    for target in ('v107.1','v111'):
        text = (TASK/f'final-report-{target}.log').read_text('utf-8')
        assert 'Latest report probe: PASS checks=14' in text and 'ERROR:' not in text
        assert 'Unified startup probe: PASS' in (TASK/f'startup-publish-{target}.log').read_text('utf-8')
        assert 'CaveGod random target regression: PASS' in (TASK/f'random-final-{target}/stdout.log').read_text('utf-8')
        assert not (TASK/f'random-final-{target}/stderr.log').read_text('utf-8').strip()
        for order in ('before','after'):
            output = (TASK/f'final-cute-{target}-{order}/stdout.log').read_text('utf-8')
            assert 'PORTRAIT_COMPAT_PASS checks=39' in output
            assert not (TASK/f'final-cute-{target}-{order}/stderr.log').read_text('utf-8').strip()
        assert 'native-replacement-preserved' in (TASK/f'final-cute-alone-{target}/stdout.log').read_text('utf-8')
    dest = ROOT/'dist/v1.25.22'
    dest.mkdir(parents=True, exist_ok=True)
    archive = dest/'STS2_Things-1.25.22-unified.zip'
    if archive.exists():
        # This unpublished candidate predates the additional random-target fix.
        # Preserve and verify it before replacing the final release filename.
        assert digest(archive) == 'cc58af1cec95340bdad8394e93ade26e941919b227c760d44b57c9c24fc9b5a0', 'Unexpected same-version artifact'
        backup = TASK/'three-fix-candidate'
        assert not backup.exists(), 'Preserve an existing candidate backup'
        backup.mkdir()
        for old in (archive, dest/'SHA256SUMS.txt', TASK/'delivery.json'):
            shutil.copy2(old, backup/old.name)
            assert digest(old) == digest(backup/old.name)
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as zipped:
        for name in FILES: zipped.write(package/name, name)
    with zipfile.ZipFile(archive) as zipped:
        assert zipped.testzip() is None
        for name in FILES: assert hashlib.sha256(zipped.read(name)).hexdigest() == digest(package/name)
    notes = dest/'RELEASE_NOTES.md'
    assert notes.is_file()
    (dest/'SHA256SUMS.txt').write_text('\n'.join([f'{digest(archive)}  {archive.name}', f'{digest(notes)}  {notes.name}'] + [f'{digest(package/name)}  {name}' for name in FILES])+'\n','utf-8')
    result = {'version':'1.25.22', 'baseline_commit':'34475ee5dcc060bf66188321c9fe586e2930614f',
              'changed_cs':changed,'unchanged_cs':len(unchanged),'new_cs':new,'unrelated_wip_excluded':True,
              'zip':str(archive),'zip_sha256':digest(archive),'crc_and_hashes':'PASS',
              'installed':str(installed),'installed_files':receipt['result']['files'],
              'fingerprint':receipt['result']['fingerprint'],'backup':receipt['result']['backup'],
              'game_started':False,'steam_multiplayer_tested':False,'published':False}
    (TASK/'delivery.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),'utf-8')
    print(json.dumps({'version':'1.25.22','zip':str(archive),'installed_hashes':'PASS','source_unchanged':len(unchanged)},ensure_ascii=False))

if __name__ == '__main__': main()
